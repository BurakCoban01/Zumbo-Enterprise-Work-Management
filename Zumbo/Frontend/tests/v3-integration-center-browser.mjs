import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-surface-006');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = new Date().toISOString();
const admin = {
  id: 'integration-admin-1',
  username: 'derya',
  email: 'derya@zumbo.local',
  organizationId: 'org-integration',
  roles: ['IntegrationOperator']
};
const user = {
  id: 'integration-user-1',
  username: 'emre',
  email: 'emre@zumbo.local',
  organizationId: 'org-integration',
  roles: ['User']
};
const roles = [
  { name: 'IntegrationOperator', permissions: ['IntegrationManage'] },
  { name: 'User', permissions: [] }
];
const checks = [];
const failures = [];
let subscriptions = [];
let deliveries = [];
let sequence = 0;
let afterTest = false;
let deliveryReads = 0;

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-surface-006' });
}

function metrics() {
  return {
    pending: deliveries.filter(item => item.status === 'Pending').length,
    processing: deliveries.filter(item => item.status === 'Processing').length,
    delivered: deliveries.filter(item => item.status === 'Delivered').length,
    deadLetter: deliveries.filter(item => item.status === 'DeadLetter').length,
    oldestPendingAtUtc: null,
    capturedAtUtc: now
  };
}

function subscription(name, targetUrl) {
  sequence += 1;
  return {
    id: `webhook-${sequence}`,
    name,
    targetUrl,
    eventScopes: ['work-item.created'],
    isActive: true,
    secretFingerprint: `fingerprint-${sequence}`,
    secretVersion: 1,
    createdAtUtc: now,
    updatedAtUtc: now,
    version: 1
  };
}

function delivery(status = 'Pending') {
  sequence += 1;
  return {
    id: `delivery-${sequence}`,
    subscriptionId: subscriptions[0].id,
    eventScope: 'webhook.test',
    payloadSha256: '4d3c2b1a'.repeat(8),
    status,
    attempts: status === 'DeadLetter' ? 3 : 0,
    nextAttemptAtUtc: now,
    lastErrorCode: status === 'DeadLetter' ? 'HTTP_503' : null,
    deliveredAtUtc: null,
    deadLetteredAtUtc: status === 'DeadLetter' ? now : null,
    createdAtUtc: now,
    version: 1
  };
}

async function createContext(currentUser, viewport) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user: currentUser, csrfToken: 'csrf-surface-006' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    let data = [];

    if (path === '/api/browser-auth/session') data = { user: currentUser, csrfToken: 'csrf-surface-006' };
    else if (path === '/api/auth/roles') data = roles;
    else if (path === '/api/auth/users') data = [admin, user];
    else if (path === '/api/auth/sessions' || path === '/api/auth/api-keys') data = [];
    else if (path === '/api/auth/mfa') data = { enabled: false, remainingRecoveryCodes: 0 };
    else if (path === '/api/notifications/preferences/me') {
      data = { inAppEnabled: true, emailEnabled: false, mutedTypes: [] };
    } else if (path === '/api/organizations') {
      data = [{ id: 'org-integration', tenantKey: 'org-integration', name: 'Zumbo Operasyon' }];
    } else if (path === '/api/projects' || path === '/api/teams') data = [];
    else if (path === '/api/integrations/webhooks' && method === 'GET') data = subscriptions;
    else if (path === '/api/integrations/webhooks/metrics') data = metrics();
    else if (path === '/api/integrations/webhooks' && method === 'POST') {
      const body = request.postDataJSON();
      const created = subscription(body.name, body.targetUrl);
      subscriptions.unshift(created);
      data = { subscription: created, secret: `whsec_surface_once_${sequence}` };
    } else if (/^\/api\/integrations\/webhooks\/[^/]+\/deliveries$/.test(path)) {
      deliveryReads += 1;
      if (afterTest && deliveryReads >= 1 && deliveries[0]?.status === 'Pending') {
        deliveries[0] = { ...deliveries[0], status: 'DeadLetter', attempts: 3, lastErrorCode: 'HTTP_503', deadLetteredAtUtc: now };
      }
      data = { items: deliveries, nextCursor: null };
    } else if (/^\/api\/integrations\/webhooks\/[^/]+\/test-delivery$/.test(path)) {
      const selected = subscriptions.find(item => path.includes(item.id));
      if (!selected?.isActive) {
        return route.fulfill({
          status: 409,
          contentType: 'application/json',
          body: JSON.stringify({
            success: false,
            data: null,
            error: { code: 'WEBHOOK_SUBSCRIPTION_DISABLED', message: 'Webhook devre dışı.' },
            correlationId: 'v3-surface-006'
          })
        });
      }
      const queued = delivery();
      deliveries.unshift(queued);
      afterTest = true;
      deliveryReads = 0;
      data = queued;
    } else if (/\/rotate-secret$/.test(path)) {
      const selected = subscriptions.find(item => path.includes(item.id));
      Object.assign(selected, {
        secretVersion: selected.secretVersion + 1,
        version: selected.version + 1,
        secretFingerprint: 'rotated-fingerprint'
      });
      data = { subscription: { ...selected }, secret: 'whsec_rotated_once' };
    } else if (/\/(enable|disable)$/.test(path)) {
      const selected = subscriptions.find(item => path.includes(item.id));
      selected.isActive = path.endsWith('/enable');
      selected.version += 1;
      data = { ...selected };
    } else if (/\/deliveries\/[^/]+\/replay$/.test(path)) {
      const selected = deliveries.find(item => path.includes(item.id));
      Object.assign(selected, {
        status: 'Pending',
        attempts: 0,
        lastErrorCode: null,
        deadLetteredAtUtc: null
      });
      afterTest = false;
      data = { ...selected };
    } else if (/^\/api\/integrations\/webhooks\/[^/]+$/.test(path) && method === 'PUT') {
      const selected = subscriptions.find(item => path.endsWith(item.id));
      Object.assign(selected, request.postDataJSON(), { version: selected.version + 1, updatedAtUtc: now });
      data = { ...selected };
    } else if (/^\/api\/integrations\/webhooks\/[^/]+$/.test(path)) {
      data = subscriptions.find(item => path.endsWith(item.id)) || null;
    }

    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error' && !/WebSocket|signalr|Failed to load resource/.test(message.text())) {
      failures.push(`${label}: ${message.text()}`);
    }
  });
}

try {
  const desktopContext = await createContext(admin, { width: 1440, height: 1000 });
  const desktopPage = await desktopContext.newPage();
  diagnostics(desktopPage, 'desktop-admin');
  desktopPage.on('dialog', dialog => dialog.accept());
  await desktopPage.goto(`${server.origin}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await desktopPage.getByRole('tab', { name: 'Entegrasyonlar' }).waitFor();
  await desktopPage.getByRole('tab', { name: 'Entegrasyonlar' }).click();
  await desktopPage.getByRole('heading', { name: 'Webhook yönetimi' }).waitFor();
  checks.push('desktop-role-gated-integration-center');

  await desktopPage.locator('.integration-heading').getByRole('button', { name: 'Webhook ekle' }).click();
  await desktopPage.getByLabel('Ad', { exact: true }).fill('Teslimat akışı');
  await desktopPage.getByLabel('HTTPS uç noktası').fill('https://hooks.example.test/zumbo?token=private-value');
  await desktopPage.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await desktopPage.getByText('Bu sır yalnız şimdi gösterilir', { exact: true }).waitFor();
  const secretText = await desktopPage.locator('.integration-secret code').innerText();
  assert.match(secretText, /^whsec_/);
  assert.doesNotMatch(await desktopPage.locator('.integration-detail').innerText(), /private-value/);
  const storageText = await desktopPage.evaluate(() => JSON.stringify({
    local: { ...localStorage },
    session: { ...sessionStorage }
  }));
  assert.doesNotMatch(storageText, new RegExp(secretText));
  await desktopPage.getByRole('button', { name: 'Webhook sırrını kapat' }).click();
  assert.equal(await desktopPage.getByText(secretText, { exact: true }).count(), 0);
  checks.push('desktop-create-secret-once-and-target-redaction');

  await desktopPage.getByRole('button', { name: 'Test gönder' }).click();
  await desktopPage.getByText('Müdahale gerekli', { exact: true }).waitFor({ timeout: 8_000 });
  assert.match(await desktopPage.locator('.delivery-register').innerText(), /Alıcı geçici olarak kullanılamıyor/);
  assert.doesNotMatch(await desktopPage.locator('.delivery-register').innerText(), /private-value|payload/i);
  checks.push('desktop-safe-test-and-dead-letter-state');

  await desktopPage.getByRole('button', { name: 'Teslimatı yeniden sırala' }).click();
  await desktopPage.locator('.delivery-row').getByText('Sırada', { exact: true }).waitFor();
  checks.push('desktop-confirmed-dead-letter-replay');

  await desktopPage.getByRole('button', { name: 'Durdur', exact: true }).click();
  await desktopPage.getByText('Durduruldu', { exact: true }).waitFor();
  assert.equal(await desktopPage.getByRole('button', { name: 'Test gönder' }).isDisabled(), true);
  checks.push('desktop-disabled-subscription-blocks-test');
  assert.equal(await desktopPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await desktopPage.screenshot({ path: resolve(output, 'desktop-integrations.png'), fullPage: true });
  await desktopContext.close();

  const deniedContext = await createContext(user, { width: 1280, height: 820 });
  const deniedPage = await deniedContext.newPage();
  diagnostics(deniedPage, 'desktop-denied');
  await deniedPage.goto(`${server.origin}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await deniedPage.getByRole('heading', { name: 'Ayarlar' }).waitFor();
  assert.equal(await deniedPage.getByRole('tab', { name: 'Entegrasyonlar' }).count(), 0);
  assert.equal(await deniedPage.locator('.integration-center').count(), 0);
  checks.push('desktop-permission-denied');
  await deniedContext.close();

  const mobileContext = await createContext(admin, { width: 390, height: 844 });
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-admin');
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/profile/integrations`, {
    waitUntil: 'domcontentloaded'
  });
  await mobilePage.getByRole('heading', { name: 'Webhook yönetimi' }).waitFor();
  await mobilePage.locator('.mobile-webhook-row').first().click();
  await mobilePage.getByText('Durduruldu', { exact: true }).waitFor();
  assert.equal(await mobilePage.getByRole('button', { name: 'Test gönder' }).isDisabled(), true);
  assert.doesNotMatch(await mobilePage.locator('body').innerText(), /private-value/);
  assert.equal(await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-integrations.png'), fullPage: true });
  checks.push('mobile-route-parity-responsive-and-safe');
  await mobileContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-SURFACE-006',
  passed: failures.length === 0,
  checks,
  failures
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 7);
console.log('V3-SURFACE-006 browser passed: lifecycle, secret-once, safe delivery recovery, permissions and mobile parity.');
