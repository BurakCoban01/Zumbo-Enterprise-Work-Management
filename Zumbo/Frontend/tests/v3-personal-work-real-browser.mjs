import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-002-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-002', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX-002 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX-002 tenant cleanup');
const password = 'P@ssword123';
let cleanupAdminTokenPromise;
let browser;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
const failures = [];
const checks = [];

await mkdir(outputDir, { recursive: true });

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

async function eventually(action, predicate, label, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let latest;
  while (Date.now() < deadline) {
    latest = await action();
    if (predicate(latest)) return latest;
    await new Promise(resolvePromise => setTimeout(resolvePromise, 500));
  }
  throw new Error(`${label} did not become ready within ${timeoutMs}ms`);
}

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      let authentication = await apiRequest('/api/auth/register', 'POST', {
        username: 'local-system-admin',
        email: adminEmail,
        password,
        organizationId: 'local-system-administration',
        bootstrapToken: adminBootstrapToken
      });
      if (authentication.response.status === 409) {
        authentication = await apiRequest('/api/auth/login', 'POST', { usernameOrEmail: adminEmail, password });
      }
      assert.ok(authentication.response.ok, authentication.payload.error?.message || 'Cleanup administrator authentication failed');
      return authentication.data.accessToken;
    })();
  }
  return cleanupAdminTokenPromise;
}

async function archiveTenant() {
  const token = await cleanupAdminToken();
  const result = await apiRequest(`/api/organizations/${encodeURIComponent(tenantId)}/archive`, 'POST', undefined, token);
  if (result.response.ok || result.response.status === 404) return { tenantId, status: result.response.status };
  throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
}

async function browserContextLogin(context, usernameOrEmail) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin },
    data: { usernameOrEmail, password }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser context login failed');
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, payload.data);
  return payload.data;
}

function attachDiagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (detail.includes('/hubs/work-items') || detail.includes('Failed to start the connection')) return;
    if (!detail.includes('Failed to load resource')) failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

async function waitForStablePersonalWork(page) {
  await page.evaluate(() => { window.__zumboPersonalStableAt = 0; });
  await page.waitForFunction(() => {
    const vm = window.angular.element(document.body).scope().vm;
    if (!vm.personalFreshAt || vm.personalLoading) {
      window.__zumboPersonalStableAt = 0;
      return false;
    }
    window.__zumboPersonalStableAt ||= Date.now();
    return Date.now() - window.__zumboPersonalStableAt >= 1_000;
  });
}

async function createProject(owner, ownerToken, key, name) {
  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key,
    name,
    ownerUserId: owner.id
  }, ownerToken, `${name} project creation`);
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: `${name} Panosu`,
    type: 'Kanban'
  }, ownerToken, `${name} board creation`);
  return { project, board };
}

async function createTask(ownerToken, fixture, title, extra = {}) {
  return requireApi('/api/work-items', 'POST', {
    projectId: fixture.project.id,
    boardId: fixture.board.id,
    title,
    type: 'Task',
    priority: 'High',
    assigneeUserId: extra.assigneeUserId,
    dueDate: extra.dueDate || null
  }, ownerToken, `${title} work-item creation`);
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `pwowner${stamp}`,
    email: `pwowner${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Owner registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Kişisel Çalışma Alanı',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');

  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `pwviewer${stamp}`,
    email: `pwviewer${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  const developerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `pwdev${stamp}`,
    email: `pwdev${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Developer registration');

  const delivery = await createProject(owner, ownerToken, `DL${stamp.slice(-5)}`, 'Teslimat Merkezi');
  const operations = await createProject(owner, ownerToken, `OP${stamp.slice(-5)}`, 'Operasyon Akışı');
  await requireApi(`/api/projects/${delivery.project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Viewer project grant');
  await requireApi(`/api/projects/${delivery.project.id}/members`, 'POST', {
    userId: developerRegistration.user.id,
    role: 'Developer'
  }, ownerToken, 'Developer project grant');

  const dueTitle = `Yayın takvimini kesinleştir ${stamp.slice(-4)}`;
  const blockedTitle = `Bağımlılık kararını bekle ${stamp.slice(-4)}`;
  const blockerTitle = `Servis sözleşmesini tamamla ${stamp.slice(-4)}`;
  const approvalTitle = `Dağıtım kapsamını onayla ${stamp.slice(-4)}`;
  const dueTask = await createTask(ownerToken, delivery, dueTitle, {
    assigneeUserId: owner.id,
    dueDate: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()
  });
  const blockedTask = await createTask(ownerToken, operations, blockedTitle, { assigneeUserId: owner.id });
  const blockerTask = await createTask(ownerToken, operations, blockerTitle, { assigneeUserId: owner.id });
  await requireApi(`/api/work-items/${blockedTask.id}/relations`, 'POST', {
    relatedWorkItemId: blockerTask.id,
    relationType: 'BlockedBy'
  }, ownerToken, 'Blocked relation creation');
  const approvalTask = await createTask(ownerToken, delivery, approvalTitle, { assigneeUserId: owner.id });

  const statuses = [
    { name: 'To Do', category: 'Todo' },
    { name: 'Test', category: 'InProgress' },
    { name: 'Done', category: 'Done' }
  ];
  const transitions = [
    { fromStatus: 'To Do', toStatus: 'Test', requiresAssignee: false, requiresCompletedChecklist: false },
    { fromStatus: 'Test', toStatus: 'Done', requiresAssignee: false, requiresCompletedChecklist: false, requiresApproval: true }
  ];
  await requireApi(`/api/workflows/${delivery.project.id}`, 'PUT', {
    projectId: delivery.project.id,
    transitions,
    statuses
  }, ownerToken, 'Approval workflow creation');
  await requireApi(`/api/work-items/${approvalTask.id}/status`, 'PATCH', { status: 'Test' }, ownerToken, 'Approval task transition');
  await requireApi(`/api/work-items/${approvalTask.id}/approvals`, 'POST', { targetStatus: 'Done' }, ownerToken, 'Approval request');

  await requireApi(`/api/work-items/${dueTask.id}/comments`, 'POST', {
    body: 'Yayın hazırlığı için sahibin görüşü gerekiyor.',
    mentions: [owner.id]
  }, developerRegistration.accessToken, 'Mention comment');
  const notifications = await eventually(
    async () => (await requireApi('/api/notifications?page=1&pageSize=20', 'GET', undefined, ownerToken, 'Notification polling')),
    items => items.some(item => item.type === 'Mention' && item.message.includes(dueTitle)),
    'Owner mention notification'
  );
  checks.push('durable-mention-notification');
  assert.ok(notifications.some(item => item.type === 'Assignment'), 'Assignment notifications were not delivered');

  const ownerSearchProjects = [];
  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'owner');
  ownerPage.on('request', request => {
    if (new URL(request.url()).pathname !== '/api/work-items/search') return;
    const body = request.postDataJSON();
    ownerSearchProjects.push({
      projectId: body.projectId,
      assigneeUserId: body.assigneeUserId,
      page: body.page,
      pageSize: body.pageSize
    });
  });
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=home&project=${delivery.project.id}&board=${delivery.board.id}`,
    { waitUntil: 'domcontentloaded' }
  );
  await ownerPage.getByRole('heading', { name: `Merhaba, ${owner.username}` }).waitFor({ timeout: 45_000 });
  await ownerPage.getByText(dueTitle, { exact: true }).waitFor();
  await ownerPage.waitForFunction(() => {
    const scope = window.angular.element(document.body).scope();
    return scope.vm.personalTasks.length >= 4 && !scope.vm.personalLoading;
  });
  const ownerState = await ownerPage.evaluate(() => {
    const vm = window.angular.element(document.body).scope().vm;
    return {
      taskProjects: [...new Set(vm.personalTasks.map(task => task.projectName))].sort(),
      due: vm.personalDue().map(task => task.title),
      blocked: vm.personalBlocked().map(task => task.title),
      approvals: vm.pendingApprovals().map(task => task.title),
      freshAt: vm.personalFreshAt,
      partial: vm.personalPartial
    };
  });
  assert.deepEqual(ownerState.taskProjects, ['Operasyon Akışı', 'Teslimat Merkezi']);
  assert.ok(ownerState.due.includes(dueTitle));
  assert.ok(ownerState.blocked.includes(blockedTitle));
  assert.ok(ownerState.approvals.includes(approvalTitle));
  assert.equal(ownerState.partial, false);
  assert.ok(Number.isFinite(Date.parse(ownerState.freshAt)), 'Freshness timestamp was not populated');
  const personalSearches = ownerSearchProjects.filter(item => item.assigneeUserId === owner.id);
  assert.deepEqual([...new Set(personalSearches.map(item => item.projectId))].sort(), [delivery.project.id, operations.project.id].sort());
  assert.ok(personalSearches.every(item => item.page === 1 && item.pageSize === 50));
  checks.push('multi-project-aggregation', 'due-blocked-approval', 'freshness-pagination-contract');

  await ownerPage.getByRole('button', { name: 'İşlerim', exact: true }).click();
  await ownerPage.getByRole('tab', { name: 'Tarihli', exact: true }).click();
  await ownerPage.getByText(dueTitle, { exact: true }).waitFor();
  await ownerPage.getByRole('tab', { name: 'Engelli', exact: true }).click();
  await ownerPage.getByText(blockedTitle, { exact: true }).waitFor();
  await ownerPage.getByLabel('Kişisel görünüm adı').fill('Engel takibi');
  await ownerPage.getByRole('button', { name: 'Kişisel görünümü kaydet' }).click();
  await ownerPage.reload({ waitUntil: 'domcontentloaded' });
  await ownerPage.getByRole('button', { name: /Engel takibi/ }).waitFor({ timeout: 45_000 });
  checks.push('saved-view-persistence');

  await ownerPage.getByRole('button', { name: 'Gelen kutusu', exact: true }).click();
  await ownerPage.getByRole('tab', { name: 'Eylem gereken', exact: true }).click();
  await ownerPage.getByText(`Mentioned on ${dueTitle}`, { exact: true }).waitFor();
  await ownerPage.getByText(approvalTitle, { exact: true }).waitFor();
  await ownerPage.locator('.create-button').click();
  assert.ok(await ownerPage.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).isVisible());
  await ownerPage.locator('.create-button').click();
  checks.push('inbox-action-filter', 'owner-quick-create');
  await waitForStablePersonalWork(ownerPage);
  await ownerPage.screenshot({ path: resolve(outputDir, 'owner-personal-work.png'), fullPage: true });

  const viewerSearchProjects = [];
  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'viewer');
  viewerPage.on('request', request => {
    if (new URL(request.url()).pathname !== '/api/work-items/search') return;
    const body = request.postDataJSON();
    if (body.assigneeUserId === viewerRegistration.user.id) viewerSearchProjects.push(body.projectId);
  });
  await viewerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=mywork&project=${delivery.project.id}&board=${delivery.board.id}`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.getByRole('heading', { name: 'İşlerim', exact: true }).waitFor({ timeout: 45_000 });
  await waitForStablePersonalWork(viewerPage);
  await viewerPage.getByText('Bu görünümde iş yok.', { exact: true }).waitFor();
  assert.deepEqual([...new Set(viewerSearchProjects)], [delivery.project.id]);
  await viewerPage.locator('.create-button').click();
  assert.equal(await viewerPage.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).count(), 0);
  await viewerPage.locator('.create-button').click();
  const unauthorized = await apiRequest('/api/work-items/search', 'POST', {
    projectId: operations.project.id,
    assigneeUserId: viewerRegistration.user.id,
    page: 1,
    pageSize: 50
  }, viewerRegistration.accessToken);
  assert.equal(unauthorized.response.status, 200, 'Internal project read policy changed unexpectedly');
  assert.deepEqual(unauthorized.data.items, [], 'Viewer personal filter returned another user\'s work');
  assert.equal((await viewerPage.locator('body').innerText()).includes(blockedTitle), false);
  await waitForStablePersonalWork(viewerPage);
  await viewerPage.getByText('Bu görünümde iş yok.', { exact: true }).waitFor();
  checks.push('viewer-personal-scope', 'viewer-create-boundary', 'empty-state');
  await viewerPage.screenshot({ path: resolve(outputDir, 'viewer-personal-work.png'), fullPage: true });

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
  await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  attachDiagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/app/dashboard`, { waitUntil: 'domcontentloaded' });
  await mobilePage.getByRole('tab', { name: 'Tarihli', exact: true }).waitFor({ timeout: 45_000 });
  await mobilePage.getByRole('tab', { name: 'Tarihli', exact: true }).click();
  await mobilePage.getByText(dueTitle, { exact: true }).waitFor();
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile personal work overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  checks.push('mobile-real-entry');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-personal-work.png'), fullPage: true });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-UX-002',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 11,
    apiBaseUrl,
    frontendBaseUrl,
    checks,
    cleanup: cleanupResult,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
console.log('V3-UX-002 real-browser passed: multi-project personal work, inbox, Viewer isolation and mobile entry.');
