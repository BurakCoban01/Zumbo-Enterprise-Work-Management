import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-ux-005');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const user = { id: 'user-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const board = {
  id: 'board-1', projectId: 'project-1', name: 'Teslimat Panosu', type: 'Scrum', swimlaneMode: 'None', views: [],
  columns: [
    { id: 'todo', name: 'To Do', category: 'Todo', position: 0, wipLimit: null },
    { id: 'doing', name: 'In Progress', category: 'InProgress', position: 1, wipLimit: null },
    { id: 'done', name: 'Done', category: 'Done', position: 2, wipLimit: null }
  ]
};

function createFixture(role = 'ProjectOwner') {
  const project = {
    id: 'project-1', organizationId: 'org-1', key: 'PLN', name: 'Planlama Operasyonu', visibility: 'Private',
    members: [{ userId: user.id, role }], teamIds: []
  };
  const sprints = [
    sprint('sprint-plan', 'Temmuz teslimatı', 'Kritik yetki akışını tamamla', 'Planned', '2026-07-20', '2026-08-02'),
    sprint('sprint-next', 'Ağustos hazırlığı', 'Kalan işleri güvenle devral', 'Planned', '2026-08-03', '2026-08-16'),
    { ...sprint('sprint-done', 'Haziran teslimatı', 'Önceki teslimat', 'Completed', '2026-06-15', '2026-06-28'), committedItems: 12, committedPoints: 34, completedItems: 10, completedPoints: 29 }
  ];
  const tasks = [];
  for (let index = 1; index <= 12; index += 1) {
    tasks.push(workItem(`sprint-${index}`, `Sprint kapsam işi ${index}`, index % 2 ? 3 : 5, 'sprint-plan', index));
  }
  for (let index = 1; index <= 110; index += 1) {
    const title = index === 1 ? 'Çakışmalı kapsam işi' : index === 2 ? 'Klavye ile planlanacak iş' : `Backlog hazırlık işi ${index}`;
    tasks.push(workItem(`backlog-${String(index).padStart(3, '0')}`, title, [1, 2, 3, 5, 8][index % 5], null, index + 20));
  }
  return { project, sprints, tasks, ifMatchHeaders: [], created: 0 };
}

function sprint(id, name, goal, status, startDate, endDate) {
  return {
    id, projectId: 'project-1', name, goal, startDate, endDate, status,
    committedItems: 0, committedPoints: 0, completedItems: 0, completedPoints: 0,
    carryoverItems: 0, carryoverPoints: 0, version: 1
  };
}

function workItem(id, title, estimatePoints, sprintId, rank) {
  return {
    id, projectId: 'project-1', boardId: board.id, columnId: 'todo', title, description: '', type: 'Task',
    priority: rank % 3 === 0 ? 'High' : 'Medium', status: 'To Do', assigneeUserId: user.id,
    dueDate: null, sprintId, estimatePoints, completedAt: null, statusHistory: [], labels: [], checklist: [],
    comments: [], attachments: [], workLogs: [], relations: [], approvals: [], customFields: [], rank, version: 3
  };
}

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-005' });
}

function errorEnvelope(code, message) {
  return JSON.stringify({ success: false, data: null, error: { code, message }, correlationId: 'v3-ux-005' });
}

async function createContext(viewport, role = 'ProjectOwner') {
  const fixture = createFixture(role);
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, route => handleApi(route, fixture));
  return { context, fixture };
}

async function handleApi(route, fixture) {
  const request = route.request();
  if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
  const url = new URL(request.url());
  const path = url.pathname;
  const method = request.method();
  const body = request.postData() ? request.postDataJSON() : null;
  const itemMutation = path.match(/^\/api\/sprints\/([^/]+)\/items\/([^/]+)$/);
  const sprintAction = path.match(/^\/api\/sprints\/([^/]+)\/(start|complete)$/);
  const burndown = path.match(/^\/api\/sprints\/([^/]+)\/burndown$/);

  if (itemMutation && (method === 'PUT' || method === 'DELETE')) {
    const [, sprintId, workItemId] = itemMutation;
    const item = fixture.tasks.find(candidate => candidate.id === workItemId);
    fixture.ifMatchHeaders.push(request.headers()['if-match'] || null);
    if (workItemId === 'backlog-001') {
      return json(route, 409, errorEnvelope('CONCURRENCY_CONFLICT', 'Work item version changed.'));
    }
    if (method === 'PUT') item.sprintId = sprintId;
    else item.sprintId = null;
    item.version += 1;
    return json(route, 200, envelope({ workItemId, sprintId: item.sprintId, estimatePoints: item.estimatePoints, version: item.version }));
  }
  if (sprintAction && method === 'POST') {
    const sprintItem = fixture.sprints.find(candidate => candidate.id === sprintAction[1]);
    if (sprintAction[2] === 'start') {
      if (fixture.sprints.some(candidate => candidate.status === 'Active' && candidate.id !== sprintItem.id)) {
        return json(route, 409, errorEnvelope('SPRINT_ACTIVE_EXISTS', 'Only one sprint can be active.'));
      }
      const scope = fixture.tasks.filter(item => item.sprintId === sprintItem.id);
      sprintItem.status = 'Active';
      sprintItem.committedItems = scope.length;
      sprintItem.committedPoints = points(scope);
    } else {
      const carryover = body.carryoverSprintId || null;
      const open = fixture.tasks.filter(item => item.sprintId === sprintItem.id && !item.completedAt);
      open.forEach(item => { item.sprintId = carryover; });
      sprintItem.status = 'Completed';
      sprintItem.carryoverItems = carryover ? open.length : 0;
      sprintItem.carryoverPoints = carryover ? points(open) : 0;
    }
    sprintItem.version += 1;
    return json(route, 200, envelope(sprintItem));
  }
  if (path === '/api/sprints' && method === 'POST') {
    fixture.created += 1;
    const created = sprint(`sprint-created-${fixture.created}`, body.name, body.goal || '', 'Planned', body.startDate, body.endDate);
    fixture.sprints.push(created);
    return json(route, 201, envelope(created));
  }
  if (burndown && method === 'GET') {
    const selected = fixture.sprints.find(candidate => candidate.id === burndown[1]);
    const values = selected.status === 'Planned' ? [] : [
      { date: selected.startDate, remainingPoints: selected.committedPoints, remainingItems: selected.committedItems },
      { date: '2026-07-23', remainingPoints: Math.max(0, selected.committedPoints - 8), remainingItems: Math.max(0, selected.committedItems - 2) }
    ];
    return json(route, 200, envelope(values));
  }

  let data;
  if (path === '/api/browser-auth/session') data = { user, csrfToken: 'csrf' };
  else if (path === '/api/projects') data = [fixture.project];
  else if (path === '/api/boards/by-project/project-1') data = [board];
  else if (path === '/api/work-items/search' && method === 'POST') {
    const page = body.page || 1;
    const pageSize = body.pageSize || 100;
    data = { items: fixture.tasks.slice((page - 1) * pageSize, page * pageSize), totalCount: fixture.tasks.length, degraded: false };
  } else if (path === '/api/sprints/projects/project-1') {
    data = { items: fixture.sprints, nextCursor: null };
  } else if (path === '/api/sprints/projects/project-1/backlog') {
    const backlog = fixture.tasks.filter(item => !item.sprintId);
    const offset = url.searchParams.get('after') === 'backlog-page-2' ? 100 : 0;
    data = { items: backlog.slice(offset, offset + 100).map(item => ({ ...item })), nextCursor: offset === 0 && backlog.length > 100 ? 'backlog-page-2' : null };
  } else if (path === '/api/work-items/reports/project-summary/project-1') data = { total: fixture.tasks.length, inProgress: 0, done: 0, overdue: 0 };
  else if (path === '/api/work-items/reports/sprint-velocity/project-1') data = [
    { sprintId: 'old-1', completedItems: 8, completedPoints: 29 },
    { sprintId: 'old-2', completedItems: 9, completedPoints: 31 },
    { sprintId: 'old-3', completedItems: 10, completedPoints: 34 }
  ];
  else if (path.startsWith('/api/work-items/reports/')) data = [];
  else if (path === '/api/workflows/project-1') data = { projectId: 'project-1', statuses: board.columns.map(column => ({ name: column.name, category: column.category })), transitions: [] };
  else if (path === '/api/work-item-schemas/project-1') data = { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
  else if (path.startsWith('/api/audit/entity/')) data = [];
  else if (path === '/api/teams') data = [];
  else if (path === '/api/auth/users') data = [user];
  else if (path === '/api/notifications' || path === `/api/notifications/${user.id}`) data = [];
  else data = [];
  return json(route, 200, envelope(data));
}

function json(route, status, body) {
  return route.fulfill({ status, contentType: 'application/json', body });
}

function points(items) {
  return items.reduce((total, item) => total + Number(item.estimatePoints || 0), 0);
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
  const errors = watchConsole(page);
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=backlog`, { waitUntil: 'networkidle' });
  await page.locator('.planning-workspace').waitFor();
  assert.equal(await page.locator('.planning-column').first().locator('.planning-list article').count(), 100);
  assert.match(await page.locator('.capacity-line').first().innerText(), /Son 3 sprint ortalaması/);
  await page.screenshot({ path: resolve(output, 'desktop-backlog-large.png') });

  const keyboardItem = page.getByText('Klavye ile planlanacak iş', { exact: true }).locator('..').locator('..');
  await keyboardItem.focus();
  await keyboardItem.press('Alt+ArrowRight');
  await page.waitForFunction(() => !document.body.textContent.includes('Klavye ile planlanacak iş') || document.querySelector('.sprint-scope')?.textContent.includes('Klavye ile planlanacak iş'));
  assert.ok(owner.fixture.ifMatchHeaders.includes('"3"'));

  await page.getByLabel('Çakışmalı kapsam işi işini sprint kapsamına al').click();
  await page.getByRole('alert').filter({ hasText: 'Çakışma algılandı' }).waitFor();
  assert.ok(await page.getByText('Çakışmalı kapsam işi', { exact: true }).isVisible());
  await page.screenshot({ path: resolve(output, 'desktop-conflict-recovery.png') });

  await page.getByRole('button', { name: 'Daha fazla backlog işi yükle' }).click();
  await page.waitForFunction(() => document.querySelectorAll('.planning-column')[0]?.querySelectorAll('.planning-list article').length > 100);
  assert.equal(await page.locator('.planning-column').first().locator('.planning-list article').count(), 109);

  await page.getByRole('tab', { name: 'Sprint', exact: true }).click();
  await page.locator('.sprint-view').waitFor();
  const formInputs = page.locator('.sprint-create input');
  await formInputs.nth(0).fill('Eylül hazırlığı');
  await formInputs.nth(1).fill('Bir sonraki hedefi doğrula');
  await formInputs.nth(2).fill('2026-08-17');
  await formInputs.nth(3).fill('2026-08-30');
  await page.getByRole('button', { name: 'Sprint oluştur' }).click();
  await page.waitForFunction(() => document.body.textContent.includes('Eylül hazırlığı'));
  assert.equal(owner.fixture.created, 1);
  await page.getByLabel('Sprint seç').selectOption({ label: 'Temmuz teslimatı · Planned' });

  await page.getByRole('button', { name: "Sprint'i başlat" }).click();
  await page.getByText('Active', { exact: true }).first().waitFor();
  await page.locator('.burndown-bars').waitFor();
  await page.screenshot({ path: resolve(output, 'desktop-sprint-active.png') });
  await page.getByLabel('Devreden iş hedefi').selectOption('sprint-next');
  await page.getByRole('button', { name: "Sprint'i tamamla" }).click();
  await page.getByText('Completed', { exact: true }).first().waitFor();
  assert.ok(owner.fixture.tasks.filter(item => item.sprintId === 'sprint-next').length >= 12);
  assert.deepEqual(errors, []);
  const desktopOverflow = await page.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(desktopOverflow.scrollWidth <= desktopOverflow.width + 1);
  await owner.context.close();

  const viewer = await createContext({ width: 1280, height: 900 }, 'Viewer');
  const viewerPage = await viewer.context.newPage();
  await viewerPage.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=backlog`, { waitUntil: 'networkidle' });
  await viewerPage.locator('.planning-workspace').waitFor();
  assert.equal(await viewerPage.locator('.planning-move').count(), 0);
  await viewerPage.getByRole('tab', { name: 'Sprint', exact: true }).click();
  assert.equal(await viewerPage.locator('.sprint-create').count(), 0);
  await viewer.context.close();

  const mobile = await createContext({ width: 390, height: 844 });
  const mobilePage = await mobile.context.newPage();
  const mobileErrors = watchConsole(mobilePage);
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/projects/project-1`, { waitUntil: 'networkidle' });
  await mobilePage.getByRole('button', { name: 'Backlog', exact: true }).click();
  await mobilePage.locator('.mobile-planning-row').first().waitFor();
  assert.ok(await mobilePage.getByRole('button', { name: /Klavye ile planlanacak iş.*sprint kapsamına al/ }).isVisible());
  await mobilePage.getByRole('button', { name: /Klavye ile planlanacak iş.*sprint kapsamına al/ }).click();
  await mobilePage.getByText('İş sprint kapsamına alındı.', { exact: true }).waitFor();
  await mobilePage.waitForFunction(() => {
    const scope = window.angular.element(document.querySelector('.mobile-planning')).scope();
    return scope && scope.vm && !scope.vm.planningBusy;
  });
  await mobilePage.screenshot({ path: resolve(output, 'mobile-backlog-touch.png') });
  await mobilePage.getByRole('tab', { name: 'Sprint', exact: true }).click();
  await mobilePage.locator('.mobile-sprint-summary').waitFor();
  await mobilePage.locator('.mobile-sprint-actions button').click();
  await mobilePage.locator('.popup-buttons button').last().click();
  await mobilePage.waitForTimeout(500);
  assert.equal(mobile.fixture.sprints[0].status, 'Active');
  await mobilePage.locator('.mobile-sprint-actions button').waitFor();
  assert.match(await mobilePage.locator('.mobile-sprint-actions').innerText(), /Tamamla/);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-sprint-active.png') });
  const mobileOverflow = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(mobileOverflow.scrollWidth <= mobileOverflow.width + 1);
  assert.deepEqual(mobileErrors, []);
  await mobile.context.close();
} finally {
  await browser.close();
  await server.close();
}

console.log('V3-UX-005 browser passed: large backlog, If-Match conflict rollback, lifecycle, carryover, Viewer and mobile touch planning.');
