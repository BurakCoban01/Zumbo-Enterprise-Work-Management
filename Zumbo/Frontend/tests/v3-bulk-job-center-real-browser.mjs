import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-surface-004-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-SURFACE-004', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-SURFACE-004 tenant cleanup');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-SURFACE-004 tenant cleanup');
const password = 'P@ssword123';
const failures = [];
const checks = [];
let cleanupTokenPromise;
let cleanup = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;

await mkdir(output, { recursive: true });

async function apiRequest(path, method = 'GET', body, token, options = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(options.idempotencyKey ? { 'Idempotency-Key': options.idempotencyKey } : {}),
      ...(options.expectedVersion ? { 'If-Match': `"${options.expectedVersion}"` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label, options) {
  const result = await apiRequest(path, method, body, token, options);
  assert.ok(result.response.ok, result.payload.error?.message || `${label} failed with HTTP ${result.response.status}`);
  return result.data;
}

async function archiveTenant() {
  const token = await cleanupTokenPromise;
  const result = await apiRequest(`/api/organizations/${encodeURIComponent(tenantId)}/archive`, 'POST', undefined, token);
  if (result.response.ok || result.response.status === 404) return { tenantId, status: result.response.status };
  throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
}

async function browserLogin(context, usernameOrEmail) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin, 'X-Zumbo-Device-Name': 'Surface 004 browser' },
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

async function eventually(operation, label, attempts = 180, delayMs = 100) {
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const value = await operation();
    if (value) return value;
    await new Promise(resolveDelay => setTimeout(resolveDelay, delayMs));
  }
  throw new Error(`${label} did not become visible after ${attempts} attempts`);
}

async function jobPage(projectId, token) {
  return await requireApi(
    `/api/work-items/bulk/jobs?projectId=${encodeURIComponent(projectId)}&page=1&pageSize=50`,
    'GET',
    undefined,
    token,
    'Bulk job list'
  );
}

async function terminalJob(projectId, token, predicate, label) {
  return await eventually(async () => {
    const page = await jobPage(projectId, token);
    return page.items.find(job => predicate(job)
      && ['Completed', 'CompletedWithErrors', 'Cancelled', 'Failed'].includes(job.state));
  }, label, 240, 100);
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const registration = await requireApi('/api/auth/register', 'POST', {
    username: `bulkowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Owner registration');
  const owner = registration.user;
  const ownerToken = registration.accessToken;
  cleanupTokenPromise = Promise.resolve(ownerToken);

  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Toplu İşler',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `BJ${stamp.slice(-5)}`,
    name: 'Veri Taşıma Operasyonu',
    ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Veri Taşıma Panosu',
    type: 'Kanban'
  }, ownerToken, 'Board creation');
  checks.push('real-tenant-project-board-fixture');

  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Veri Operasyon Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const viewerEmail = `bulkviewer${stamp}@zumbo.local`;
  const invite = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `bulkviewer${stamp}`,
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invite.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Viewer project grant');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul',
    acceptDownloads: true
  });
  await browserLogin(ownerContext, owner.username);
  const page = await ownerContext.newPage();
  diagnostics(page, 'desktop-owner');
  await page.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&view=jobs`,
    { waitUntil: 'domcontentloaded' }
  );
  await page.getByRole('heading', { name: 'İçe aktarım, dışa aktarım ve iş merkezi' }).waitFor({ timeout: 45_000 });

  const partialRows = [{
    sourceKey: `valid-${stamp}`,
    boardId: board.id,
    title: `Doğrulanan aktarım ${stamp}`,
    type: 'Task',
    priority: 'High'
  }, {
    sourceKey: `invalid-${stamp}`,
    boardId: 'missing-board',
    title: `Hatalı aktarım ${stamp}`,
    type: 'Task',
    priority: 'Medium'
  }];
  await page.locator('#bulk-import-file').setInputFiles({
    name: 'partial-import.json',
    mimeType: 'application/json',
    buffer: Buffer.from(JSON.stringify(partialRows))
  });
  await page.getByText('2 geçerli satır', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Önizlemeyi başlat' }).click();
  const preview = await terminalJob(
    project.id,
    ownerToken,
    job => job.type === 'Import' && job.dryRun && job.totalItems === 2,
    'Dry-run bulk job'
  );
  assert.equal(preview.state, 'CompletedWithErrors');
  assert.equal(preview.failedItems, 1);
  assert.ok(preview.hasErrorFile);
  assert.ok(preview.artifactsExpireAt);
  const drySearch = await requireApi(
    '/api/work-items/search',
    'POST',
    { projectId: project.id, text: `Doğrulanan aktarım ${stamp}`, page: 1, pageSize: 20 },
    ownerToken,
    'Dry-run search'
  );
  assert.equal(drySearch.items.length, 0);
  await page.getByRole('button', { name: 'İş geçmişini yenile' }).click();
  const previewRow = page.locator('.job-row').filter({ hasText: 'Kısmen tamamlandı' }).first();
  await previewRow.click();
  const errorDownload = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Hataları indir' }).click();
  assert.match((await errorDownload).suggestedFilename(), /errors\.ndjson$/);
  checks.push('real-dry-run-partial-error-artifact');

  await page.getByRole('button', { name: 'Başarısızları yinele' }).click();
  const retried = await terminalJob(
    project.id,
    ownerToken,
    job => job.id === preview.id && job.version > preview.version,
    'Retried dry-run job'
  );
  assert.equal(retried.state, 'CompletedWithErrors');
  checks.push('real-partial-retry-resume');

  const validRows = [{
    sourceKey: `import-${stamp}`,
    boardId: board.id,
    title: `Kalıcı aktarım ${stamp}`,
    type: 'Task',
    priority: 'Critical'
  }];
  await page.locator('#bulk-import-file').setInputFiles({
    name: 'valid-import.json',
    mimeType: 'application/json',
    buffer: Buffer.from(JSON.stringify(validRows))
  });
  await page.getByRole('button', { name: 'İçe aktar', exact: true }).click();
  const imported = await terminalJob(
    project.id,
    ownerToken,
    job => job.type === 'Import' && !job.dryRun && job.totalItems === 1,
    'Import job'
  );
  assert.equal(imported.state, 'Completed');
  const importedSearch = await eventually(async () => {
    const result = await apiRequest('/api/work-items/search', 'POST', {
      projectId: project.id,
      text: `Kalıcı aktarım ${stamp}`,
      page: 1,
      pageSize: 20
    }, ownerToken);
    return result.response.ok && result.data.items.length === 1 ? result.data.items : null;
  }, 'Imported work item');
  assert.equal(importedSearch[0].priority, 'Critical');
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.getByText('İçe aktarım, dışa aktarım ve iş merkezi', { exact: true }).waitFor();
  await page.locator('.job-row').filter({ hasText: 'İçe aktarım' }).first().waitFor();
  checks.push('real-import-persistence-refresh-resume');

  await page.getByRole('button', { name: 'Dışa aktar', exact: true }).click();
  const exported = await terminalJob(
    project.id,
    ownerToken,
    job => job.type === 'Export' && !job.dryRun,
    'Export job'
  );
  assert.equal(exported.state, 'Completed');
  assert.ok(exported.hasResult);
  await page.getByRole('button', { name: 'İş geçmişini yenile' }).click();
  const exportRow = page.locator('.job-row').filter({ hasText: 'Dışa aktarım' }).first();
  await exportRow.click();
  const resultDownload = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Sonucu indir' }).click();
  assert.match((await resultDownload).suggestedFilename(), /result\.ndjson$/);
  checks.push('real-export-result-artifact');

  const outsider = await requireApi('/api/auth/register', 'POST', {
    username: `bulkoutsider${stamp}`,
    email: `bulkoutsider${stamp}@zumbo.local`,
    password,
    organizationId: `foreign-${stamp}`
  }, undefined, 'Outsider registration');
  const crossTenant = await apiRequest(
    `/api/work-items/bulk/jobs/${imported.id}`,
    'GET',
    undefined,
    outsider.accessToken
  );
  assert.equal(crossTenant.response.status, 404);
  assert.equal(crossTenant.payload.error.code, 'WORK_ITEM_BULK_JOB_NOT_FOUND');
  checks.push('real-cross-tenant-job-denial');

  await page.screenshot({ path: resolve(output, 'desktop-real-job-center.png'), fullPage: true });
  await ownerContext.close();

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(mobileContext, owner.username);
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile-owner');
  await mobile.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}/jobs?mode=history`,
    { waitUntil: 'domcontentloaded' }
  );
  await mobile.getByRole('heading', { name: 'İş merkezi' }).waitFor({ timeout: 45_000 });
  await mobile.getByRole('tab', { name: /Geçmiş/ }).click();
  await mobile.getByText('Dışa aktarım', { exact: true }).first().waitFor();
  assert.equal(await mobile.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  checks.push('real-mobile-history-no-overflow');
  await mobile.screenshot({ path: resolve(output, 'mobile-real-history.png'), fullPage: true });
  await mobileContext.close();

  const viewerContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'mobile-viewer');
  await viewerPage.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}/jobs?mode=launch`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.getByText('Bu projede içe aktarım yetkiniz yok.', { exact: true }).waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.getByLabel('İçe aktarım JSON dosyası seç').count(), 0);
  assert.equal(await viewerPage.getByRole('button', { name: 'Dışa aktar' }).isEnabled(), true);
  const viewerJobs = await jobPage(project.id, viewerRegistration.accessToken);
  assert.equal(viewerJobs.totalCount, 0);
  checks.push('real-viewer-permission-owner-filter');
  await viewerPage.screenshot({ path: resolve(output, 'mobile-real-viewer.png'), fullPage: true });
  await viewerContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  if (browser) await browser.close();
  cleanup = await cleanupLedger.run();
  if (cleanup.failed) failures.push(`cleanup: ${JSON.stringify(cleanup.results)}`);
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-SURFACE-004',
  runId: runContext.runId,
  passed: failures.length === 0,
  checks,
  failures,
  cleanup
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 8);
console.log('V3-SURFACE-004 real browser passed: dry-run, retry, import/export, tenant safety and mobile parity.');
