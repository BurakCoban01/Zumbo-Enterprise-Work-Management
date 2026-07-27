import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-surface-007');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = new Date().toISOString();
const admin = {
  id: 'operations-admin',
  username: 'selin',
  email: 'selin@zumbo.local',
  organizationId: 'org-operations',
  roles: ['SystemAdmin']
};
const member = {
  id: 'operations-member',
  username: 'kaan',
  email: 'kaan@zumbo.local',
  organizationId: 'org-operations',
  roles: ['User']
};
const roles = [
  { name: 'SystemAdmin', permissions: ['OperationsManage'] },
  { name: 'User', permissions: [] }
];
const checks = [];
const failures = [];
const actions = [];
let storageReads = 0;
let messageDeadLetters = [{
  id: 'opaque-message-identifier',
  eventType: 'work-item.updated.v1',
  attempts: 3,
  deadLetteredAtUtc: now
}];
let notificationDeadLetters = [{
  id: 'opaque-notification-identifier',
  type: 'Assignment',
  attempts: 2,
  deadLetteredAt: now
}];

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-surface-007' });
}

async function createContext(currentUser, viewport, options = {}) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user: currentUser, csrfToken: 'csrf-surface-007' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    let data = [];

    if (path === '/api/browser-auth/session') data = { user: currentUser, csrfToken: 'csrf-surface-007' };
    else if (path === '/api/auth/roles') data = roles;
    else if (path === '/api/auth/users') data = [admin, member];
    else if (path === '/api/auth/sessions' || path === '/api/auth/api-keys') data = [];
    else if (path === '/api/auth/mfa') data = { enabled: false, remainingRecoveryCodes: 0 };
    else if (path === '/api/notifications/preferences/me') {
      data = { inAppEnabled: true, emailEnabled: false, mutedTypes: [] };
    } else if (path === '/api/organizations') {
      data = [{ id: 'org-operations', tenantKey: 'org-operations', name: 'Zumbo Operasyon' }];
    } else if (path === '/api/projects' || path === '/api/teams') data = [];
    else if (path === '/api/operations/external-dependencies') {
      const directBody = {
        dependencies: [
          { dependency: 'mongodb', executions: 18, succeeded: 18, failed: 0, timedOut: 0, rejected: 0, queued: 0, retries: 0, averageLatencyMilliseconds: 12, circuitOpen: false },
          { dependency: 'opensearch', executions: 12, succeeded: 8, failed: 1, timedOut: 2, rejected: 0, queued: 1, retries: 2, averageLatencyMilliseconds: 93, circuitOpen: false },
          { dependency: 'smtp', executions: 6, succeeded: 0, failed: 6, timedOut: 0, rejected: 0, queued: 0, retries: 3, averageLatencyMilliseconds: 0, circuitOpen: true }
        ]
      };
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(directBody)
      });
    } else if (path === '/api/work-items/durable-messaging/metrics') {
      data = { pending: 4, processing: 1, completed: 28, deadLetter: messageDeadLetters.length, retried: 3, capturedAtUtc: now };
    } else if (path === '/api/work-items/durable-messaging/dead-letters' && method === 'GET') {
      data = messageDeadLetters;
    } else if (/^\/api\/work-items\/durable-messaging\/dead-letter\/[^/]+\/replay$/.test(path)) {
      messageDeadLetters = [];
      actions.push('message-replay');
      data = { replayed: true };
    } else if (path === '/api/notifications/delivery/status') {
      data = { pending: 2, sent: 14, deadLetter: notificationDeadLetters.length, capturedAt: now };
    } else if (path === '/api/notifications/delivery/dead-letters') {
      data = notificationDeadLetters;
    } else if (/^\/api\/notifications\/delivery\/[^/]+\/replay$/.test(path)) {
      notificationDeadLetters = [];
      actions.push('notification-replay');
      data = { status: 'Pending' };
    } else if (path === '/api/operations/storage/security' && method === 'GET') {
      storageReads += 1;
      if (options.failFirstStorageRead && storageReads === 1) {
        return route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({
            success: false,
            data: null,
            error: { code: 'DEPENDENCY_UNAVAILABLE', message: 'Durum şu anda alınamıyor.' },
            correlationId: 'v3-surface-007'
          })
        });
      }
      data = { quarantined: 2, clean: 21, rejected: 1, oldestQuarantinedAt: now, capturedAt: now };
    } else if (path === '/api/operations/storage/security/maintenance') {
      actions.push('storage-maintenance');
      data = { examined: 2, retried: 2, released: 1, rejected: 0, purgedMetadata: 0, deletedOrphans: 0 };
    } else if (path === '/api/work-items/search/reconcile') {
      actions.push('search-reconcile');
      data = { indexed: 27, removed: 2 };
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
  const desktopContext = await createContext(admin, { width: 1440, height: 1000 }, {
    failFirstStorageRead: true
  });
  const desktopPage = await desktopContext.newPage();
  diagnostics(desktopPage, 'desktop-admin');
  desktopPage.on('dialog', dialog => dialog.accept());
  await desktopPage.goto(`${server.origin}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await desktopPage.getByRole('tab', { name: 'Operasyonlar' }).waitFor();
  await desktopPage.getByRole('tab', { name: 'Operasyonlar' }).click();
  const desktopSurface = desktopPage.locator('.operations-center');
  await desktopSurface.locator('.operations-grid').waitFor();
  await desktopSurface.locator('.operations-error').waitFor();
  assert.match(await desktopSurface.innerText(), /Müdahale/);
  assert.doesNotMatch(
    await desktopSurface.innerText(),
    /opaque-|private|password|payload|correlation|storagePath/i
  );
  checks.push('desktop-degraded-and-partial-failure-isolation');

  await desktopSurface.getByRole('button', { name: 'Sistem olayını yeniden sırala' }).click();
  await desktopPage.waitForFunction(() =>
    !document.querySelector('[aria-label="Sistem olayını yeniden sırala"]'));
  await desktopSurface.getByRole('button', { name: 'Bildirim teslimatını yeniden sırala' }).click();
  await desktopPage.waitForFunction(() =>
    !document.querySelector('[aria-label="Bildirim teslimatını yeniden sırala"]'));
  assert.deepEqual(actions.slice(0, 2), ['message-replay', 'notification-replay']);
  checks.push('desktop-confirmed-redacted-recovery');

  await desktopSurface.getByRole('button', { name: 'Uzlaştır' }).click();
  await desktopSurface.locator('.operations-panel').last().locator('.operations-search-result').waitFor();
  await desktopSurface.getByRole('button', { name: 'Yeniden denetle' }).click();
  await desktopPage.waitForFunction(() => !document.querySelector('.operations-center [disabled]'));
  assert.ok(actions.includes('search-reconcile'));
  assert.ok(actions.includes('storage-maintenance'));
  checks.push('desktop-search-and-storage-actions');

  await desktopContext.setOffline(true);
  await desktopPage.evaluate(() => window.dispatchEvent(new window.Event('offline')));
  await desktopSurface.locator('.integration-offline').waitFor();
  const recoveryButtons = desktopSurface.locator('.operations-row button, .operations-panel header > button');
  assert.equal(await recoveryButtons.count() > 0, true);
  for (let index = 0; index < await recoveryButtons.count(); index += 1) {
    assert.equal(await recoveryButtons.nth(index).isDisabled(), true);
  }
  assert.equal(await desktopPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await desktopPage.waitForTimeout(5200);
  await desktopPage.screenshot({ path: resolve(output, 'desktop-operations.png'), fullPage: true });
  checks.push('desktop-offline-readonly-responsive');
  await desktopContext.close();

  const deniedContext = await createContext(member, { width: 1280, height: 820 });
  const deniedPage = await deniedContext.newPage();
  diagnostics(deniedPage, 'desktop-denied');
  await deniedPage.goto(`${server.origin}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await deniedPage.getByRole('heading', { name: 'Ayarlar' }).waitFor();
  assert.equal(await deniedPage.getByRole('tab', { name: 'Operasyonlar' }).count(), 0);
  assert.equal(await deniedPage.locator('.operations-center').count(), 0);
  checks.push('desktop-permission-denied');
  await deniedContext.close();

  const mobileContext = await createContext(admin, { width: 390, height: 844 });
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-admin');
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/profile/operations`, {
    waitUntil: 'domcontentloaded'
  });
  const mobileSurface = mobilePage.locator('.mobile-operations-center');
  await mobileSurface.locator('.mobile-operations-panel').first().waitFor();
  assert.doesNotMatch(
    await mobileSurface.innerText(),
    /opaque-|private|password|payload|correlation|storagePath/i
  );
  await mobileSurface.getByRole('button', { name: 'Uzlaştır' }).click();
  await mobilePage.locator('.popup-buttons .button-positive').click();
  await mobileSurface.locator('.mobile-operations-result').waitFor();
  assert.equal(await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
  await mobilePage.evaluate(() => {
    document.querySelectorAll('.scroll, ion-content').forEach(element => {
      element.scrollTop = 0;
    });
  });
  await mobilePage.screenshot({ path: resolve(output, 'mobile-operations.png'), fullPage: true });
  checks.push('mobile-action-parity-responsive-and-safe');
  await mobileContext.close();

  const mobileDeniedContext = await createContext(member, { width: 390, height: 844 });
  const mobileDeniedPage = await mobileDeniedContext.newPage();
  diagnostics(mobileDeniedPage, 'mobile-denied');
  await mobileDeniedPage.goto(`${server.origin}/mobile-ionic/index.html#/profile/operations`, {
    waitUntil: 'domcontentloaded'
  });
  await mobileDeniedPage.locator('.mobile-state.is-error').waitFor();
  assert.match(await mobileDeniedPage.locator('.mobile-state.is-error').innerText(), /sistem operasyonu/i);
  checks.push('mobile-permission-denied');
  await mobileDeniedContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-SURFACE-007',
  passed: failures.length === 0,
  checks,
  actions,
  failures
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 7);
console.log('V3-SURFACE-007 browser passed: degraded states, recovery, permissions, offline safety and mobile parity.');
