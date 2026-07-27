import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-surface-006-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-SURFACE-006', 'chromium');
const tenantId = runContext.tenants.desktop;
const foreignTenantId = `${tenantId}-foreign`;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-SURFACE-006 tenant cleanup');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-SURFACE-006 tenant cleanup');
const password = 'P@ssword123';
const checks = [];
const failures = [];
let cleanup = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;
let ownerToken;
let foreignToken;

await mkdir(output, { recursive: true });

const receiverStatuses = [204, 500, 500, 204];
const receiverRequests = [];
const receiver = createServer((request, response) => {
  const chunks = [];
  request.on('data', chunk => chunks.push(chunk));
  request.on('end', () => {
    receiverRequests.push({
      headers: { ...request.headers },
      body: Buffer.concat(chunks).toString('utf8')
    });
    const status = receiverStatuses.shift() || 204;
    response.writeHead(status, { 'Content-Length': '0' });
    response.end();
  });
});
await new Promise((resolveListen, reject) => {
  receiver.once('error', reject);
  receiver.listen(0, '127.0.0.1', resolveListen);
});
const receiverUrl = `http://127.0.0.1:${receiver.address().port}/webhooks?receiver=surface006`;

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

async function eventually(operation, label, attempts = 300, delayMs = 120) {
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const value = await operation();
    if (value) return value;
    await new Promise(resolveDelay => setTimeout(resolveDelay, delayMs));
  }
  throw new Error(`${label} did not become observable after ${attempts} attempts`);
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

async function deliveryWithStatus(subscriptionId, status, excludedId) {
  return eventually(async () => {
    const page = await requireApi(
      `/api/integrations/webhooks/${subscriptionId}/deliveries?pageSize=100`,
      'GET',
      undefined,
      ownerToken,
      'Webhook deliveries'
    );
    return page.items.find(item => item.status === status && item.id !== excludedId) || null;
  }, `Webhook delivery ${status}`);
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
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `hookowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Webhook owner registration');
  ownerToken = ownerRegistration.accessToken;
  const owner = ownerRegistration.user;
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Entegrasyon Operasyonları',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, () => archiveTenant(tenantId, ownerToken));

  const memberRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `hookmember${stamp}`,
    email: `hookmember-${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Webhook member registration');

  const foreignRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `hookforeign${stamp}`,
    email: `hookforeign-${stamp}@zumbo.local`,
    password,
    organizationId: foreignTenantId
  }, undefined, 'Foreign registration');
  foreignToken = foreignRegistration.accessToken;
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Yabancı Organizasyon',
    tenantKey: foreignTenantId
  }, foreignToken, 'Foreign organization creation');
  cleanupLedger.add(`archive:${foreignTenantId}`, () => archiveTenant(foreignTenantId, foreignToken));

  const desktopContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(desktopContext, owner.username, 'Webhook desktop');
  const desktopPage = await desktopContext.newPage();
  diagnostics(desktopPage, 'desktop-owner');
  desktopPage.on('dialog', dialog => dialog.accept());
  await desktopPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await desktopPage.getByRole('tab', { name: 'Entegrasyonlar' }).waitFor({ timeout: 45_000 });
  await desktopPage.getByRole('tab', { name: 'Entegrasyonlar' }).click();
  await desktopPage.getByRole('heading', { name: 'Webhook yönetimi' }).waitFor();
  await desktopPage.locator('.integration-heading').getByRole('button', { name: 'Webhook ekle' }).click();
  await desktopPage.getByLabel('Ad', { exact: true }).fill('Gerçek alıcı');
  await desktopPage.getByLabel('HTTPS uç noktası').fill(receiverUrl);
  await desktopPage.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await desktopPage.getByText('Bu sır yalnız şimdi gösterilir', { exact: true }).waitFor();
  const originalSecret = await desktopPage.locator('.integration-secret code').innerText();
  const subscription = await eventually(async () => {
    const values = await requireApi('/api/integrations/webhooks', 'GET', undefined, ownerToken, 'Webhook list');
    return values.find(item => item.name === 'Gerçek alıcı') || null;
  }, 'Created webhook');
  assert.doesNotMatch(await desktopPage.locator('.integration-detail').innerText(), /receiver=surface006/);
  await desktopPage.getByRole('button', { name: 'Webhook sırrını kapat' }).click();

  await desktopPage.getByRole('button', { name: 'Test gönder' }).click();
  const delivered = await deliveryWithStatus(subscription.id, 'Delivered');
  await eventually(() => receiverRequests.length >= 1 ? receiverRequests[0] : null, 'Successful receiver request');
  const firstPayload = JSON.parse(receiverRequests[0].body);
  assert.equal(firstPayload.type, 'webhook.test');
  assert.equal(firstPayload.data.test, true);
  assert.equal(Object.hasOwn(firstPayload, 'workItem'), false);
  const firstTimestamp = receiverRequests[0].headers['x-zumbo-webhook-timestamp'];
  const firstSignature = createHmac('sha256', originalSecret)
    .update(`${firstTimestamp}.${receiverRequests[0].body}`)
    .digest('hex');
  assert.equal(receiverRequests[0].headers['x-zumbo-webhook-signature'], `v1=${firstSignature}`);
  checks.push('real-ui-create-safe-signed-test-delivery');

  await desktopPage.getByRole('button', { name: 'Sırrı döndür' }).click();
  await desktopPage.getByText('Bu sır yalnız şimdi gösterilir', { exact: true }).waitFor();
  const rotatedSecret = await desktopPage.locator('.integration-secret code').innerText();
  assert.notEqual(rotatedSecret, originalSecret);
  assert.doesNotMatch(await desktopPage.evaluate(() => JSON.stringify({
    local: { ...localStorage },
    session: { ...sessionStorage }
  })), new RegExp(rotatedSecret));
  await desktopPage.getByRole('button', { name: 'Webhook sırrını kapat' }).click();

  await desktopPage.getByRole('button', { name: 'Test gönder' }).click();
  const deadLetter = await deliveryWithStatus(subscription.id, 'DeadLetter', delivered.id);
  assert.equal(deadLetter.attempts, 2);
  const rotatedRequests = receiverRequests.filter(item =>
    item.headers['x-zumbo-webhook-id'] === deadLetter.id);
  assert.equal(rotatedRequests.length, 2);
  assert.equal(rotatedRequests[0].headers['x-zumbo-webhook-previous-secret-version'], '1');
  checks.push('real-secret-rotation-overlap-and-secret-once');

  await desktopPage.getByRole('button', { name: 'Teslimatları yenile' }).click();
  await desktopPage.getByText('Müdahale gerekli', { exact: true }).waitFor();
  await desktopPage.getByRole('button', { name: 'Teslimatı yeniden sırala' }).click();
  const replayed = await eventually(async () => {
    const page = await requireApi(
      `/api/integrations/webhooks/${subscription.id}/deliveries?pageSize=100`,
      'GET',
      undefined,
      ownerToken,
      'Replayed delivery'
    );
    return page.items.find(item => item.id === deadLetter.id && item.status === 'Delivered') || null;
  }, 'Webhook replay');
  const replayBodies = receiverRequests
    .filter(item => item.headers['x-zumbo-webhook-id'] === deadLetter.id)
    .map(item => item.body);
  assert.equal(replayed.status, 'Delivered');
  assert.equal(new Set(replayBodies).size, 1);
  checks.push('real-dead-letter-ui-replay-immutable-payload');

  await desktopPage.getByRole('button', { name: 'Durdur', exact: true }).click();
  await desktopPage.getByText('Durduruldu', { exact: true }).waitFor();
  assert.equal(await desktopPage.getByRole('button', { name: 'Test gönder' }).isDisabled(), true);
  const disabledTest = await apiRequest(
    `/api/integrations/webhooks/${subscription.id}/test-delivery`,
    'POST',
    {},
    ownerToken
  );
  assert.equal(disabledTest.response.status, 409);
  assert.equal(disabledTest.payload.error.code, 'WEBHOOK_SUBSCRIPTION_DISABLED');
  checks.push('real-disabled-subscription-fails-closed');

  const privateTarget = await apiRequest('/api/integrations/webhooks', 'POST', {
    name: 'Blocked metadata',
    targetUrl: 'https://169.254.169.254/latest/meta-data',
    eventScopes: ['work-item.created']
  }, ownerToken);
  assert.equal(privateTarget.response.status, 400);
  const denied = await apiRequest('/api/integrations/webhooks', 'GET', undefined, memberRegistration.accessToken);
  assert.equal(denied.response.status, 403);
  const foreign = await apiRequest(
    `/api/integrations/webhooks/${subscription.id}`,
    'GET',
    undefined,
    foreignToken
  );
  assert.equal(foreign.response.status, 404);
  checks.push('real-ssrf-permission-and-tenant-boundaries');

  const listText = await (await fetch(`${apiBaseUrl}/api/integrations/webhooks`, {
    headers: { Authorization: `Bearer ${ownerToken}` }
  })).text();
  assert.doesNotMatch(listText, new RegExp(originalSecret));
  assert.doesNotMatch(listText, new RegExp(rotatedSecret));
  const auditPage = await eventually(async () => {
    const result = await apiRequest(
      `/api/audit?organizationId=${encodeURIComponent(tenantId)}&pageSize=100`,
      'GET',
      undefined,
      ownerToken
    );
    if (!result.response.ok) return null;
    const actions = result.data.items.map(item => item.action);
    return actions.includes('WebhookSubscriptionCreated')
      && actions.includes('WebhookTestDeliveryQueued')
      && actions.includes('WebhookDeliveryReplayed')
      ? result.data
      : null;
  }, 'Webhook audit actions', 30, 500);
  assert.doesNotMatch(JSON.stringify(auditPage), /whsec_/);
  checks.push('real-audit-correlation-and-secret-redaction');

  assert.equal(await desktopPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await desktopPage.screenshot({ path: resolve(output, 'desktop-integrations.png'), fullPage: true });

  const mobileAdminContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(mobileAdminContext, owner.username, 'Webhook mobile admin');
  const mobileAdminPage = await mobileAdminContext.newPage();
  diagnostics(mobileAdminPage, 'mobile-admin');
  await mobileAdminPage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/profile/integrations`, {
    waitUntil: 'domcontentloaded'
  });
  await mobileAdminPage.getByRole('heading', { name: 'Webhook yönetimi' }).waitFor({ timeout: 45_000 });
  await mobileAdminPage.locator('.mobile-webhook-row').filter({ hasText: 'Gerçek alıcı' }).click();
  await mobileAdminPage.getByText('Durduruldu', { exact: true }).waitFor();
  assert.equal(await mobileAdminPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
  await mobileAdminPage.screenshot({ path: resolve(output, 'mobile-integrations.png'), fullPage: true });

  const mobileDeniedContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce'
  });
  await browserLogin(mobileDeniedContext, memberRegistration.user.username, 'Webhook mobile denied');
  const mobileDeniedPage = await mobileDeniedContext.newPage();
  diagnostics(mobileDeniedPage, 'mobile-denied');
  await mobileDeniedPage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/profile/integrations`, {
    waitUntil: 'domcontentloaded'
  });
  await mobileDeniedPage.getByText('Bu alan için Entegrasyon Yönetimi izni gereklidir.').waitFor({
    timeout: 45_000
  });
  checks.push('real-mobile-admin-parity-and-permission-denial');

  await mobileDeniedContext.close();
  await mobileAdminContext.close();
  await desktopContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanup = await cleanupLedger.run();
  await browser?.close();
  await new Promise(resolveClose => receiver.close(resolveClose));
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-006',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanup.failed === 0 && checks.length === 7,
    apiBaseUrl,
    frontendBaseUrl,
    receiverRequests: receiverRequests.length,
    checks,
    cleanup,
    failures
  }, null, 2)}\n`);
}

assert.equal(cleanup.failed, 0, `Cleanup failures: ${cleanup.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 7);
console.log('V3-SURFACE-006 real-browser passed: signed test delivery, rotation, replay, boundaries, audit and mobile parity.');
