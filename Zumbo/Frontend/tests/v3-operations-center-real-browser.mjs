import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-surface-007-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-SURFACE-007', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-SURFACE-007 administration');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-SURFACE-007 administration');
const password = 'P@ssword123';
const checks = [];
const failures = [];
let cleanup = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;
let adminToken;

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
  return Object.prototype.hasOwnProperty.call(result.payload, 'data')
    ? result.data
    : result.payload;
}

async function archiveTenant(id, token) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    const result = await apiRequest(`/api/organizations/${encodeURIComponent(id)}/archive`, 'POST', undefined, token);
    if (result.response.ok || result.response.status === 404) {
      return { tenantId: id, status: result.response.status };
    }
    if (result.response.status !== 429) {
      throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
    }
    await new Promise(resolveDelay => setTimeout(resolveDelay, 500));
  }
  throw new Error('Tenant cleanup remained rate limited after bounded retries.');
}

async function browserLogin(context, username, deviceName) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin, 'X-Zumbo-Device-Name': deviceName },
    data: { usernameOrEmail: username, password }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser login failed');
  return payload.data;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    if (/WebSocket|signalr|Failed to load resource/.test(message.text())) return;
    failures.push(`${label}: ${message.text()}`);
  });
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const adminRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `operationsadmin${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Operations administrator registration');
  adminToken = adminRegistration.accessToken;
  const admin = adminRegistration.user;

  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Sistem Operasyonları',
    tenantKey: tenantId
  }, adminToken, 'Operations organization creation');
  cleanupLedger.add(`archive:${tenantId}`, () => archiveTenant(tenantId, adminToken));

  const memberRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `operationsmember${stamp}`,
    email: `operations-member-${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Operations member registration');
  const memberToken = memberRegistration.accessToken;

  const anonymous = await apiRequest('/api/operations/external-dependencies');
  const forbidden = await apiRequest('/api/operations/external-dependencies', 'GET', undefined, memberToken);
  assert.equal(anonymous.response.status, 401);
  assert.equal(forbidden.response.status, 403);
  checks.push('real-global-role-authorization');

  const operationsData = {
    dependencies: await requireApi(
      '/api/operations/external-dependencies',
      'GET',
      undefined,
      adminToken,
      'External dependency status'
    ),
    messaging: await requireApi(
      '/api/work-items/durable-messaging/metrics',
      'GET',
      undefined,
      adminToken,
      'Durable messaging metrics'
    ),
    messageDeadLetters: await requireApi(
      '/api/work-items/durable-messaging/dead-letters?pageSize=20',
      'GET',
      undefined,
      adminToken,
      'Durable message dead letters'
    ),
    notifications: await requireApi(
      `/api/notifications/delivery/status?organizationId=${encodeURIComponent(tenantId)}`,
      'GET',
      undefined,
      adminToken,
      'Notification delivery status'
    ),
    notificationDeadLetters: await requireApi(
      `/api/notifications/delivery/dead-letters?organizationId=${encodeURIComponent(tenantId)}&pageSize=20`,
      'GET',
      undefined,
      adminToken,
      'Notification dead letters'
    ),
    storage: await requireApi(
      `/api/operations/storage/security?organizationId=${encodeURIComponent(tenantId)}`,
      'GET',
      undefined,
      adminToken,
      'Attachment security status'
    )
  };
  const serializedOperations = JSON.stringify(operationsData);
  assert.doesNotMatch(serializedOperations, /connectionString|password|payload|correlation|storagePath|lastError/i);
  assert.ok(Array.isArray(operationsData.dependencies.dependencies));
  assert.ok(Array.isArray(operationsData.messageDeadLetters));
  assert.ok(Array.isArray(operationsData.notificationDeadLetters));
  checks.push('real-safe-bounded-status-projections');

  const reconciliation = await requireApi(
    '/api/work-items/search/reconcile',
    'POST',
    {},
    adminToken,
    'Search reconciliation'
  );
  const maintenance = await requireApi(
    `/api/operations/storage/security/maintenance?organizationId=${encodeURIComponent(tenantId)}`,
    'POST',
    {},
    adminToken,
    'Attachment security maintenance'
  );
  assert.equal(typeof reconciliation.indexed, 'number');
  assert.equal(typeof maintenance.retried, 'number');
  const audit = await requireApi(
    `/api/audit?organizationId=${encodeURIComponent(tenantId)}&pageSize=100`,
    'GET',
    undefined,
    adminToken,
    'Operations audit'
  );
  assert.ok(audit.items.some(item => item.action === 'SearchIndexReconciled'));
  assert.ok(audit.items.some(item => item.action === 'AttachmentSecurityMaintenanceRun'));
  assert.doesNotMatch(JSON.stringify(audit), /password|connectionString|storagePath/i);
  checks.push('real-confirmed-actions-and-audit');

  const desktopContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(desktopContext, admin.username, 'Operations desktop');
  const desktopPage = await desktopContext.newPage();
  diagnostics(desktopPage, 'desktop-admin');
  await desktopPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await desktopPage.getByRole('tab', { name: 'Operasyonlar' }).waitFor({ timeout: 45_000 });
  await desktopPage.getByRole('tab', { name: 'Operasyonlar' }).click();
  const desktopSurface = desktopPage.locator('.operations-center');
  await desktopSurface.locator('.operations-grid').waitFor({ timeout: 45_000 });
  assert.equal(await desktopPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  assert.doesNotMatch(
    await desktopSurface.innerText(),
    /connectionString|password|payload|correlation|storagePath|lastError/i
  );
  await desktopPage.screenshot({ path: resolve(output, 'desktop-operations.png'), fullPage: true });
  checks.push('real-desktop-status-and-responsive-surface');

  const desktopDeniedContext = await browser.newContext({
    viewport: { width: 1280, height: 820 },
    reducedMotion: 'reduce'
  });
  await browserLogin(desktopDeniedContext, memberRegistration.user.username, 'Operations desktop denied');
  const desktopDeniedPage = await desktopDeniedContext.newPage();
  diagnostics(desktopDeniedPage, 'desktop-denied');
  await desktopDeniedPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await desktopDeniedPage.getByRole('heading', { name: 'Ayarlar' }).waitFor({ timeout: 45_000 });
  assert.equal(await desktopDeniedPage.getByRole('tab', { name: 'Operasyonlar' }).count(), 0);
  checks.push('real-desktop-permission-denied');

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(mobileContext, admin.username, 'Operations mobile');
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-admin');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/profile/operations`, {
    waitUntil: 'domcontentloaded'
  });
  const mobileSurface = mobilePage.locator('.mobile-operations-center');
  await mobileSurface.locator('.mobile-operations-panel').first().waitFor({ timeout: 45_000 });
  assert.equal(await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
  assert.doesNotMatch(
    await mobileSurface.innerText(),
    /connectionString|password|payload|correlation|storagePath|lastError/i
  );
  await mobilePage.screenshot({ path: resolve(output, 'mobile-operations.png'), fullPage: true });
  checks.push('real-mobile-status-responsive-and-safe');

  const mobileDeniedContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce'
  });
  await browserLogin(mobileDeniedContext, memberRegistration.user.username, 'Operations mobile denied');
  const mobileDeniedPage = await mobileDeniedContext.newPage();
  diagnostics(mobileDeniedPage, 'mobile-denied');
  await mobileDeniedPage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/profile/operations`, {
    waitUntil: 'domcontentloaded'
  });
  await mobileDeniedPage.locator('.mobile-state.is-error').waitFor({ timeout: 45_000 });
  assert.match(await mobileDeniedPage.locator('.mobile-state.is-error').innerText(), /sistem operasyonu/i);
  checks.push('real-mobile-permission-denied');

  await mobileDeniedContext.close();
  await mobileContext.close();
  await desktopDeniedContext.close();
  await desktopContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  cleanup = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-007',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanup.failed === 0 && checks.length === 7,
    apiBaseUrl,
    frontendBaseUrl,
    checks,
    cleanup,
    failures
  }, null, 2)}\n`);
}

assert.equal(cleanup.failed, 0, `Cleanup failures: ${cleanup.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 7);
console.log('V3-SURFACE-007 real-browser passed: authorization, safe status, audited recovery and desktop/mobile parity.');
