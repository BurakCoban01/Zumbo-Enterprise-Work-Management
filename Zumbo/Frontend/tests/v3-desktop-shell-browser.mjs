import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const artifactRoot = resolve(root, '../artifacts/ui/v3-ux');
await mkdir(artifactRoot, { recursive: true });
await buildFrontend();

const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const user = {
  id: 'user-1',
  username: 'deniz',
  email: 'deniz@zumbo.local',
  organizationId: 'org-opaque-001',
  roles: ['User']
};
const board = {
  id: 'board-1',
  projectId: 'project-1',
  name: 'Platform Akışı',
  type: 'Kanban',
  swimlaneMode: 'None',
  version: 1,
  views: [],
  columns: [
    { id: 'column-todo', name: 'Yapılacak', category: 'Todo', position: 0, wipLimit: null },
    { id: 'column-progress', name: 'Devam Ediyor', category: 'InProgress', position: 1, wipLimit: 4 },
    { id: 'column-done', name: 'Tamamlandı', category: 'Done', position: 2, wipLimit: null }
  ]
};
const task = {
  id: 'task-1',
  projectId: 'project-1',
  boardId: board.id,
  columnId: 'column-progress',
  title: 'İzin akışını doğrula',
  description: 'Kabuk yetki ve geçmiş davranışı.',
  type: 'Task',
  status: 'Devam Ediyor',
  priority: 'High',
  assigneeUserId: user.id,
  labels: ['UX'],
  checklist: [],
  comments: [],
  attachments: [],
  workLogs: [],
  relations: [],
  approvals: [],
  customFields: [],
  rank: 1000,
  version: 1
};

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-browser' });
}

function failureEnvelope() {
  return JSON.stringify({
    success: false,
    data: null,
    error: { code: 'DEPENDENCY_UNAVAILABLE', message: 'Fixture search unavailable.' },
    correlationId: 'v3-ux-browser-error'
  });
}

function projectFor(role) {
  return {
    id: 'project-1',
    organizationId: user.organizationId,
    key: 'UX',
    name: 'Deneyim Platformu',
    visibility: 'Internal',
    ownerUserId: role === 'Viewer' ? 'owner-2' : user.id,
    members: [{ userId: user.id, role }],
    teamIds: [],
    version: 1
  };
}

function responseFor(url, role) {
  const path = url.pathname;
  if (path === '/api/browser-auth/session') return { user, csrfToken: 'v3-ux-csrf' };
  if (path === '/api/projects') return [projectFor(role)];
  if (path === '/api/boards/by-project/project-1') return [board];
  if (path === '/api/work-items/search') return { items: [task], page: 1, pageSize: 100, total: 1, degraded: false };
  if (path === '/api/work-items/task-1') return task;
  if (path === '/api/work-items/reports/project-summary/project-1') return { total: 1, inProgress: 1, done: 0, overdue: 0 };
  if (path.startsWith('/api/work-items/reports/')) return [];
  if (path === '/api/workflows/project-1') return {
    projectId: 'project-1',
    statuses: board.columns.map(column => ({ name: column.name, category: column.category })),
    transitions: []
  };
  if (path === '/api/work-item-schemas/project-1') return {
    issueTypes: [{ key: 'Task', name: 'Görev', hierarchyLevel: 'Standard', active: true }],
    customFields: [],
    layouts: []
  };
  if (path.startsWith('/api/sprints/projects/project-1')) return { items: [], page: 1, pageSize: 50, total: 0 };
  if (path === '/api/notifications') return [{ id: 'notification-1', type: 'Assignment', message: 'Bir görev size atandı.', read: false }];
  if (path === '/api/teams') return [];
  if (path === '/api/auth/users') return [user];
  if (path.startsWith('/api/audit/entity/')) return [];
  return [];
}

async function createSurface(role, viewport, options = {}) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  const page = await context.newPage();
  const failures = [];
  page.on('pageerror', error => failures.push(error.message));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    const expectedMockRealtimeFailure = detail.includes('/hubs/work-items')
      || detail.includes('Failed to start the connection');
    if (!detail.includes('Failed to load resource') && !expectedMockRealtimeFailure) failures.push(detail);
  });
  await page.route(`${apiBaseUrl}/**`, async route => {
    if (route.request().method() === 'OPTIONS') {
      await route.fulfill({ status: 204, body: '' });
      return;
    }
    const requestUrl = new URL(route.request().url());
    if (requestUrl.pathname === '/api/work-items/search' && options.searchDelay) {
      await new Promise(resolveWait => setTimeout(resolveWait, options.searchDelay));
    }
    if (requestUrl.pathname === '/api/work-items/search' && options.searchFailure) {
      await route.fulfill({ status: 503, contentType: 'application/json', body: failureEnvelope() });
      return;
    }
    const data = responseFor(requestUrl, role);
    await route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1`, {
    waitUntil: options.waitForTask === false ? 'domcontentloaded' : 'networkidle'
  });
  await page.locator('.side-nav').waitFor();
  if (options.waitForTask !== false) await page.locator('.task').filter({ hasText: task.title }).waitFor();
  return { context, page, failures };
}

try {
  const owner = await createSurface('ProjectOwner', { width: 1440, height: 1000 });
  const { page } = owner;
  const nav = page.locator('.side-nav');

  await nav.getByRole('button', { name: 'Raporlar', exact: true }).click();
  await page.waitForFunction(() => location.hash.includes('section=reports'));
  await nav.getByRole('button', { name: 'Ekipler', exact: true }).click();
  await page.waitForFunction(() => location.hash.includes('section=teams'));
  await page.goBack();
  await page.waitForFunction(() => location.hash.includes('section=reports'));
  assert.ok(await nav.getByRole('button', { name: 'Raporlar', exact: true }).evaluate(element => element.classList.contains('active')));
  await page.goForward();
  await page.waitForFunction(() => location.hash.includes('section=teams'));
  assert.ok(await nav.getByRole('button', { name: 'Ekipler', exact: true }).evaluate(element => element.classList.contains('active')));

  await page.getByRole('button', { name: 'Bildirimler', exact: true }).click();
  await page.locator('.notification-popover').getByText('Bir görev size atandı.').waitFor();
  await page.getByRole('button', { name: 'Bildirimler', exact: true }).click();
  await page.getByRole('button', { name: 'Kullanıcı menüsü', exact: true }).click();
  assert.equal(await page.locator('.user-popover').getByText(user.email, { exact: true }).isVisible(), true);
  assert.equal(await page.locator('.user-popover').getByRole('button', { name: 'Çıkış yap', exact: true }).isVisible(), true);
  await page.getByRole('button', { name: 'Kullanıcı menüsü', exact: true }).click();

  await page.keyboard.press('Control+K');
  const commandInput = page.getByRole('combobox', { name: 'Komut ara' });
  await commandInput.fill('İzin akışı');
  await page.locator('[role="option"]').filter({ hasText: task.title }).waitFor();
  await commandInput.press('Enter');
  await page.waitForTimeout(250);
  const commandState = await page.evaluate(() => {
    const scope = window.angular.element(document.body).scope();
    return {
      activeCommandIndex: scope.vm.activeCommandIndex,
      activeSection: scope.vm.activeSection,
      commandOpen: scope.vm.commandOpen,
      commandQuery: scope.vm.commandQuery,
      selectedTaskId: scope.vm.selectedTask && scope.vm.selectedTask.id,
      resultCount: scope.vm.commandResultCount()
    };
  });
  assert.equal(commandState.commandOpen, false, `command did not execute: ${JSON.stringify(commandState)}`);
  assert.equal(commandState.activeSection, 'board', `task command did not activate board: ${JSON.stringify(commandState)}`);
  await page.locator('.inspector').waitFor();
  await assert.doesNotReject(() => page.locator('#task-title').waitFor());
  assert.equal(await page.locator('#task-title').inputValue(), task.title);
  await page.waitForFunction(() => location.hash.includes('section=board') && location.hash.includes('task=task-1'));
  assert.ok(await nav.getByRole('button', { name: 'Pano', exact: true }).evaluate(element => element.classList.contains('active')));

  const themeButton = page.getByRole('button', { name: 'Temayı değiştir', exact: true }).first();
  await themeButton.click();
  await page.locator('body.theme-dark').waitFor();
  await themeButton.click();
  assert.equal(await page.locator('body.theme-dark').count(), 0);
  assert.equal((await page.locator('.title-context').innerText()).includes(user.organizationId), false);

  await page.screenshot({ path: resolve(artifactRoot, 'shell-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Görev detayını kapat', exact: true }).click();
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-work-item-id') === 'task-1');
  assert.ok(await page.locator('.command-trigger').isVisible(), 'command trigger disappeared at 390px');
  await page.locator('.command-trigger').click();
  await page.getByRole('combobox', { name: 'Komut ara' }).waitFor();
  await page.keyboard.press('Escape');
  const mobileLayout = await page.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(mobileLayout.scrollWidth <= mobileLayout.width + 1, `desktop shell overflowed at 390px: ${mobileLayout.scrollWidth}/${mobileLayout.width}`);
  await page.screenshot({ path: resolve(artifactRoot, 'shell-responsive.png'), fullPage: true });
  assert.deepEqual(owner.failures, []);
  await owner.context.close();

  const viewer = await createSurface('Viewer', { width: 1280, height: 900 });
  await viewer.page.locator('.create-button').click();
  assert.equal(await viewer.page.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).count(), 0);
  await viewer.page.keyboard.press('Control+K');
  await viewer.page.getByRole('combobox', { name: 'Komut ara' }).fill('Yeni görev');
  await viewer.page.getByText('Eşleşen komut veya görev yok.').waitFor();
  assert.equal(await viewer.page.locator('[role="option"]').count(), 0);
  assert.deepEqual(viewer.failures, []);
  await viewer.context.close();

  const degraded = await createSurface('ProjectOwner', { width: 1280, height: 900 }, {
    searchDelay: 750,
    searchFailure: true,
    waitForTask: false
  });
  await degraded.page.locator('.board-skeleton').waitFor();
  await degraded.page.getByText('Pano verileri yüklenemedi.').waitFor();
  assert.equal(await degraded.page.locator('.board-skeleton').count(), 0);
  assert.deepEqual(degraded.failures, []);
  await degraded.context.close();
} finally {
  await browser.close();
  await server.close();
}

console.log('V3-UX-001 shell browser passed: deep-link/history, command keyboard, role visibility, theme and 390px reflow.');
