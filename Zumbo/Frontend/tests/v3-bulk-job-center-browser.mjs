import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-surface-004');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = Date.now();
const owner = { id: 'owner-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const viewer = { id: 'viewer-1', username: 'deniz', email: 'deniz@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const project = {
  id: 'project-1',
  organizationId: 'org-1',
  key: 'OPS',
  name: 'Operasyon Teslimatı',
  visibility: 'Private',
  version: 4,
  members: [
    { userId: owner.id, role: 'ProjectOwner' },
    { userId: viewer.id, role: 'Viewer' }
  ],
  teamIds: [],
  templates: [],
  components: [],
  versions: [],
  releases: [],
  milestones: [],
  archived: false
};
const board = {
  id: 'board-1',
  projectId: project.id,
  name: 'Teslimat Panosu',
  type: 'Kanban',
  version: 1,
  views: [],
  columns: [{ id: 'todo', name: 'To Do', category: 'Todo', position: 0 }]
};
let sequence = 10;
let lastImportRequest;
let lastImportIdempotency;
let jobs = [
  job({
    id: 'job-running',
    type: 'Import',
    state: 'Running',
    totalItems: 10,
    processedItems: 4,
    succeededItems: 4
  }),
  job({
    id: 'job-partial',
    type: 'Import',
    state: 'CompletedWithErrors',
    totalItems: 5,
    processedItems: 5,
    succeededItems: 4,
    failedItems: 1,
    hasResult: true,
    hasErrorFile: true,
    completedAt: new Date(now - 3600000).toISOString(),
    artifactsExpireAt: new Date(now + 6 * 86400000).toISOString(),
    lastErrorCode: 'WORK_ITEM_NOT_FOUND',
    lastErrorMessage: 'Bir satır uygulanamadı.'
  }),
  job({
    id: 'job-expired',
    type: 'Export',
    state: 'Completed',
    totalItems: 18,
    processedItems: 18,
    succeededItems: 18,
    hasResult: true,
    completedAt: new Date(now - 8 * 86400000).toISOString(),
    artifactsExpireAt: new Date(now - 86400000).toISOString()
  })
];
const checks = [];
const failures = [];

function job(overrides) {
  return {
    id: `job-${sequence}`,
    projectId: project.id,
    requestedByUserId: owner.id,
    type: 'Import',
    operation: null,
    dryRun: false,
    state: 'Pending',
    totalItems: 0,
    processedItems: 0,
    succeededItems: 0,
    failedItems: 0,
    cancelRequested: false,
    hasResult: false,
    hasErrorFile: false,
    lastErrorCode: null,
    lastErrorMessage: null,
    createdAt: new Date(now - sequence * 60000).toISOString(),
    startedAt: null,
    completedAt: null,
    artifactsExpireAt: null,
    version: 1,
    ...overrides
  };
}

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-surface-004' });
}

function baseData(url, request) {
  const path = url.pathname;
  const method = request.method();
  if (path === '/api/projects' && method === 'GET') return [project];
  if (path === `/api/projects/${project.id}` && method === 'GET') return project;
  if (path === `/api/boards/by-project/${project.id}`) return url.searchParams.get('archived') === 'true' ? [] : [board];
  if (path === `/api/workflows/${project.id}`) return { projectId: project.id, statuses: [], transitions: [] };
  if (path === `/api/work-item-schemas/${project.id}`) return { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
  if (path === '/api/work-items/search') return { items: [], totalCount: 0, degraded: false };
  if (path.startsWith('/api/work-items/reports/')) return { total: 0, inProgress: 0, done: 0, overdue: 0 };
  if (path === `/api/sprints/projects/${project.id}` || path === `/api/sprints/projects/${project.id}/backlog`) {
    return { items: [], totalCount: 0 };
  }
  if (path === '/api/teams' || path === '/api/auth/users') return path === '/api/auth/users' ? [owner, viewer] : [];
  if (path === '/api/organizations') return [{ id: 'org-1', tenantKey: 'org-1', name: 'Zumbo' }];
  if (path.startsWith('/api/audit/') || path.startsWith('/api/notifications/')) return [];
  return undefined;
}

function jobData(url, request, user) {
  const path = url.pathname;
  const method = request.method();
  if (path === '/api/work-items/bulk/jobs' && method === 'GET') {
    const visible = jobs.filter(item => item.requestedByUserId === user.id);
    return { items: visible, page: 1, pageSize: 50, totalCount: visible.length };
  }
  if (path === '/api/work-items/bulk/jobs/import' && method === 'POST') {
    lastImportRequest = request.postDataJSON();
    lastImportIdempotency = request.headers()['idempotency-key'];
    sequence += 1;
    const created = job({
      id: `job-preview-${sequence}`,
      dryRun: lastImportRequest.dryRun,
      state: 'Completed',
      totalItems: lastImportRequest.items.length,
      processedItems: lastImportRequest.items.length,
      succeededItems: lastImportRequest.items.length,
      hasResult: true,
      completedAt: new Date().toISOString(),
      artifactsExpireAt: new Date(now + 7 * 86400000).toISOString()
    });
    jobs = [created, ...jobs];
    return created;
  }
  if (path === '/api/work-items/bulk/jobs/export' && method === 'POST') {
    sequence += 1;
    const requestBody = request.postDataJSON();
    const created = job({
      id: `job-export-${sequence}`,
      type: 'Export',
      dryRun: requestBody.dryRun,
      state: 'Pending',
      totalItems: 18
    });
    jobs = [created, ...jobs];
    return created;
  }
  const cancel = path.match(/^\/api\/work-items\/bulk\/jobs\/([^/]+)\/cancel$/);
  if (cancel && method === 'POST') {
    jobs = jobs.map(item => item.id === cancel[1]
      ? { ...item, state: 'Cancelled', cancelRequested: true, completedAt: new Date().toISOString(), version: item.version + 1 }
      : item);
    return jobs.find(item => item.id === cancel[1]);
  }
  const retry = path.match(/^\/api\/work-items\/bulk\/jobs\/([^/]+)\/retry$/);
  if (retry && method === 'POST') {
    jobs = jobs.map(item => item.id === retry[1]
      ? { ...item, state: 'Pending', processedItems: item.succeededItems, failedItems: 0, completedAt: null, version: item.version + 1 }
      : item);
    return jobs.find(item => item.id === retry[1]);
  }
  if (/^\/api\/work-items\/bulk\/jobs\/[^/]+\/(result|errors)$/.test(path) && method === 'GET') {
    return { artifact: true };
  }
  return undefined;
}

async function createContext(user, viewport) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    if (url.pathname === '/api/browser-auth/session') {
      return route.fulfill({ status: 200, contentType: 'application/json', body: envelope({ user, csrfToken: 'csrf' }) });
    }
    const data = jobData(url, request, user) ?? baseData(url, request) ?? [];
    if (data.artifact) {
      return route.fulfill({ status: 200, contentType: 'application/x-ndjson', body: '{"status":"ok"}\n' });
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
  const desktopContext = await createContext(owner, { width: 1440, height: 1000 });
  const desktop = await desktopContext.newPage();
  diagnostics(desktop, 'desktop');
  await desktop.goto(
    `${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&view=jobs`,
    { waitUntil: 'domcontentloaded' }
  );
  await desktop.getByRole('heading', { name: 'İçe aktarım, dışa aktarım ve iş merkezi' }).waitFor();
  assert.equal(await desktop.getByRole('tab', { name: 'İş merkezi', exact: true }).getAttribute('aria-selected'), 'true');
  checks.push('desktop-job-center-navigation');

  await desktop.locator('#bulk-import-file').setInputFiles({
    name: 'operasyon.json',
    mimeType: 'application/json',
    buffer: Buffer.from(JSON.stringify([{
      sourceKey: 'OPS-101',
      boardId: board.id,
      title: 'Erişim kayıtlarını doğrula',
      type: 'Task',
      priority: 'High'
    }]))
  });
  await desktop.getByText('1 geçerli satır', { exact: true }).waitFor();
  await desktop.getByRole('button', { name: 'Önizlemeyi başlat' }).click();
  await desktop.getByText('İçe aktarım önizlemesi sıraya alındı.', { exact: true }).waitFor();
  assert.equal(lastImportRequest.dryRun, true);
  assert.equal(lastImportRequest.items[0].sourceKey, 'OPS-101');
  assert.match(lastImportIdempotency, /^import-preview-/);
  checks.push('desktop-upload-parse-dry-run-idempotency');

  const partialRow = desktop.locator('.job-row').filter({ hasText: 'Kısmen tamamlandı' });
  await partialRow.click();
  await desktop.getByRole('button', { name: 'Başarısızları yinele' }).click();
  await desktop.getByText('Başarısız satırlar yeniden sıraya alındı.', { exact: true }).waitFor();
  checks.push('desktop-partial-retry-resume');

  const runningRow = desktop.locator('.job-row').filter({ hasText: 'Çalışıyor' });
  await runningRow.click();
  desktop.once('dialog', dialog => dialog.accept());
  await desktop.getByRole('button', { name: 'İptal et' }).click();
  await desktop.getByText('İptal isteği kaydedildi.', { exact: true }).waitFor();
  assert.equal(jobs.find(item => item.id === 'job-running').state, 'Cancelled');
  checks.push('desktop-cancel-confirmation');

  const expiredRow = desktop.locator('.job-row').filter({ hasText: 'Dosyalar süresi doldu' });
  await expiredRow.click();
  await desktop.getByText('Bu işin sonuç dosyalarının saklama süresi doldu.', { exact: true }).waitFor();
  assert.equal(await desktop.getByRole('button', { name: 'Sonucu indir' }).count(), 0);
  checks.push('desktop-expired-artifact-fail-closed');
  await desktop.screenshot({ path: resolve(output, 'desktop-job-center.png'), fullPage: true });
  await desktopContext.close();

  const mobileContext = await createContext(owner, { width: 390, height: 844 });
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile-owner');
  await mobile.goto(
    `${server.origin}/mobile-ionic/index.html#/projects/${project.id}/jobs?mode=history`,
    { waitUntil: 'domcontentloaded' }
  );
  await mobile.getByRole('heading', { name: 'İş merkezi' }).waitFor();
  await mobile.getByRole('tab', { name: /Geçmiş/ }).click();
  await mobile.getByText('Dosyalar süresi doldu', { exact: true }).waitFor();
  await mobile.locator('.mobile-job-row').filter({ hasText: 'Dosyalar süresi doldu' }).click();
  await mobile.getByText('Bu işin sonuç dosyalarının saklama süresi doldu.', { exact: true }).waitFor();
  assert.equal(await mobile.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  checks.push('mobile-history-expiry-no-overflow');
  await mobile.screenshot({ path: resolve(output, 'mobile-job-history.png'), fullPage: true });
  await mobileContext.close();

  const viewerContext = await createContext(viewer, { width: 390, height: 844 });
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'mobile-viewer');
  await viewerPage.goto(
    `${server.origin}/mobile-ionic/index.html#/projects/${project.id}/jobs?mode=launch`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.getByText('Bu projede içe aktarım yetkiniz yok.', { exact: true }).waitFor();
  assert.equal(await viewerPage.getByLabel('İçe aktarım JSON dosyası seç').count(), 0);
  assert.equal(await viewerPage.getByRole('button', { name: 'Dışa aktar' }).isEnabled(), true);
  checks.push('mobile-viewer-read-export-permission');
  await viewerPage.screenshot({ path: resolve(output, 'mobile-viewer-permission.png'), fullPage: true });
  await viewerContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-SURFACE-004',
  passed: failures.length === 0,
  checks,
  failures
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 7);
console.log('V3-SURFACE-004 browser passed: durable job center workflow, recovery, expiry and mobile permissions.');
