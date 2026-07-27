import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-surface-003-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-SURFACE-003', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-SURFACE-003 tenant cleanup');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-SURFACE-003 tenant cleanup');
const password = 'P@ssword123';
const failures = [];
const checks = [];
let cleanupTokenPromise;
let cleanup = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;

await mkdir(output, { recursive: true });

async function apiRequest(path, method = 'GET', body, token, expectedVersion) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(expectedVersion ? { 'If-Match': `"${expectedVersion}"` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label, expectedVersion) {
  const result = await apiRequest(path, method, body, token, expectedVersion);
  assert.ok(result.response.ok, result.payload.error?.message || `${label} failed with HTTP ${result.response.status}`);
  return result.data;
}

async function archiveTenant() {
  const token = await cleanupTokenPromise;
  const result = await apiRequest(`/api/organizations/${encodeURIComponent(tenantId)}/archive`, 'POST', undefined, token);
  if (result.response.ok || result.response.status === 404) return { tenantId, status: result.response.status };
  throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
}

async function browserLogin(context, usernameOrEmail, deviceName, mfaCode) {
  await context.clearCookies();
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin, 'X-Zumbo-Device-Name': deviceName },
    data: { usernameOrEmail, password, mfaCode: mfaCode || null }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser login failed');
  return payload.data;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (/\/hubs\/work-items|WebSocket|Failed to start the connection|Failed to load resource/.test(text)) return;
    failures.push(`${label}: ${text}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

function decodeBase32(value) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const char of value.replace(/=+$/g, '').toUpperCase()) {
    const index = alphabet.indexOf(char);
    assert.ok(index >= 0, `Invalid base32 character: ${char}`);
    bits += index.toString(2).padStart(5, '0');
  }
  const bytes = [];
  for (let offset = 0; offset + 8 <= bits.length; offset += 8) bytes.push(Number.parseInt(bits.slice(offset, offset + 8), 2));
  return Buffer.from(bytes);
}

function totp(secret, timestamp = Date.now()) {
  const counter = Math.floor(timestamp / 30_000);
  const buffer = Buffer.alloc(8);
  buffer.writeBigUInt64BE(BigInt(counter));
  const digest = createHmac('sha1', decodeBase32(secret)).update(buffer).digest();
  const offset = digest[digest.length - 1] & 0x0f;
  const binary = ((digest[offset] & 0x7f) << 24)
    | (digest[offset + 1] << 16)
    | (digest[offset + 2] << 8)
    | digest[offset + 3];
  return String(binary % 1_000_000).padStart(6, '0');
}

async function eventually(operation, label, attempts = 100, delayMs = 100) {
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const value = await operation();
    if (value) return value;
    await new Promise(resolveDelay => setTimeout(resolveDelay, delayMs));
  }
  throw new Error(`${label} did not become visible after ${attempts} attempts`);
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const registration = await requireApi('/api/auth/register', 'POST', {
    username: `security${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Owner registration');
  const owner = registration.user;
  const ownerToken = registration.accessToken;
  cleanupTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', { name: 'Zumbo Hesap Güvenliği', tenantKey: tenantId }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId, key: `SC${stamp.slice(-5)}`, name: 'Güvenlik İşbirliği', ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', { projectId: project.id, name: 'Güvenlik Panosu', type: 'Kanban' }, ownerToken, 'Board creation');
  const task = await requireApi('/api/work-items', 'POST', {
    projectId: project.id, boardId: board.id, title: `Takip ve oy ${stamp.slice(-4)}`, type: 'Task', priority: 'Medium', assigneeUserId: owner.id
  }, ownerToken, 'Work item creation');
  await eventually(async () => {
    const result = await apiRequest('/api/work-items/search', 'POST', {
      projectId: project.id,
      page: 1,
      pageSize: 20
    }, ownerToken);
    return result.response.ok && result.data.items.some(item => item.id === task.id);
  }, 'Work item search projection');

  const primaryContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await browserLogin(primaryContext, owner.username, 'Ana tarayici');
  const backupContext = await browser.newContext({ viewport: { width: 1100, height: 800 }, reducedMotion: 'reduce' });
  await browserLogin(backupContext, owner.username, 'Yedek tarayici');
  const page = await primaryContext.newPage();
  diagnostics(page, 'desktop');

  await requireApi(`/api/work-items/${task.id}/watch`, 'PUT', { watching: true }, ownerToken, 'Watch ownership');
  await requireApi(`/api/work-items/${task.id}/vote`, 'PUT', { voted: true }, ownerToken, 'Vote ownership');
  const collaboration = await requireApi(`/api/work-items/${task.id}/collaboration`, 'GET', undefined, ownerToken, 'Collaboration read');
  assert.equal(collaboration.watching, true);
  assert.equal(collaboration.voted, true);
  checks.push('real-watch-vote-api-ownership');

  await page.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=settings`, { waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { name: 'Aktif oturumlar' }).waitFor({ timeout: 45_000 });
  assert.equal(await page.locator('.session-row.current').count(), 1);
  const backupRow = page.locator('.session-row').filter({ hasText: 'Yedek tarayici' });
  await backupRow.waitFor();
  page.once('dialog', dialog => dialog.accept());
  await backupRow.getByRole('button', { name: 'Seçilen cihaz oturumunu kapat' }).click();
  await backupRow.getByText('Kapalı', { exact: true }).waitFor();
  const rejectedBackup = await backupContext.request.get(`${apiBaseUrl}/api/browser-auth/session`, { headers: { Origin: frontendOrigin } });
  assert.equal(rejectedBackup.status(), 401);
  checks.push('real-targeted-session-revoke');

  const savedPreferences = await requireApi('/api/notifications/preferences/me', 'PUT', {
    inAppEnabled: true,
    emailEnabled: false,
    mutedTypes: ['Assignment', 'Mention']
  }, ownerToken, 'Preference update');
  assert.deepEqual(savedPreferences.mutedTypes, ['Assignment', 'Mention']);
  checks.push('real-notification-preferences');

  const mfaBand = page.locator('.settings-band').filter({ has: page.getByRole('heading', { name: 'İki adımlı doğrulama' }) });
  await mfaBand.getByLabel('MFA kurulum parolası').fill(password);
  await mfaBand.getByRole('button', { name: 'Kurulumu başlat' }).click();
  const secret = (await mfaBand.locator('.secret-output code').textContent()).trim();
  await mfaBand.getByLabel('MFA doğrulama kodu').fill(totp(secret));
  await mfaBand.getByRole('button', { name: 'Etkinleştir' }).click();
  await mfaBand.getByText(/8 kurtarma kodu/).waitFor();
  const originalCodes = await mfaBand.locator('.recovery-output code').allTextContents();
  assert.equal(originalCodes.length, 8);
  await mfaBand.getByRole('button', { name: 'Kurtarma kodlarını kapat' }).click();
  const postMfaApiLogin = await apiRequest('/api/auth/login', 'POST', {
    usernameOrEmail: owner.username,
    password,
    mfaCode: originalCodes[0]
  });
  assert.equal(postMfaApiLogin.response.status, 200);
  cleanupTokenPromise = Promise.resolve(postMfaApiLogin.data.accessToken);
  await browserLogin(primaryContext, owner.username, 'Ana tarayici', originalCodes[1]);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { name: 'Aktif oturumlar' }).waitFor();
  const safeStatusResponse = await primaryContext.request.get(`${apiBaseUrl}/api/auth/mfa`, { headers: { Origin: frontendOrigin } });
  const safeStatus = await safeStatusResponse.json();
  assert.ok(safeStatusResponse.ok(), safeStatus.error?.message || 'MFA status failed');
  assert.equal(Object.hasOwn(safeStatus.data, 'secret'), false);
  assert.equal(Object.hasOwn(safeStatus.data, 'recoveryCodes'), false);
  checks.push('real-mfa-secret-once-confirm');

  await mfaBand.getByLabel('Kurtarma kodu yenileme parolası').fill(password);
  await mfaBand.getByLabel('Kurtarma kodu yenileme doğrulaması').fill(totp(secret));
  page.once('dialog', dialog => dialog.accept());
  await mfaBand.getByRole('button', { name: 'Kodları yenile' }).click();
  await mfaBand.locator('.recovery-output').waitFor();
  const newCodes = await mfaBand.locator('.recovery-output code').allTextContents();
  assert.equal(newCodes.length, 8);
  assert.notDeepEqual(newCodes, originalCodes);
  const oldRecoveryLogin = await apiRequest('/api/auth/login', 'POST', { usernameOrEmail: owner.username, password, mfaCode: originalCodes[2] });
  assert.equal(oldRecoveryLogin.response.status, 401);
  const newRecoveryLogin = await apiRequest('/api/auth/login', 'POST', { usernameOrEmail: owner.username, password, mfaCode: newCodes[0] });
  assert.equal(newRecoveryLogin.response.status, 200);
  await requireApi('/api/auth/mfa/disable', 'POST', { password, code: totp(secret) }, newRecoveryLogin.data.accessToken, 'MFA disable');
  const postDisableLogin = await apiRequest('/api/auth/login', 'POST', { usernameOrEmail: owner.username, password });
  assert.equal(postDisableLogin.response.status, 200);
  cleanupTokenPromise = Promise.resolve(postDisableLogin.data.accessToken);
  await browserLogin(primaryContext, owner.username, 'Ana tarayici');
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { name: 'Aktif oturumlar' }).waitFor();
  assert.equal(await page.locator('.recovery-output').count(), 0);
  checks.push('real-recovery-rotation-invalidates-old');
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: resolve(output, 'desktop-security.png'), fullPage: true });

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
  await browserLogin(mobileContext, owner.username, 'Mobil cihaz');
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile');
  await mobile.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/app/profile`, { waitUntil: 'domcontentloaded' });
  await mobile.getByRole('heading', { name: 'Aktif oturumlar' }).waitFor({ timeout: 45_000 });
  assert.equal(await mobile.locator('.mobile-session-row.current').count(), 1);
  assert.equal(await mobile.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
  const mobileSessions = mobile.locator('.mobile-settings-band').filter({ has: mobile.getByRole('heading', { name: 'Aktif oturumlar' }) });
  await mobileSessions.screenshot({ path: resolve(output, 'mobile-sessions.png') });
  checks.push('real-mobile-security-parity-no-overflow');

  await mobileContext.close();
  await backupContext.close();
  await primaryContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanup = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-003',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanup.failed === 0 && checks.length === 6,
    apiBaseUrl,
    frontendBaseUrl,
    checks,
    cleanup,
    failures
  }, null, 2)}\n`);
}

assert.equal(cleanup.failed, 0, `Cleanup failures: ${cleanup.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 6);
console.log('V3-SURFACE-003 real-browser passed: watch/vote, sessions, preferences, MFA recovery and mobile parity.');
