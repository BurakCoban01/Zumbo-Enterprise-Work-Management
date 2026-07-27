import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-006-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-006', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX-006 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX-006 tenant cleanup');
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
    if (detail.includes('Failed to load resource: the server responded with a status of 409')) return;
    failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

function taskPayload(project, board, owner, title) {
  return {
    projectId: project.id, boardId: board.id, title, type: 'Task', priority: 'High',
    assigneeUserId: owner.id, dueDate: new Date(Date.now() + 3 * 86400000).toISOString()
  };
}

async function openDesktopTask(page, project, board, task) {
  await page.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=list`, { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: task.title, exact: true }).waitFor({ timeout: 45_000 });
  await page.getByRole('button', { name: task.title, exact: true }).click();
  await page.locator('.inspector[data-detail-mode="drawer"] #task-detail-title').waitFor({ timeout: 45_000 });
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux6owner${stamp}`, email: adminEmail, password,
    organizationId: tenantId, bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', { name: 'Zumbo Work Item Detail', tenantKey: tenantId }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `ux6viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId, name: 'İşbirliği Ekibi', ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail, role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux6viewer${stamp}`, email: viewerEmail, password, organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId, key: `DT${stamp.slice(-5)}`, name: 'Detay İşbirliği', ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id, name: 'İşbirliği Panosu', type: 'Kanban'
  }, ownerToken, 'Board creation');
  const workflow = await requireApi(`/api/workflows/${project.id}`, 'GET', undefined, ownerToken, 'Workflow read');
  const approvalTransitions = workflow.transitions.map(transition => ({
    ...transition,
    requiresApproval: transition.fromStatus === 'To Do' && transition.toStatus === 'In Progress'
      ? true : transition.requiresApproval
  }));
  assert.ok(approvalTransitions.some(transition => transition.requiresApproval), 'An approval transition is required for the real fixture');
  await requireApi(`/api/workflows/${project.id}`, 'PUT', {
    projectId: project.id, statuses: workflow.statuses, transitions: approvalTransitions
  }, ownerToken, 'Approval workflow update');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id, role: 'Viewer'
  }, ownerToken, 'Viewer project grant');

  const detailTask = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Gerçek detay ${stamp.slice(-4)}`), ownerToken, 'Detail task creation');
  const relatedTask = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Bağlı gerçek iş ${stamp.slice(-4)}`), ownerToken, 'Related task creation');
  await requireApi(`/api/work-items/${detailTask.id}`, 'PUT', {
    title: detailTask.title, description: '<img src=x onerror=alert(1)>\nGerçek işbirliği ayrıntısı', priority: detailTask.priority, dueDate: detailTask.dueDate
  }, ownerToken, 'Safe content seed');
  await requireApi(`/api/work-items/${detailTask.id}/comments`, 'POST', {
    body: 'Viewer gözden geçirsin.', mentions: [viewerRegistration.user.id]
  }, ownerToken, 'Mention comment seed');
  await requireApi(`/api/work-items/${detailTask.id}/worklogs`, 'POST', {
    userId: owner.id, hours: 1.25, note: 'Gerçek ayrıntı doğrulaması'
  }, ownerToken, 'Worklog seed');
  await requireApi(`/api/work-items/${detailTask.id}/relations`, 'POST', {
    relatedWorkItemId: relatedTask.id, relationType: 'RelatesTo'
  }, ownerToken, 'Relation seed');
  await requireApi(`/api/work-items/${detailTask.id}/approvals`, 'POST', {
    targetStatus: 'In Progress'
  }, ownerToken, 'Approval seed');
  checks.push('real-collaboration-seed');

  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'owner');
  await openDesktopTask(ownerPage, project, board, detailTask);
  assert.equal(await ownerPage.locator('.safe-rich-text img').count(), 0);
  assert.match(await ownerPage.getByLabel('Görev açıklaması ve kabul ölçütleri').inputValue(), /<img src=x onerror=alert\(1\)>/);
  const watchButton = ownerPage.getByRole('button', { name: 'Görev takibini değiştir' });
  const voteButton = ownerPage.getByRole('button', { name: 'Görev oyunu değiştir' });
  await watchButton.click();
  await ownerPage.waitForFunction(() => document.querySelector('[aria-label="Görev takibini değiştir"]')?.getAttribute('aria-pressed') === 'true');
  await voteButton.click();
  await ownerPage.waitForFunction(() => document.querySelector('[aria-label="Görev oyunu değiştir"]')?.getAttribute('aria-pressed') === 'true');
  const collaboration = await requireApi(`/api/work-items/${detailTask.id}/collaboration`, 'GET', undefined, ownerToken, 'Collaboration read');
  assert.equal(collaboration.watching, true);
  assert.equal(collaboration.voted, true);
  checks.push('real-owner-watch-vote');

  await ownerPage.getByRole('tab', { name: 'Yorumlar', exact: true }).click();
  await ownerPage.getByText('Viewer gözden geçirsin.', { exact: true }).waitFor();
  await ownerPage.getByRole('tab', { name: 'Çalışma', exact: true }).click();
  await ownerPage.locator('.task-activity .task-event', { hasText: 'Gerçek ayrıntı doğrulaması' }).waitFor();
  await ownerPage.locator('.relation-row', { hasText: relatedTask.title }).waitFor();
  await ownerPage.locator('.approval-row', { hasText: 'Pending' }).waitFor();
  checks.push('real-stream-relation-approval');

  const fileInput = ownerPage.locator('.task-upload input[type="file"]');
  await fileInput.setInputFiles({ name: 'gerçek-kanıt.txt', mimeType: 'text/plain', buffer: Buffer.from('kanıt') });
  await fileInput.dispatchEvent('change');
  await ownerPage.getByRole('button', { name: 'Yükle', exact: true }).click();
  await ownerPage.getByText('gerçek-kanıt.txt', { exact: true }).waitFor();
  checks.push('real-owner-upload');

  await ownerPage.locator('#task-title').fill('Korunan gerçek taslak');
  const current = await requireApi(`/api/work-items/${detailTask.id}`, 'GET', undefined, ownerToken, 'Current task read');
  await requireApi(`/api/work-items/${detailTask.id}`, 'PUT', {
    title: `${detailTask.title} sunucu`, description: current.description || '', priority: current.priority, dueDate: current.dueDate || null
  }, ownerToken, 'External conflict update');
  await ownerPage.locator('.task-detail-notice.warning', { hasText: 'Yerel form değişiklikleriniz korunuyor.' }).waitFor({ timeout: 45_000 });
  assert.equal(await ownerPage.locator('#task-title').inputValue(), 'Korunan gerçek taslak');
  checks.push('real-realtime-draft-preserved');
  await ownerPage.getByRole('button', { name: 'Görevi tam sayfada aç' }).click();
  await ownerPage.locator('.inspector[data-detail-mode="page"]').waitFor();
  await ownerPage.screenshot({ path: resolve(outputDir, 'owner-detail-page.png'), fullPage: true });

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'viewer');
  await openDesktopTask(viewerPage, project, board, { ...detailTask, title: `${detailTask.title} sunucu` });
  assert.equal(await viewerPage.locator('#task-title').count(), 0);
  assert.ok(await viewerPage.getByText('Bu görevde alanlar salt okunur.', { exact: false }).isVisible());
  await viewerPage.locator('#task-comment').fill('Viewer gerçek yorumu');
  await viewerPage.getByRole('button', { name: 'Yorum gönder' }).click();
  await viewerPage.getByRole('tab', { name: 'Yorumlar', exact: true }).click();
  await viewerPage.getByText('Viewer gerçek yorumu', { exact: true }).waitFor();
  checks.push('real-viewer-comment-boundary');
  await viewerPage.screenshot({ path: resolve(outputDir, 'viewer-detail-drawer.png'), fullPage: true });

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
  await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  attachDiagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/tasks/${detailTask.id}`, { waitUntil: 'domcontentloaded' });
  await mobilePage.locator('.mobile-task-header h1').waitFor({ timeout: 45_000 });
  assert.equal(await mobilePage.locator('.mobile-task-description img').count(), 0);
  await mobilePage.getByRole('button', { name: /Etkinlik/ }).click();
  await mobilePage.getByRole('button', { name: 'Yorumlar', exact: true }).click();
  await mobilePage.getByText('Viewer gerçek yorumu', { exact: true }).waitFor();
  checks.push('real-mobile-detail-collaboration');
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile detail overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  checks.push('real-mobile-no-overflow');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-detail-activity.png'), fullPage: true });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1, taskId: 'V3-UX-006', runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 8,
    apiBaseUrl, frontendBaseUrl, checks, cleanup: cleanupResult, failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.equal(checks.length, 8, `Expected 8 real checks, received ${checks.length}`);
console.log('V3-UX-006 real-browser passed: collaboration streams, upload, conflict draft, Viewer comment, mobile parity and cleanup.');
