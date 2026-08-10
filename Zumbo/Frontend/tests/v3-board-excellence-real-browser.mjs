import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-004-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-004', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX-004 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX-004 tenant cleanup');
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
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
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

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      const authentication = await apiRequest('/api/auth/login', 'POST', { usernameOrEmail: adminEmail, password });
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
    headers: { Origin: frontendOrigin }, data: { usernameOrEmail, password }
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
    if (detail.includes('Failed to load resource')) return;
    failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

function taskPayload(project, board, owner, title, priority = 'Medium') {
  return {
    projectId: project.id,
    boardId: board.id,
    title,
    type: 'Task',
    priority,
    assigneeUserId: owner.id,
    dueDate: new Date(Date.now() + 3 * 86400000).toISOString()
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux4owner${stamp}`, email: adminEmail, password,
    organizationId: tenantId, bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Board Excellence', tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `ux4viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId, name: 'Board Ekibi', ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitedTeam = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail, role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux4viewer${stamp}`, email: viewerEmail, password, organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitedTeam.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId, key: `BX${stamp.slice(-5)}`, name: 'Board Operasyonları', ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  let board = await requireApi('/api/boards', 'POST', {
    projectId: project.id, name: 'Akış Panosu', type: 'Kanban'
  }, ownerToken, 'Board creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id, role: 'Viewer'
  }, ownerToken, 'Viewer project grant');

  const doingColumn = board.columns.find(column => column.name === 'In Progress');
  assert.ok(doingColumn, 'Default In Progress column is missing');
  board = await requireApi(`/api/boards/${board.id}/columns/${doingColumn.id}`, 'PUT', {
    name: doingColumn.name, category: doingColumn.category, wipLimit: 1
  }, ownerToken, 'WIP limit update');
  const todoColumn = board.columns.find(column => column.name === 'To Do');
  const currentDoing = board.columns.find(column => column.name === 'In Progress');

  const createdTasks = [];
  for (let index = 1; index <= 8; index += 1) {
    createdTasks.push(await requireApi('/api/work-items', 'POST',
      taskPayload(project, board, owner, `Gerçek operasyon işi ${index} ${stamp.slice(-4)}`, index % 2 ? 'High' : 'Low'),
      ownerToken, `Task ${index} creation`));
  }
  const wipTask = await requireApi(`/api/work-items/${createdTasks[0].id}/status`, 'PATCH', {
    status: currentDoing.name
  }, ownerToken, 'Initial WIP task move');
  checks.push('real-large-enough-fixture');

  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  let ownerStatusRequests = 0;
  ownerPage.on('request', request => {
    if (request.method() === 'PATCH' && /\/api\/work-items\/[^/]+\/status$/.test(new URL(request.url()).pathname)) {
      ownerStatusRequests += 1;
    }
  });
  attachDiagnostics(ownerPage, 'owner');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=board`,
    { waitUntil: 'domcontentloaded' }
  );
  await ownerPage.locator(`[data-work-item-id="${wipTask.id}"]`).waitFor({ timeout: 45_000 });
  const doingLane = ownerPage.locator('.column-lane').filter({ hasText: 'In Progress' }).first();
  assert.equal(await doingLane.getAttribute('data-wip-state'), 'full');
  assert.match(await doingLane.locator('.wip-count').innerText(), /1\s*\/\s*1/);
  checks.push('real-wip-projection');

  const rollbackTask = ownerPage.locator(`[data-work-item-id="${createdTasks[1].id}"]`);
  assert.equal(await rollbackTask.getByTitle('Sonraki kolona taşı').isDisabled(), true);
  const ownerStatusRequestsBefore = ownerStatusRequests;
  await rollbackTask.focus();
  await rollbackTask.press('Alt+ArrowRight');
  await ownerPage.waitForTimeout(250);
  assert.equal(ownerStatusRequests, ownerStatusRequestsBefore);
  assert.equal(await rollbackTask.evaluate(element => element.closest('.column-lane').querySelector('.lane-title strong').textContent.trim()), todoColumn.name);
  checks.push('real-keyboard-wip-preflight');

  const missingTaskId = `missing-${stamp}`;
  await ownerPage.evaluate(({ taskId, missingId }) => {
    const scope = window.angular.element(document.body).scope();
    scope.$apply(() => {
      scope.vm.selectedTaskIds = { [taskId]: true, [missingId]: true };
    });
  }, { taskId: createdTasks[2].id, missingId: missingTaskId });
  await ownerPage.getByRole('button', { name: 'Bana ata', exact: true }).click();
  await ownerPage.locator('.bulk-result[data-failed="1"]').waitFor();
  assert.match(await ownerPage.locator('.bulk-result').innerText(), /1 başarılı, 1 başarısız/);
  checks.push('real-partial-bulk');

  await ownerPage.getByRole('tab', { name: 'Liste', exact: true }).click();
  await ownerPage.locator('.list-work-view tbody tr').first().waitFor();
  const successRow = ownerPage.locator('.list-work-view tbody tr').filter({ hasText: createdTasks[3].title });
  await successRow.getByTitle('Satırda düzenle').click();
  await ownerPage.getByLabel('Görev başlığı').fill(`Inline gerçek güncelleme ${stamp.slice(-4)}`);
  await ownerPage.getByTitle('Kaydet').click();
  await ownerPage.getByText(`Inline gerçek güncelleme ${stamp.slice(-4)}`, { exact: true }).waitFor();
  checks.push('real-inline-edit');

  const conflictTask = createdTasks[4];
  const conflictRow = ownerPage.locator('.list-work-view tbody tr').filter({ hasText: conflictTask.title });
  await conflictRow.getByTitle('Satırda düzenle').click();
  const externalTitle = `Dış güncelleme ${stamp.slice(-4)}`;
  await requireApi(`/api/work-items/${conflictTask.id}`, 'PUT', {
    title: externalTitle,
    description: conflictTask.description || '',
    priority: conflictTask.priority,
    dueDate: conflictTask.dueDate || null
  }, ownerToken, 'External concurrent update');
  await ownerPage.getByLabel('Görev başlığı').fill(`Stale güncelleme ${stamp.slice(-4)}`);
  await ownerPage.getByTitle('Kaydet').click();
  await ownerPage.getByText(/başka bir kullanıcı tarafından değiştirildi/i).first().waitFor();
  await ownerPage.getByText(externalTitle, { exact: true }).waitFor();
  checks.push('real-concurrency-conflict');
  await ownerPage.screenshot({ path: resolve(outputDir, 'owner-list-conflict.png') });

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'viewer');
  await viewerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=board`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.locator('.board-shell .task').first().waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.locator('.task-select').count(), 0);
  assert.equal(await viewerPage.locator('.task-move-actions').count(), 0);
  assert.equal(await viewerPage.locator('.task').first().getAttribute('draggable'), 'false');
  await viewerPage.getByRole('tab', { name: 'Liste', exact: true }).click();
  assert.equal(await viewerPage.getByTitle('Satırda düzenle').count(), 0);
  checks.push('real-viewer-read-only');

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
  const mobileAuthentication = await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  attachDiagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}`, { waitUntil: 'domcontentloaded' });
  await mobilePage.waitForFunction(initialToken => {
    const currentToken = sessionStorage.getItem('zumbo.csrfToken');
    return currentToken && currentToken !== initialToken && document.body.getAttribute('aria-busy') === 'false';
  }, mobileAuthentication.csrfToken, { timeout: 45_000 });
  await mobilePage.getByRole('button', { name: 'Pano', exact: true }).waitFor({ timeout: 45_000 });
  await mobilePage.getByRole('button', { name: 'Pano', exact: true }).click();
  await mobilePage.locator('.mobile-board-task').first().waitFor({ timeout: 45_000 });
  const mobileCandidate = mobilePage.getByRole('button', { name: new RegExp(`${createdTasks[1].title}.*sonraki kolona taşı`) });
  await mobileCandidate.click();
  await mobilePage.getByText('Kolonun WIP limiti dolu; görev önceki kolonuna alındı.', { exact: true }).waitFor();
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile board overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  checks.push('real-mobile-touch-wip-rollback');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-board-wip.png') });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-UX-004',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 8,
    apiBaseUrl,
    frontendBaseUrl,
    checks,
    cleanup: cleanupResult,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
console.log('V3-UX-004 real-browser passed: desktop WIP preflight, partial bulk, inline conflict, Viewer and mobile touch rollback.');
