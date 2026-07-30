import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-surface-005-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-SURFACE-005', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-SURFACE-005 tenant cleanup');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-SURFACE-005 tenant cleanup');
const password = 'P@ssword123';
const checks = [];
const failures = [];
let cleanupTokenPromise;
let cleanup = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;

await mkdir(output, { recursive: true });

async function apiRequest(path, method = 'GET', body, token) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label) {
  const result = await apiRequest(path, method, body, token);
  assert.ok(result.response.ok, result.payload.error?.message || `${label} failed with HTTP ${result.response.status}`);
  return result.data;
}

async function eventually(operation, label, attempts = 180, delayMs = 100) {
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const value = await operation();
    if (value) return value;
    await new Promise(resolveDelay => setTimeout(resolveDelay, delayMs));
  }
  throw new Error(`${label} did not become observable after ${attempts} attempts`);
}

async function archiveTenant() {
  const token = await cleanupTokenPromise;
  const result = await apiRequest(
    `/api/organizations/${encodeURIComponent(tenantId)}/archive`,
    'POST',
    undefined,
    token
  );
  if (result.response.ok || result.response.status === 404) {
    return { tenantId, status: result.response.status };
  }
  throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
}

async function browserLogin(context, usernameOrEmail, deviceName) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin, 'X-Zumbo-Device-Name': deviceName },
    data: { usernameOrEmail, password }
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

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `auditowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Audit owner registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupTokenPromise = Promise.resolve(ownerToken);

  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Denetim Laboratuvarı',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `AU${stamp.slice(-5)}`,
    name: 'Uyum Kanıt Akışı',
    ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Denetim Panosu',
    type: 'Kanban'
  }, ownerToken, 'Board creation');
  const workItem = await requireApi('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: `Denetim kanıtı ${stamp}`,
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id
  }, ownerToken, 'Work item creation');
  assert.ok(workItem.id);

  const privacyRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `privacyuser${stamp}`,
    email: `privacy-${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Privacy user registration');
  const privacyUser = privacyRegistration.user;
  const privacyToken = privacyRegistration.accessToken;

  const auditPageData = await eventually(async () => {
    const result = await apiRequest(
      `/api/audit?organizationId=${encodeURIComponent(tenantId)}&pageSize=100`,
      'GET',
      undefined,
      ownerToken
    );
    return result.response.ok && result.data.items.some(entry => entry.entityId === workItem.id)
      ? result.data
      : null;
  }, 'Audit projection');
  assert.ok(auditPageData.items.length >= 3);
  checks.push('real-audit-projection');

  const crossTenant = await apiRequest(
    '/api/audit?organizationId=another-tenant&pageSize=10',
    'GET',
    undefined,
    privacyToken
  );
  assert.equal(crossTenant.response.status, 403);
  const deniedAudit = await apiRequest(
    `/api/audit?organizationId=${encodeURIComponent(tenantId)}&pageSize=10`,
    'GET',
    undefined,
    privacyToken
  );
  assert.equal(deniedAudit.response.status, 403);
  checks.push('real-audit-authorization-and-tenant-boundary');

  const exportResponse = await fetch(
    `${apiBaseUrl}/api/audit/export?organizationId=${encodeURIComponent(tenantId)}`,
    { headers: { Authorization: `Bearer ${ownerToken}` } }
  );
  assert.equal(exportResponse.status, 200);
  assert.equal(exportResponse.headers.get('content-type'), 'application/x-ndjson');
  assert.equal(exportResponse.headers.get('cache-control'), 'no-store');
  assert.equal(exportResponse.headers.get('x-zumbo-export-format'), 'audit-ndjson-v1');
  const exportLines = (await exportResponse.text()).trim().split('\n').filter(Boolean);
  assert.equal(Number(exportResponse.headers.get('x-zumbo-export-records')), exportLines.length);
  assert.ok(exportLines.length >= auditPageData.items.length);
  assert.doesNotMatch(exportLines.join('\n'), new RegExp(password.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  for (const line of exportLines) JSON.parse(line);
  checks.push('real-complete-redacted-ndjson-export');

  const integrity = await requireApi(
    `/api/audit/integrity/${encodeURIComponent(tenantId)}`,
    'GET',
    undefined,
    ownerToken,
    'Audit integrity'
  );
  assert.equal(integrity.valid, true);
  assert.ok(integrity.verified > 0, 'Audit integrity must verify at least one hashed record');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul',
    acceptDownloads: true
  });
  await browserLogin(ownerContext, owner.username, 'Audit browser');
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'desktop-owner');
  await ownerPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=audit`, {
    waitUntil: 'domcontentloaded'
  });
  await ownerPage.getByRole('heading', { name: 'Denetim merkezi' }).waitFor({ timeout: 45_000 });
  await ownerPage.locator('.audit-event-list > button').first().waitFor();
  await ownerPage.getByRole('button', { name: 'Bütünlüğü doğrula' }).click();
  await ownerPage.getByText(
    /Denetim zinciri doğrulandı|Saklanan kayıt aralığı doğrulandı/
  ).waitFor();
  assert.equal(await ownerPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await ownerPage.screenshot({ path: resolve(output, 'desktop-audit.png'), fullPage: true });
  checks.push('real-desktop-search-and-integrity');

  const deniedContext = await browser.newContext({
    viewport: { width: 1280, height: 820 },
    reducedMotion: 'reduce'
  });
  await browserLogin(deniedContext, privacyUser.username, 'Denied audit browser');
  const deniedPage = await deniedContext.newPage();
  diagnostics(deniedPage, 'desktop-denied');
  await deniedPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=audit`, {
    waitUntil: 'domcontentloaded'
  });
  await deniedPage.getByText('Organizasyon denetim kayıtlarını görüntüleme yetkiniz yok.').waitFor({
    timeout: 45_000
  });
  assert.equal(await deniedPage.locator('.nav-item').filter({ hasText: 'Denetim' }).count(), 0);
  checks.push('real-desktop-permission-loss-state');

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul',
    acceptDownloads: true
  });
  await browserLogin(mobileContext, privacyUser.username, 'Privacy mobile');
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-privacy');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/app/profile`, {
    waitUntil: 'domcontentloaded'
  });
  await mobilePage.getByRole('heading', { name: 'Gizlilik ve hesap' }).waitFor({ timeout: 45_000 });
  const privacyDownloadPromise = mobilePage.waitForEvent('download');
  await mobilePage.getByRole('button', { name: 'Verilerimi NDJSON olarak aktar' }).click();
  const privacyDownload = await privacyDownloadPromise;
  assert.equal(privacyDownload.suggestedFilename(), 'zumbo-privacy-export.ndjson');

  await mobilePage.getByLabel('Anonimleştirme parolası').fill(password);
  await mobilePage.getByLabel('Anonimleştirme onayı').fill('ANONYMIZE');
  await mobilePage.getByRole('button', { name: 'Hesabı anonimleştir' }).click();
  await mobilePage.locator('.popup-buttons .button-positive').click();
  await mobilePage.getByText('Tamamlandı', { exact: true }).waitFor({ timeout: 45_000 });
  await mobilePage.getByRole('button', {
    name: 'Gizlilik işi durumunu kapat',
    exact: true
  }).waitFor();
  const receipt = await mobilePage.evaluate(user => {
    const key = `zumbo.privacy.workflow.${encodeURIComponent(user.organizationId)}.${encodeURIComponent(user.id)}`;
    return JSON.parse(sessionStorage.getItem(key));
  }, privacyUser);
  assert.ok(receipt.id);
  assert.match(receipt.statusToken, /^[A-Za-z0-9_-]{20,128}$/);
  const bodyText = await mobilePage.locator('body').innerText();
  assert.doesNotMatch(bodyText, new RegExp(receipt.statusToken));
  assert.doesNotMatch(mobilePage.url(), new RegExp(receipt.statusToken));
  assert.doesNotMatch(await mobilePage.evaluate(() => JSON.stringify(localStorage)), new RegExp(receipt.statusToken));
  assert.equal(await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
  await mobilePage.locator('.mobile-privacy-band').screenshot({ path: resolve(output, 'mobile-privacy.png') });

  const publicStatus = await mobileContext.request.get(
    `${apiBaseUrl}/api/auth/privacy/jobs/${encodeURIComponent(receipt.id)}/status`,
    { headers: { 'X-Privacy-Status-Token': receipt.statusToken, Origin: frontendOrigin } }
  );
  assert.equal(publicStatus.status(), 200);
  const invalidStatus = await mobileContext.request.get(
    `${apiBaseUrl}/api/auth/privacy/jobs/${encodeURIComponent(receipt.id)}/status`,
    { headers: { 'X-Privacy-Status-Token': 'invalid_status_token_123456789', Origin: frontendOrigin } }
  );
  assert.equal(invalidStatus.status(), 404);
  const revokedOwnedRead = await mobileContext.request.get(
    `${apiBaseUrl}/api/auth/privacy/jobs/${encodeURIComponent(receipt.id)}`,
    { headers: { Origin: frontendOrigin } }
  );
  assert.equal(revokedOwnedRead.status(), 401);
  checks.push('real-mobile-export-confirmation-token-status-and-revocation');

  await mobileContext.close();
  await deniedContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanup = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-005',
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
console.log('V3-SURFACE-005 real-browser passed: audit authorization/export/integrity and durable privacy completion.');
