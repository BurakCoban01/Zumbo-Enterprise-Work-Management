import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-mobile-002-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-MOBILE-002', 'chromium');
const tenantId = runContext.tenants.mobile;
const password = 'P@ssword123';
const checks = [];
const failures = [];
let browser;
let cleanup = { tenantId, status: 0, passed: false, error: null };
let ownerToken;

await mkdir(output, { recursive: true });
await buildFrontend();

async function apiRequest(path, method = 'GET', body, token = ownerToken) {
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

async function eventually(read, predicate, label, timeout = 20_000) {
  const startedAt = Date.now();
  let value;
  while (Date.now() - startedAt < timeout) {
    value = await read();
    if (predicate(value)) return value;
    await new Promise(resolvePromise => setTimeout(resolvePromise, 250));
  }
  assert.fail(`${label} did not reach the expected state: ${JSON.stringify(value)}`);
}

async function browserLogin(context, usernameOrEmail) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin },
    data: { usernameOrEmail, password }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser login failed');
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, payload.data);
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (/WebSocket|signalr|Failed to start the connection|Failed to load resource/.test(detail)) return;
    failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

async function assertMobileLayout(page) {
  const layout = await page.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(layout.scrollWidth <= layout.width + 1, `Horizontal overflow: ${layout.scrollWidth}/${layout.width}`);
}

function taskPayload(project, board, owner, title) {
  return {
    projectId: project.id,
    boardId: board.id,
    title,
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id,
    estimatePoints: 3
  };
}

async function openTask(page, taskId, title) {
  await page.goto(`${frontendBaseUrl}/mobile-ionic/index.html?e2e=${Date.now()}#/tasks/${taskId}`, { waitUntil: 'domcontentloaded' });
  await page.locator('.mobile-task-header h1', { hasText: title }).waitFor({ timeout: 45_000 });
}

try {
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `mobilework${stamp}`,
    email: `mobilework${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Owner registration');
  const owner = ownerRegistration.user;
  ownerToken = ownerRegistration.accessToken;
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Mobile Work Parity',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');

  const viewerEmail = `mobileviewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Mobil Teslimat Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `mobileviewer${stamp}`,
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');
  const approverEmail = `mobileapprover${stamp}@zumbo.local`;
  const approverInvitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: approverEmail,
    role: 'Member'
  }, ownerToken, 'Approver invitation');
  const approverRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `mobileapprover${stamp}`,
    email: approverEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Approver registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: approverInvitation.invitationToken
  }, approverRegistration.accessToken, 'Approver invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `MW${stamp.slice(-5)}`,
    name: 'Mobil İş Akışları',
    ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Mobil Operasyon Panosu',
    type: 'Scrum'
  }, ownerToken, 'Board creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Viewer project grant');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: approverRegistration.user.id,
    role: 'ProjectAdmin'
  }, ownerToken, 'Approver project grant');

  const boardTask = await requireApi('/api/work-items', 'POST',
    taskPayload(project, board, owner, `Dokunmatik taşıma ${stamp.slice(-4)}`),
    ownerToken, 'Board task creation');
  const backlogTask = await requireApi('/api/work-items', 'POST',
    taskPayload(project, board, owner, `Backlog planlama ${stamp.slice(-4)}`),
    ownerToken, 'Backlog task creation');
  const detailTask = await requireApi('/api/work-items', 'POST',
    taskPayload(project, board, owner, `Mobil ayrıntı ${stamp.slice(-4)}`),
    ownerToken, 'Detail task creation');
  const relatedTask = await requireApi('/api/work-items', 'POST',
    taskPayload(project, board, owner, `Bağlı mobil görev ${stamp.slice(-4)}`),
    ownerToken, 'Related task creation');
  const approvalTask = await requireApi('/api/work-items', 'POST',
    taskPayload(project, board, owner, `Mobil onay ${stamp.slice(-4)}`),
    ownerToken, 'Approval task creation');

  await requireApi(`/api/work-items/${approvalTask.id}/status`, 'PATCH', {
    status: 'In Progress'
  }, ownerToken, 'Approval task preparation');
  const workflow = await requireApi(`/api/workflows/${project.id}`, 'GET', undefined, ownerToken, 'Workflow read');
  const approvalTransitions = workflow.transitions.map(transition => ({
    ...transition,
    requiresApproval: transition.fromStatus === 'In Progress' && transition.toStatus === 'Code Review'
      ? true
      : transition.requiresApproval
  }));
  await requireApi(`/api/workflows/${project.id}`, 'PUT', {
    projectId: project.id,
    statuses: workflow.statuses,
    transitions: approvalTransitions
  }, ownerToken, 'Approval workflow update');
  await requireApi(`/api/work-items/${approvalTask.id}/approvals`, 'POST', {
    targetStatus: 'Code Review'
  }, ownerToken, 'Approval request');

  const today = new Date();
  const sprint = await requireApi('/api/sprints', 'POST', {
    projectId: project.id,
    name: `Mobil sprint ${stamp.slice(-4)}`,
    goal: 'Mobil planlama akışını doğrula',
    startDate: today.toISOString().slice(0, 10),
    endDate: new Date(today.getTime() + 13 * 86400000).toISOString().slice(0, 10)
  }, ownerToken, 'Sprint creation');

  browser = await chromium.launch({ headless: true });
  const ownerContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'owner-mobile-work');

  await ownerPage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}`, {
    waitUntil: 'domcontentloaded'
  });
  await ownerPage.getByRole('heading', { name: project.name }).waitFor({ timeout: 45_000 });
  await ownerPage.getByRole('button', { name: 'Pano', exact: true }).click();
  await ownerPage.getByRole('tab', { name: 'Pano', exact: true }).waitFor({ timeout: 45_000 });
  const nextButton = ownerPage.getByRole('button', { name: `${boardTask.title} görevini sonraki kolona taşı` });
  await nextButton.waitFor();
  const targetSize = await nextButton.boundingBox();
  assert.ok(targetSize && targetSize.width >= 44 && targetSize.height >= 44, `Move target was ${JSON.stringify(targetSize)}`);
  await nextButton.click();
  await eventually(
    () => requireApi(`/api/work-items/${boardTask.id}`, 'GET', undefined, ownerToken, 'Moved task read'),
    task => task.status === 'In Progress',
    'Board move'
  );
  checks.push('real-touch-safe-board-move');
  await assertMobileLayout(ownerPage);
  await ownerPage.screenshot({ path: resolve(output, 'owner-board-moved.png'), fullPage: true });

  await ownerPage.getByRole('tab', { name: 'Liste', exact: true }).click();
  await ownerPage.getByText(boardTask.title, { exact: true }).waitFor();
  checks.push('real-list-mode');

  await ownerPage.getByRole('tab', { name: 'Backlog', exact: true }).click();
  await ownerPage.getByLabel('Backlog hedef sprinti').selectOption({ label: `${sprint.name} · Planned` });
  await ownerPage.getByRole('button', { name: `${backlogTask.title} işini sprint kapsamına al` }).click();
  await eventually(
    () => requireApi(`/api/work-items/${backlogTask.id}`, 'GET', undefined, ownerToken, 'Planned task read'),
    task => task.sprintId === sprint.id,
    'Backlog planning'
  );
  checks.push('real-backlog-plan');

  await ownerPage.getByRole('tab', { name: 'Sprint', exact: true }).click();
  await ownerPage.getByLabel('Mobil sprint').selectOption({ label: `${sprint.name} · Planned` });
  await ownerPage.getByText(backlogTask.title, { exact: true }).waitFor();
  const sprintState = await ownerPage.locator('section[ng-if="vm.mode === \'sprint\'"]:visible').evaluate(element => {
    const model = window.angular.element(element).scope().vm;
    return {
      mode: model.mode,
      canEdit: model.canEditTasks(),
      selectedSprintId: model.selectedSprintId,
      selectedSprint: model.selectedSprint()
    };
  });
  assert.equal(sprintState.mode, 'sprint');
  assert.equal(sprintState.canEdit, true);
  assert.equal(sprintState.selectedSprintId, sprint.id);
  assert.equal(sprintState.selectedSprint?.status, 'Planned');
  const startSprintButton = ownerPage.locator('.mobile-sprint-actions button:visible').filter({ hasText: 'Başlat' });
  assert.equal(await startSprintButton.count(), 1, `Sprint start command state: ${JSON.stringify(sprintState)}`);
  await startSprintButton.click();
  await ownerPage.locator('.popup-container').getByRole('button', { name: 'Başlat', exact: true }).click();
  await eventually(
    () => requireApi(`/api/sprints/${sprint.id}`, 'GET', undefined, ownerToken, 'Started sprint read'),
    current => current.status === 'Active',
    'Sprint start'
  );
  checks.push('real-sprint-start');
  await ownerPage.screenshot({ path: resolve(output, 'owner-active-sprint.png'), fullPage: true });

  await ownerPage.locator('.zumbo-primary-tabs .tab-item').filter({ hasText: 'Oluştur' }).click();
  await ownerPage.getByRole('heading', { name: 'Görev oluştur' }).waitFor();
  await ownerPage.getByRole('button', { name: 'Görev ayrıntılarına geç' }).click();
  const createPopup = ownerPage.locator('.popup-container');
  await createPopup.waitFor();
  const createdTitle = `Mobil hızlı oluştur ${stamp.slice(-4)}`;
  await createPopup.locator('input[type="text"]').first().fill(createdTitle);
  await createPopup.locator('.popup-buttons .button-positive').click();
  await createPopup.waitFor({ state: 'hidden' });
  const createdSearch = await eventually(
    () => requireApi('/api/work-items/search', 'POST', {
      projectId: project.id,
      text: createdTitle,
      page: 1,
      pageSize: 10
    }, ownerToken, 'Created task search'),
    result => result.items?.some(item => item.title === createdTitle),
    'Mobile task creation'
  );
  const createdTask = createdSearch.items.find(item => item.title === createdTitle);
  checks.push('real-create');

  await openTask(ownerPage, detailTask.id, detailTask.title);
  const editedTitle = `${detailTask.title} güncellendi`;
  await ownerPage.getByLabel('Başlık').fill(editedTitle);
  await ownerPage.getByRole('textbox', { name: 'Açıklama', exact: true }).fill('Mobil ayrıntı akışı gerçek API ile doğrulandı.');
  await ownerPage.getByRole('button', { name: 'Değişiklikleri kaydet' }).click();
  await eventually(
    () => requireApi(`/api/work-items/${detailTask.id}`, 'GET', undefined, ownerToken, 'Edited title read'),
    task => task.title === editedTitle,
    'Task edit persistence'
  );
  await openTask(ownerPage, detailTask.id, editedTitle);
  await ownerPage.getByRole('button', { name: 'In Progress', exact: true }).click();
  await eventually(
    () => requireApi(`/api/work-items/${detailTask.id}`, 'GET', undefined, ownerToken, 'Edited task read'),
    task => task.title === editedTitle && task.status === 'In Progress',
    'Task edit and move'
  );
  checks.push('real-edit-move');

  const checklistText = `Mobil kontrol ${stamp.slice(-4)}`;
  await ownerPage.getByLabel('Yeni kontrol listesi maddesi').scrollIntoViewIfNeeded();
  await ownerPage.getByLabel('Yeni kontrol listesi maddesi').fill(checklistText);
  await ownerPage.getByRole('button', { name: 'Madde ekle' }).click();
  const checklist = ownerPage.getByText(checklistText, { exact: true }).locator('xpath=ancestor::label[1]');
  await checklist.waitFor();
  await checklist.click();
  await eventually(
    () => requireApi(`/api/work-items/${detailTask.id}`, 'GET', undefined, ownerToken, 'Checklist task read'),
    task => task.checklist?.some(item => item.text === checklistText && item.completed),
    'Checklist completion'
  );
  await ownerPage.getByLabel('İlişkili görev').selectOption(relatedTask.id);
  await ownerPage.getByRole('button', { name: 'Bağla', exact: true }).click();
  await eventually(
    () => requireApi(`/api/work-items/${detailTask.id}`, 'GET', undefined, ownerToken, 'Related task read'),
    task => task.relations?.some(relation => relation.relatedWorkItemId === relatedTask.id),
    'Task relation'
  );
  await ownerPage.locator('.mobile-task-relation').filter({ hasText: relatedTask.title }).waitFor();
  checks.push('real-checklist-relation');

  const fileName = `mobil-kanit-${stamp.slice(-4)}.txt`;
  await ownerPage.getByLabel('Görev dosyası seç').setInputFiles({
    name: fileName,
    mimeType: 'text/plain',
    buffer: Buffer.from('synthetic mobile evidence')
  });
  await ownerPage.getByRole('button', { name: 'Dosya yükle' }).click();
  await ownerPage.getByText(fileName, { exact: true }).waitFor();
  checks.push('real-attachment-upload');

  const watchButton = ownerPage.getByRole('button', { name: 'Görevi takip et' });
  const voteButton = ownerPage.getByRole('button', { name: 'Göreve oy ver' });
  await watchButton.click();
  await ownerPage.waitForFunction(() => document.querySelector('[aria-label="Görevi takip et"]')?.getAttribute('aria-pressed') === 'true');
  await voteButton.click();
  await ownerPage.waitForFunction(() => document.querySelector('[aria-label="Göreve oy ver"]')?.getAttribute('aria-pressed') === 'true');
  const collaboration = await requireApi(`/api/work-items/${detailTask.id}/collaboration`, 'GET', undefined, ownerToken, 'Collaboration read');
  assert.equal(collaboration.watching, true);
  assert.equal(collaboration.voted, true);
  checks.push('real-watch-vote');

  await ownerPage.getByRole('button', { name: /Etkinlik/ }).click();
  const commentText = `Mobil yorum ${stamp.slice(-4)}`;
  await ownerPage.getByLabel('Yorum ekle').fill(commentText);
  await ownerPage.getByRole('button', { name: 'Yorumu gönder' }).click();
  await ownerPage.getByRole('button', { name: 'Yorumlar', exact: true }).click();
  await ownerPage.getByText(commentText, { exact: true }).waitFor();
  const worklogNote = `Mobil çalışma ${stamp.slice(-4)}`;
  await ownerPage.getByLabel('Çalışma saati').fill('1.25');
  await ownerPage.getByLabel('Çalışma notu').fill(worklogNote);
  await ownerPage.locator('.mobile-task-worklog-form').getByRole('button', { name: 'Ekle', exact: true }).click();
  await ownerPage.getByRole('button', { name: 'Çalışma', exact: true }).click();
  await ownerPage.locator('.mobile-task-worklog').filter({ hasText: worklogNote }).waitFor();
  checks.push('real-comment-worklog');
  await assertMobileLayout(ownerPage);
  await ownerPage.screenshot({ path: resolve(output, 'owner-detail-activity.png'), fullPage: true });

  await openTask(ownerPage, approvalTask.id, approvalTask.title);
  await ownerPage.getByRole('button', { name: /Etkinlik/ }).click();
  await ownerPage.getByLabel('Karar notu').fill('Mobil onay doğrulandı.');
  await ownerPage.getByRole('button', { name: 'Onayla', exact: true }).click();
  await ownerPage.locator('.mobile-task-notice.error').waitFor();
  const selfApproval = await requireApi(
    `/api/work-items/${approvalTask.id}/approvals?page=1&pageSize=50`,
    'GET',
    undefined,
    ownerToken,
    'Self approval state read'
  );
  assert.equal(selfApproval.items[0].status, 'Pending');
  checks.push('real-self-approval-denied');

  const approverContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce'
  });
  await browserLogin(approverContext, approverRegistration.user.username);
  const approverPage = await approverContext.newPage();
  diagnostics(approverPage, 'approver-mobile-work');
  await openTask(approverPage, approvalTask.id, approvalTask.title);
  await approverPage.getByRole('button', { name: /Etkinlik/ }).click();
  await approverPage.getByLabel('Karar notu').fill('Bağımsız mobil onay doğrulandı.');
  await approverPage.getByRole('button', { name: 'Onayla', exact: true }).click();
  await eventually(
    () => requireApi(`/api/work-items/${approvalTask.id}/approvals?page=1&pageSize=50`, 'GET', undefined, ownerToken, 'Approval state read'),
    result => result.items?.some(item => item.status === 'Approved'),
    'Approval decision'
  );
  await approverPage.getByRole('button', { name: 'Detay', exact: true }).click();
  await approverPage.getByRole('button', { name: 'Code Review', exact: true }).click();
  await eventually(
    () => requireApi(`/api/work-items/${approvalTask.id}`, 'GET', undefined, ownerToken, 'Approved task read'),
    task => task.status === 'Code Review',
    'Approved transition'
  );
  checks.push('real-approve-transition');
  await approverPage.screenshot({ path: resolve(output, 'approver-transition.png'), fullPage: true });
  await approverContext.close();

  await ownerPage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/app/search`, { waitUntil: 'domcontentloaded' });
  await ownerPage.getByLabel('Arama projesi').selectOption({ label: `${project.key} · ${project.name}` });
  await ownerPage.getByPlaceholder('Başlık veya içerik ara').fill(editedTitle);
  await ownerPage.getByRole('button', { name: 'Ara', exact: true }).click();
  await ownerPage.locator('.mobile-search-results:visible').getByText(editedTitle, { exact: true }).waitFor();
  checks.push('real-search');

  const notifications = await eventually(
    () => requireApi(`/api/notifications/${owner.id}`, 'GET', undefined, ownerToken, 'Notification read'),
    items => items.some(item => !item.read),
    'Unread notification'
  );
  const unread = notifications.find(item => !item.read);
  await ownerPage.locator('.zumbo-primary-tabs .tab-item').filter({ hasText: 'Gelen kutusu' }).click();
  const notificationRow = ownerPage.locator('ion-item').filter({ hasText: unread.message }).first();
  await notificationRow.waitFor();
  await notificationRow.click();
  await eventually(
    () => requireApi(`/api/notifications/${owner.id}`, 'GET', undefined, ownerToken, 'Read notification state'),
    items => items.some(item => item.id === unread.id && item.read),
    'Notification read command'
  );
  checks.push('real-inbox-read');

  await openTask(ownerPage, createdTask.id, createdTask.title);
  await ownerContext.setOffline(true);
  await ownerPage.getByText('Çevrimdışıyken değişiklikler devre dışıdır.', { exact: true }).waitFor();
  assert.equal(await ownerPage.getByRole('button', { name: 'Değişiklikleri kaydet' }).isDisabled(), true);
  assert.equal(await ownerPage.getByRole('button', { name: 'Görevi takip et' }).isDisabled(), true);
  checks.push('real-offline-mutation-block');
  await ownerPage.screenshot({ path: resolve(output, 'owner-offline-detail.png'), fullPage: true });
  await ownerContext.setOffline(false);
  await openTask(ownerPage, createdTask.id, createdTask.title);

  const viewerContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce'
  });
  await browserLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'viewer-mobile-work');
  await openTask(viewerPage, detailTask.id, editedTitle);
  await viewerPage.getByText('Görev alanları salt okunur.', { exact: false }).waitFor();
  assert.equal(await viewerPage.locator('form.mobile-task-form').count(), 0);
  assert.equal(await viewerPage.locator('.mobile-task-action-row').count(), 0);
  assert.equal(await viewerPage.locator('.mobile-task-upload').count(), 0);
  await viewerPage.getByRole('button', { name: /Etkinlik/ }).click();
  const viewerComment = `Viewer mobil yorumu ${stamp.slice(-4)}`;
  await viewerPage.getByLabel('Yorum ekle').fill(viewerComment);
  await viewerPage.getByRole('button', { name: 'Yorumu gönder' }).click();
  await viewerPage.getByRole('button', { name: 'Yorumlar', exact: true }).click();
  await viewerPage.getByText(viewerComment, { exact: true }).waitFor();
  await assertMobileLayout(viewerPage);
  checks.push('real-viewer-read-only-comment');
  await viewerPage.screenshot({ path: resolve(output, 'viewer-read-only-detail.png'), fullPage: true });

  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  if (ownerToken) {
    const result = await apiRequest(`/api/organizations/${encodeURIComponent(tenantId)}/archive`, 'POST', undefined, ownerToken)
      .catch(error => ({ response: { ok: false, status: 0 }, payload: { error: { message: error.message } } }));
    cleanup = {
      tenantId,
      status: result.response.status,
      passed: result.response.ok || result.response.status === 404,
      error: result.response.ok || result.response.status === 404 ? null : result.payload.error?.message
    };
  }
  await browser?.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-MOBILE-002',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanup.passed && checks.length === 16,
    apiBaseUrl,
    frontendBaseUrl,
    viewports: ['390x844'],
    checks,
    cleanup,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanup.passed, true, cleanup.error || 'Tenant cleanup failed');
assert.equal(checks.length, 16, `Expected 16 real checks, received ${checks.length}`);
console.log('V3-MOBILE-002 real-browser passed: essential mobile work parity, permissions, offline state and cleanup.');
