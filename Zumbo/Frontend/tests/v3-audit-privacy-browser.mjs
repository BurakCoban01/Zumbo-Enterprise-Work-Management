import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-surface-005');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = new Date().toISOString();
const auditUser = {
  id: 'audit-user-1',
  username: 'selin',
  email: 'selin@zumbo.local',
  organizationId: 'org-1',
  roles: ['AuditReader']
};
const standardUser = {
  id: 'standard-user-1',
  username: 'deniz',
  email: 'deniz@zumbo.local',
  organizationId: 'org-1',
  roles: ['User']
};
const users = [
  auditUser,
  standardUser,
  { id: 'actor-2', username: 'mert', email: 'mert@zumbo.local', organizationId: 'org-1', roles: ['User'] }
];
const roles = [
  { name: 'AuditReader', permissions: ['AuditReadAll'] },
  { name: 'User', permissions: [] }
];
const auditItems = [
  {
    id: 'audit-1',
    actorUserId: 'actor-2',
    action: 'WorkItemUpdated',
    entityType: 'WorkItem',
    entityId: 'item-1',
    correlationId: 'browser-correlation-1',
    createdAt: now,
    changes: [
      { field: 'PasswordHash', oldValue: '[REDACTED]', newValue: '[REDACTED]', redacted: true },
      { field: 'Title', oldValue: 'Plan', newValue: 'Teslimat planı', redacted: false }
    ]
  },
  {
    id: 'audit-2',
    actorUserId: auditUser.id,
    action: 'ProjectCreated',
    entityType: 'Project',
    entityId: 'project-1',
    correlationId: 'browser-correlation-2',
    createdAt: new Date(Date.now() - 3600000).toISOString(),
    changes: []
  }
];
const checks = [];
const failures = [];
let privacyCreateCount = 0;
let statusTokenHeaderCount = 0;
let lastAuditQuery = '';

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-surface-005' });
}

function job(state = 'Pending') {
  return {
    id: 'privacy-job-1',
    state,
    progressPercent: state === 'Completed' ? 100 : (state === 'Running' ? 62 : 0),
    createdAt: now,
    updatedAt: now,
    expiresAt: new Date(Date.now() + 7 * 86400000).toISOString(),
    lastError: null
  };
}

async function createContext(user, viewport) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul',
    acceptDownloads: true
  });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf-surface-005' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (path === '/api/audit/export') {
      return route.fulfill({
        status: 200,
        contentType: 'application/x-ndjson',
        headers: {
          'Content-Disposition': 'attachment; filename=zumbo-audit-export.ndjson',
          'Cache-Control': 'no-store',
          'X-Zumbo-Export-Records': String(auditItems.length)
        },
        body: `${auditItems.map(item => JSON.stringify(item)).join('\n')}\n`
      });
    }
    if (path === '/api/auth/privacy/export.ndjson') {
      return route.fulfill({
        status: 200,
        contentType: 'application/x-ndjson',
        headers: { 'Content-Disposition': 'attachment; filename=zumbo-privacy-export.ndjson' },
        body: `${JSON.stringify({ category: 'profile', username: user.username })}\n`
      });
    }

    let data;
    if (path === '/api/browser-auth/session') data = { user, csrfToken: 'csrf-surface-005' };
    else if (path === '/api/auth/roles') data = roles;
    else if (path === '/api/auth/users') data = users;
    else if (path === '/api/auth/sessions') data = [];
    else if (path === '/api/auth/mfa') data = { enabled: false, remainingRecoveryCodes: 0 };
    else if (path === '/api/auth/api-keys') data = [];
    else if (path === '/api/notifications/preferences/me') {
      data = { inAppEnabled: true, emailEnabled: false, mutedTypes: [] };
    } else if (path === '/api/organizations') {
      data = [{ id: 'org-1', tenantKey: 'org-1', name: 'Zumbo Araştırma' }];
    } else if (path === '/api/projects' || path === '/api/teams') data = [];
    else if (path.startsWith('/api/notifications/')) data = [];
    else if (path === '/api/audit' && method === 'GET') {
      lastAuditQuery = url.search;
      data = { items: auditItems, page: 1, pageSize: 50, hasNextPage: false, nextCursor: null };
    } else if (path === '/api/audit/integrity/org-1') {
      data = {
        organizationId: 'org-1',
        verified: auditItems.length,
        valid: true,
        brokenRecordId: null,
        completeHistory: true,
        firstSequence: 1
      };
    } else if (path === '/api/auth/privacy/anonymization-jobs' && method === 'POST') {
      privacyCreateCount += 1;
      data = { job: job('Pending'), statusToken: 'surface_005_status_token_123456789' };
    } else if (path === '/api/auth/privacy/jobs/privacy-job-1/status') {
      assert.equal(request.headers()['x-privacy-status-token'], 'surface_005_status_token_123456789');
      statusTokenHeaderCount += 1;
      data = job('Completed');
    } else if (path === '/api/auth/privacy/jobs/privacy-job-1') data = job('Failed');
    else if (path === '/api/auth/privacy/jobs/privacy-job-1/retry') data = job('Pending');
    else if (path === '/api/auth/privacy/jobs/privacy-job-1/reconcile') data = job('Running');
    else if (path.startsWith('/api/audit/entity/')) data = [];
    else data = [];

    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: envelope(data)
    });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('request', request => {
    if (request.url().includes('/privacy/jobs/undefined/')) {
      failures.push(`${label}: privacy status requested with an undefined job id`);
    }
  });
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error' && !/WebSocket|signalr|Failed to load resource/.test(message.text())) {
      failures.push(`${label}: ${message.text()}`);
    }
  });
}

try {
  const auditContext = await createContext(auditUser, { width: 1440, height: 1000 });
  const auditPage = await auditContext.newPage();
  diagnostics(auditPage, 'desktop-audit');
  await auditPage.goto(`${server.origin}/desktop-bulma/index.html#section=audit`, {
    waitUntil: 'domcontentloaded'
  });
  await auditPage.getByRole('heading', { name: 'Denetim merkezi' }).waitFor();
  await auditPage.locator('.audit-event-list').getByText('İş güncellendi', { exact: true }).waitFor();
  assert.equal(await auditPage.getByText('old-secret').count(), 0);
  assert.equal(await auditPage.getByText('new-secret').count(), 0);
  assert.match(await auditPage.locator('.audit-change-list').innerText(), /\[REDACTED\].*\[REDACTED\]/);
  checks.push('desktop-role-gated-redacted-audit');

  await auditPage.getByRole('textbox', { name: 'Olay', exact: true }).fill('WorkItemUpdated');
  await auditPage.getByRole('button', { name: 'Ara', exact: true }).click();
  await auditPage.locator('.audit-event-list').getByText('İş güncellendi', { exact: true }).waitFor();
  assert.match(lastAuditQuery, /action=WorkItemUpdated/);
  assert.match(lastAuditQuery, /organizationId=org-1/);
  checks.push('desktop-bounded-audit-search');

  await auditPage.getByRole('button', { name: 'Bütünlüğü doğrula' }).click();
  await auditPage.getByText('Denetim zinciri doğrulandı', { exact: true }).waitFor();
  checks.push('desktop-integrity-on-demand');

  const auditDownloadPromise = auditPage.waitForEvent('download');
  await auditPage.getByRole('button', { name: 'NDJSON aktar' }).click();
  const auditDownload = await auditDownloadPromise;
  assert.match(auditDownload.suggestedFilename(), /zumbo-denetim-\d{4}-\d{2}-\d{2}\.ndjson/);
  checks.push('desktop-filtered-ndjson-export');
  assert.equal(await auditPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await auditPage.screenshot({ path: resolve(output, 'desktop-audit.png'), fullPage: true });
  await auditContext.close();

  const deniedContext = await createContext(standardUser, { width: 1280, height: 820 });
  const deniedPage = await deniedContext.newPage();
  diagnostics(deniedPage, 'desktop-denied');
  await deniedPage.goto(`${server.origin}/desktop-bulma/index.html#section=audit`, {
    waitUntil: 'domcontentloaded'
  });
  await deniedPage.getByText('Organizasyon denetim kayıtlarını görüntüleme yetkiniz yok.').waitFor();
  assert.equal(await deniedPage.locator('.nav-item').filter({ hasText: 'Denetim' }).count(), 0);
  assert.equal(await deniedPage.locator('.audit-event-list').count(), 0);
  checks.push('desktop-direct-route-permission-denied');
  await deniedPage.screenshot({ path: resolve(output, 'desktop-audit-denied.png'), fullPage: true });
  await deniedContext.close();

  const mobileContext = await createContext(auditUser, { width: 390, height: 844 });
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-privacy');
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/app/profile`, {
    waitUntil: 'domcontentloaded'
  });
  await mobilePage.getByRole('heading', { name: 'Gizlilik ve hesap' }).waitFor();

  const privacyDownloadPromise = mobilePage.waitForEvent('download');
  await mobilePage.getByRole('button', { name: 'Verilerimi NDJSON olarak aktar' }).click();
  const privacyDownload = await privacyDownloadPromise;
  assert.equal(privacyDownload.suggestedFilename(), 'zumbo-privacy-export.ndjson');
  checks.push('mobile-privacy-export');

  await mobilePage.getByLabel('Anonimleştirme parolası').fill('P@ssword123');
  await mobilePage.getByLabel('Anonimleştirme onayı').fill('ANONYMIZE');
  await mobilePage.getByRole('button', { name: 'Hesabı anonimleştir' }).click();
  await mobilePage.locator('.popup-buttons .button-default').click();
  assert.equal(privacyCreateCount, 0);

  const statusRequestPromise = mobilePage.waitForRequest(request => {
    return new URL(request.url()).pathname === '/api/auth/privacy/jobs/privacy-job-1/status';
  }, { timeout: 8_000 });
  await mobilePage.getByRole('button', { name: 'Hesabı anonimleştir' }).click();
  await mobilePage.locator('.popup-buttons .button-positive').click();
  await mobilePage.getByText('Sırada', { exact: true }).waitFor();
  await statusRequestPromise;
  await mobilePage.getByText('Tamamlandı', { exact: true }).waitFor({ timeout: 10_000 });
  await mobilePage.getByRole('button', {
    name: 'Gizlilik işi durumunu kapat',
    exact: true
  }).waitFor();
  assert.equal(privacyCreateCount, 1);
  assert.ok(statusTokenHeaderCount >= 1);
  assert.doesNotMatch(await mobilePage.locator('body').innerText(), /surface_005_status_token/);
  assert.doesNotMatch(mobilePage.url(), /surface_005_status_token/);
  checks.push('mobile-danger-confirmation-and-token-status');
  assert.equal(await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-privacy.png'), fullPage: true });
  await mobileContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-SURFACE-005',
  passed: failures.length === 0,
  checks,
  failures
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 7);
console.log('V3-SURFACE-005 browser passed: role gates, redaction, integrity, exports and durable mobile privacy.');
