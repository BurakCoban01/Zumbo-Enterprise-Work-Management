import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-ux-004');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = Date.now();
const user = { id: 'user-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const teammate = { id: 'user-2', username: 'mert', email: 'mert@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const columns = [
  { id: 'todo', name: 'To Do', category: 'Todo', position: 0, wipLimit: 18 },
  { id: 'doing', name: 'In Progress', category: 'InProgress', position: 1, wipLimit: 16 },
  { id: 'review', name: 'Review', category: 'InProgress', position: 2, wipLimit: 10 },
  { id: 'done', name: 'Done', category: 'Done', position: 3, wipLimit: null }
];
const board = { id: 'board-1', projectId: 'project-1', name: 'Teslimat Panosu', type: 'Kanban', swimlaneMode: 'None', views: [], columns };
const workflow = {
  projectId: 'project-1',
  statuses: columns.map(item => ({ name: item.name, category: item.category })),
  transitions: [
    { fromStatus: 'To Do', toStatus: 'In Progress' },
    { fromStatus: 'In Progress', toStatus: 'To Do' },
    { fromStatus: 'In Progress', toStatus: 'Review' },
    { fromStatus: 'Review', toStatus: 'In Progress' },
    { fromStatus: 'Review', toStatus: 'Done' }
  ]
};

function createFixture(role = 'ProjectOwner') {
  const project = {
    id: 'project-1', organizationId: 'org-1', key: 'OPS', name: 'Operasyon Teslimatı', visibility: 'Private',
    members: [{ userId: user.id, role }, { userId: teammate.id, role: 'Developer' }], teamIds: []
  };
  const tasks = [];
  const counts = { todo: 14, doing: 14, review: 10, done: 10 };
  let sequence = 0;
  for (const column of columns) {
    for (let index = 0; index < counts[column.id]; index += 1) {
      sequence += 1;
      tasks.push({
        id: `task-${String(sequence).padStart(2, '0')}`,
        projectId: project.id,
        boardId: board.id,
        columnId: column.id,
        title: index === 0 && column.id === 'todo' ? 'Kritik erişim akışını doğrula' : `${column.name} operasyon işi ${index + 1}`,
        description: '',
        type: index % 6 === 0 ? 'Bug' : 'Task',
        status: column.name,
        priority: ['Critical', 'High', 'Medium', 'Low'][(sequence + index) % 4],
        assigneeUserId: index % 3 === 0 ? user.id : teammate.id,
        dueDate: new Date(now + (sequence % 7 - 2) * 86400000).toISOString(),
        estimatePoints: [1, 2, 3, 5, 8][sequence % 5],
        labels: index % 4 === 0 ? ['pilot', 'backend'] : [],
        checklist: [], comments: [], attachments: [], workLogs: [], approvals: [], customFields: [], statusHistory: [],
        relations: index === 0 && column.id === 'todo' ? [{ relatedWorkItemId: 'task-99', relationType: 'BlockedBy' }] : [],
        rank: sequence * 1000,
        version: 1
      });
    }
  }
  return { project, tasks };
}

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-004' });
}

function errorEnvelope(code, message) {
  return JSON.stringify({ success: false, data: null, error: { code, message }, correlationId: 'v3-ux-004' });
}

function taskColumn(status) {
  return columns.find(column => column.name === status);
}

async function createContext(viewport, role = 'ProjectOwner') {
  const fixture = createFixture(role);
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    const body = request.postData() ? request.postDataJSON() : null;
    const taskMatch = path.match(/^\/api\/work-items\/(task-\d+)$/);
    const statusMatch = path.match(/^\/api\/work-items\/(task-\d+)\/status$/);

    if (statusMatch && method === 'PATCH') {
      const task = fixture.tasks.find(item => item.id === statusMatch[1]);
      const target = taskColumn(body.status);
      const targetCount = fixture.tasks.filter(item => item.columnId === target.id).length;
      if (target.wipLimit && targetCount >= target.wipLimit && task.columnId !== target.id) {
        return route.fulfill({ status: 409, contentType: 'application/json', body: errorEnvelope('BOARD_WIP_LIMIT_EXCEEDED', 'WIP limit exceeded.') });
      }
      task.status = target.name;
      task.columnId = target.id;
      task.version += 1;
      return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(task) });
    }
    if (taskMatch && method === 'PUT') {
      const task = fixture.tasks.find(item => item.id === taskMatch[1]);
      Object.assign(task, body, { version: task.version + 1 });
      return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(task) });
    }
    if (path === '/api/work-items/bulk/assign' && method === 'POST') {
      const results = body.workItemIds.map((id, index) => index === body.workItemIds.length - 1
        ? { workItemId: id, success: false, errorCode: 'RESOURCE_BUSY', errorMessage: 'Resource busy.' }
        : { workItemId: id, success: true, errorCode: null, errorMessage: null });
      for (const result of results.filter(item => item.success)) {
        const task = fixture.tasks.find(item => item.id === result.workItemId);
        task.assigneeUserId = body.assigneeUserId;
        task.version += 1;
      }
      return route.fulfill({ status: 200, contentType: 'application/json', body: envelope({
        results, succeeded: results.filter(item => item.success).length, failed: results.filter(item => !item.success).length
      }) });
    }

    let data;
    if (path === '/api/browser-auth/session') data = { user, csrfToken: 'csrf' };
    else if (path === '/api/projects') data = [fixture.project];
    else if (path === '/api/boards/by-project/project-1') data = [board];
    else if (path === '/api/work-items/search' && method === 'POST') data = { items: fixture.tasks, totalCount: fixture.tasks.length, degraded: false };
    else if (taskMatch && method === 'GET') data = fixture.tasks.find(item => item.id === taskMatch[1]);
    else if (path === '/api/work-items/reports/project-summary/project-1') data = { total: 48, inProgress: 24, done: 10, overdue: 8 };
    else if (path === '/api/work-items/reports/status-distribution/project-1') data = columns.map(column => ({ status: column.name, count: fixture.tasks.filter(item => item.columnId === column.id).length }));
    else if (path === '/api/work-items/reports/user-workload/project-1') data = [];
    else if (path === '/api/work-items/reports/due-date-risks/project-1') data = fixture.tasks.slice(0, 3);
    else if (path === '/api/work-items/reports/sprint-velocity/project-1') data = [];
    else if (path.startsWith('/api/work-items/reports/')) data = [];
    else if (path === '/api/workflows/project-1') data = workflow;
    else if (path === '/api/work-item-schemas/project-1') data = { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
    else if (path === '/api/sprints/projects/project-1') data = { items: [], totalCount: 0 };
    else if (path === '/api/sprints/projects/project-1/backlog') data = { items: [], totalCount: 0 };
    else if (path.startsWith('/api/audit/entity/')) data = [];
    else if (path === '/api/teams') data = [];
    else if (path === '/api/auth/users') data = [user, teammate];
    else if (path === '/api/notifications' || path === `/api/notifications/${user.id}`) data = [];
    else data = [];
    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return { context, fixture };
}

function watchConsole(page) {
  const errors = [];
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const value = message.text();
    if (/WebSocket connection .*\/hubs\/work-items|Failed to start the connection/.test(value)) return;
    if (/Failed to load resource: the server responded with a status of 409/.test(value)) return;
    errors.push(value);
  });
  return errors;
}

try {
  const owner = await createContext({ width: 1440, height: 1000 });
  const page = await owner.context.newPage();
  const consoleErrors = watchConsole(page);
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=board`, { waitUntil: 'networkidle' });
  await page.locator('.board-shell .task').first().waitFor();
  assert.equal(await page.locator('.column-lane').count(), 4);
  const reviewLane = page.locator('.column-lane').filter({ hasText: 'Review' }).first();
  assert.equal(await reviewLane.getAttribute('data-wip-state'), 'full');
  assert.match(await reviewLane.locator('.wip-count').innerText(), /10\s*\/\s*10/);
  assert.ok(await page.getByText('1 engel', { exact: true }).isVisible());
  assert.ok(await page.getByText('8 puan', { exact: true }).first().isVisible());
  await page.screenshot({ path: resolve(output, 'desktop-board-large.png') });

  const keyboardTask = page.locator('[data-work-item-id="task-02"]');
  await keyboardTask.focus();
  await keyboardTask.press('Alt+ArrowRight');
  await page.waitForFunction(() => document.querySelector('[data-work-item-id="task-02"]')?.closest('.column-lane')?.textContent.includes('In Progress'));

  const rollbackTask = page.locator('[data-work-item-id="task-15"]');
  await rollbackTask.getByTitle('Sonraki kolona taşı').click();
  await page.getByText('Kolonun WIP limiti dolu; görev önceki konumuna alındı.', { exact: true }).waitFor();
  assert.match(await page.locator('[data-work-item-id="task-15"]').evaluate(element => element.closest('.column-lane').querySelector('.lane-title strong').textContent), /In Progress/);

  await page.locator('[data-work-item-id="task-03"] input[type="checkbox"]').click();
  await page.locator('[data-work-item-id="task-04"] input[type="checkbox"]').click();
  await page.getByRole('button', { name: 'Bana ata', exact: true }).click();
  await page.locator('.bulk-result[data-failed="1"]').waitFor();
  assert.match(await page.locator('.bulk-result').innerText(), /1 başarılı, 1 başarısız/);
  await page.waitForFunction(() => {
    const scope = window.angular.element(document.body).scope();
    return scope.vm.selectedIds().length === 1;
  });
  const selectedIds = await page.evaluate(() => window.angular.element(document.body).scope().vm.selectedIds());
  assert.deepEqual(selectedIds, ['task-04']);
  assert.equal(await page.locator('[data-work-item-id="task-04"] input[type="checkbox"]').isChecked(), true);

  await page.getByRole('tab', { name: 'Liste', exact: true }).click();
  await page.locator('.list-work-view .work-table').waitFor();
  assert.equal(await page.locator('.list-work-view tbody tr').count(), 48);
  await page.getByRole('button', { name: 'Sıkı liste', exact: true }).click();
  assert.equal(await page.locator('.list-work-view').getAttribute('data-density'), 'compact');
  await page.locator('.work-table th button').filter({ hasText: 'Öncelik' }).click();
  assert.match(await page.locator('.list-work-view tbody tr').first().innerText(), /High/);
  assert.match(await page.locator('.list-work-view tbody tr').last().innerText(), /Low/);
  await page.getByRole('button', { name: 'Liste kolonlarını yapılandır', exact: true }).click();
  await page.getByText('Efor', { exact: true }).last().click();
  assert.ok(await page.getByRole('columnheader', { name: 'Efor', exact: true }).isVisible());

  const firstRow = page.locator('.list-work-view tbody tr').first();
  await firstRow.getByTitle('Satırda düzenle').click();
  const titleInput = firstRow.getByLabel('Görev başlığı');
  await titleInput.fill('Inline güncellenen operasyon işi');
  await firstRow.getByTitle('Kaydet').click();
  await page.getByText('Inline güncellenen operasyon işi', { exact: true }).waitFor();
  await page.screenshot({ path: resolve(output, 'desktop-list-dense.png') });
  const desktopOverflow = await page.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(desktopOverflow.scrollWidth <= desktopOverflow.width + 1);
  assert.deepEqual(consoleErrors, []);
  await owner.context.close();

  const viewer = await createContext({ width: 1280, height: 900 }, 'Viewer');
  const viewerPage = await viewer.context.newPage();
  await viewerPage.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=board`, { waitUntil: 'networkidle' });
  await viewerPage.locator('.board-shell .task').first().waitFor();
  assert.equal(await viewerPage.locator('.task-select').count(), 0);
  assert.equal(await viewerPage.locator('.task-move-actions').count(), 0);
  assert.equal(await viewerPage.locator('.task').first().getAttribute('draggable'), 'false');
  await viewerPage.getByRole('tab', { name: 'Liste', exact: true }).click();
  assert.equal(await viewerPage.getByTitle('Satırda düzenle').count(), 0);
  await viewer.context.close();

  const mobile = await createContext({ width: 390, height: 844 });
  const mobilePage = await mobile.context.newPage();
  const mobileErrors = watchConsole(mobilePage);
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/projects/project-1`, { waitUntil: 'networkidle' });
  await mobilePage.getByRole('button', { name: 'Pano', exact: true }).click();
  await mobilePage.locator('.mobile-board-task').first().waitFor();
  assert.ok(await mobilePage.getByRole('button', { name: /sonraki kolona taşı/ }).first().isVisible());
  await mobilePage.getByRole('button', { name: /sonraki kolona taşı/ }).first().click();
  await mobilePage.waitForFunction(() => document.querySelectorAll('.mobile-board-lane')[1]?.textContent.includes('Kritik erişim akışını doğrula'));
  const mobileOverflow = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(mobileOverflow.scrollWidth <= mobileOverflow.width + 1);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-board-touch.png') });
  assert.deepEqual(mobileErrors, []);
  await mobile.context.close();
} finally {
  await browser.close();
  await server.close();
}

console.log('V3-UX-004 browser passed: large board/list, WIP rollback, keyboard/touch move, partial bulk, inline edit and Viewer controls.');
