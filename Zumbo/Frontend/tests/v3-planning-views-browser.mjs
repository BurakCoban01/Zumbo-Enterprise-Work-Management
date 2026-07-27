import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-ux-007');
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

function fixture(role = 'ProjectOwner') {
  const project = {
    id: 'project-1', organizationId: 'org-1', key: 'PLAN', name: 'Teslimat Programı', visibility: 'Private',
    members: [{ userId: user.id, role }], teamIds: [], components: [], versions: [],
    milestones: [{ id: 'milestone-1', name: 'Pilot onayı', dueAt: '2026-07-27T21:30:00Z', status: 'Open' }],
    releases: [{ id: 'release-1', versionId: 'version-1', name: 'Sürüm 1.0', scheduledAt: '2026-07-30T09:00:00Z', status: 'Approved' }]
  };
  const sprints = Array.from({ length: 55 }, (_, index) => ({
    id: `sprint-${index + 1}`, projectId: project.id, name: `Sprint ${index + 1}`,
    goal: index === 0 ? 'Kritik bağımlılıkları kapat' : 'Teslimat kapsamı',
    startDate: index === 0 ? '2026-07-13' : `2026-${String(Math.floor(index / 4) % 12 + 1).padStart(2, '0')}-01`,
    endDate: index === 0 ? '2026-07-26' : `2026-${String(Math.floor(index / 4) % 12 + 1).padStart(2, '0')}-14`,
    status: index === 0 ? 'Active' : index < 4 ? 'Planned' : 'Completed',
    committedItems: 0, committedPoints: 0, completedItems: 0, completedPoints: 0,
    carryoverItems: 0, carryoverPoints: 0, version: 1
  }));
  const tasks = Array.from({ length: 205 }, (_, index) => {
    const noDue = index > 2 && index % 19 === 0;
    return {
      id: `task-${index}`, projectId: project.id, boardId: board.id, columnId: index % 5 ? 'todo' : 'doing',
      title: index === 0 ? 'Çakışmalı teslimat' : index === 1 ? 'Tarihi taşınacak iş' : index === 2 ? 'Bağımlı canlıya hazırlık' : `Plan kapsamı ${index}`,
      description: '', type: index % 7 === 0 ? 'Bug' : 'Task', priority: index < 3 ? 'High' : 'Medium',
      status: index % 5 ? 'To Do' : 'In Progress', assigneeUserId: user.id, teamId: null,
      dueDate: noDue ? null : `2026-07-${String(index % 20 + 10).padStart(2, '0')}T00:00:00Z`,
      sprintId: index % 3 === 0 ? 'sprint-1' : null, estimatePoints: 3, completedAt: null,
      statusHistory: [], labels: index === 1 ? ['release'] : [], checklist: [], comments: [], attachments: [], workLogs: [],
      relations: index === 0 ? [{ relatedWorkItemId: 'task-2', relationType: 'Blocks' }] : [],
      approvals: [], customFields: [], rank: index + 1, version: 3
    };
  });
  tasks[0].dueDate = '2026-07-25T00:00:00Z';
  tasks[1].dueDate = '2026-07-24T00:00:00Z';
  tasks[2].dueDate = '2026-07-24T00:00:00Z';
  return { project, sprints, tasks, ifMatches: [], conflictReturned: false };
}

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-007' });
}

function errorEnvelope(code, message) {
  return JSON.stringify({ success: false, data: null, error: { code, message }, correlationId: 'v3-ux-007' });
}

async function contextFor(viewport, role = 'ProjectOwner') {
  const data = fixture(role);
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, route => handleApi(route, data));
  return { context, data };
}

async function handleApi(route, data) {
  const request = route.request();
  if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
  const url = new URL(request.url());
  const path = url.pathname;
  const method = request.method();
  const body = request.postData() ? request.postDataJSON() : null;
  const workItem = path.match(/^\/api\/work-items\/(task-\d+)$/);
  if (workItem && method === 'PUT') {
    const item = data.tasks.find(candidate => candidate.id === workItem[1]);
    data.ifMatches.push(request.headers()['if-match'] || null);
    if (item.id === 'task-0' && !data.conflictReturned) {
      data.conflictReturned = true;
      return json(route, 409, errorEnvelope('CONCURRENCY_CONFLICT', 'Work item version changed.'));
    }
    Object.assign(item, body, { version: item.version + 1 });
    return json(route, 200, envelope({ ...item }));
  }
  if (workItem && method === 'GET') return json(route, 200, envelope(data.tasks.find(item => item.id === workItem[1])));

  let response;
  if (path === '/api/browser-auth/session') response = { user, csrfToken: 'csrf' };
  else if (path === '/api/projects' || path === '/api/projects/') response = [data.project];
  else if (path === '/api/projects/project-1') response = data.project;
  else if (path === '/api/boards/by-project/project-1') response = [board];
  else if (path === '/api/work-items/search' && method === 'POST') {
    const page = body.page || 1;
    const pageSize = body.pageSize || 100;
    response = { items: data.tasks.slice((page - 1) * pageSize, page * pageSize).map(item => ({ ...item })), totalCount: data.tasks.length, degraded: false };
  } else if (path === '/api/sprints/projects/project-1') {
    const offset = url.searchParams.get('after') === 'sprint-cursor-2' ? 50 : 0;
    response = { items: data.sprints.slice(offset, offset + 50), nextCursor: offset === 0 ? 'sprint-cursor-2' : null };
  } else if (path === '/api/sprints/projects/project-1/backlog') {
    const backlog = data.tasks.filter(item => !item.sprintId);
    response = { items: backlog.slice(0, 100), nextCursor: backlog.length > 100 ? 'backlog-2' : null };
  } else if (/^\/api\/sprints\/[^/]+\/burndown$/.test(path)) response = [];
  else if (path === '/api/work-items/reports/project-summary/project-1') response = { total: 205, inProgress: 41, done: 0, overdue: 0 };
  else if (path.startsWith('/api/work-items/reports/')) response = [];
  else if (path === '/api/workflows/project-1') response = { projectId: 'project-1', statuses: board.columns.map(column => ({ name: column.name, category: column.category })), transitions: [] };
  else if (path === '/api/work-item-schemas/project-1') response = { issueTypes: [{ key: 'Task', name: 'Görev', active: true }, { key: 'Bug', name: 'Hata', active: true }], customFields: [], layouts: [] };
  else if (path.startsWith('/api/audit/entity/')) response = [];
  else if (path === '/api/teams') response = [];
  else if (path === '/api/auth/users') response = [user];
  else if (path === '/api/notifications' || path === `/api/notifications/${user.id}`) response = [];
  else response = [];
  return json(route, 200, envelope(response));
}

function json(route, status, body) {
  return route.fulfill({ status, contentType: 'application/json', body });
}

function consoleErrors(page) {
  const errors = [];
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (/WebSocket connection .*\/hubs\/work-items|Failed to start the connection|status of 409/.test(text)) return;
    errors.push(text);
  });
  page.on('pageerror', error => errors.push(error.message));
  return errors;
}

try {
  const owner = await contextFor({ width: 1440, height: 1000 });
  const page = await owner.context.newPage();
  const errors = consoleErrors(page);
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=calendar&calendar=month&zoom=month&anchor=2026-07-23`, { waitUntil: 'networkidle' });
  await page.locator('.planning-surface-v3').waitFor();
  await page.getByText('Tüm proje kapsamı', { exact: true }).waitFor();
  assert.match(await page.locator('.planning-scope-line').innerText(), /205 iş, 55 sprint/);
  assert.ok(await page.getByText('Pilot onayı', { exact: true }).isVisible());
  assert.ok(await page.getByText('Sürüm 1.0', { exact: true }).isVisible());
  await page.screenshot({ path: resolve(output, 'desktop-calendar.png'), fullPage: true });

  await page.getByRole('button', { name: 'Liste', exact: true }).click();
  const conflictInput = page.getByLabel('Çakışmalı teslimat bitiş tarihi');
  await conflictInput.fill('2026-07-29');
  await page.locator('.planning-feedback-v3').filter({ hasText: 'başka bir kullanıcı tarafından değiştirildi' }).waitFor();
  assert.equal(owner.data.tasks[0].dueDate, '2026-07-25T00:00:00Z');
  const moveInput = page.getByLabel('Tarihi taşınacak iş bitiş tarihi');
  await moveInput.fill('2026-07-31');
  await page.getByText(/Bitiş tarihi .* olarak güncellendi/).waitFor();
  assert.equal(owner.data.tasks[1].dueDate, '2026-07-31T00:00:00.000Z');
  assert.ok(owner.data.ifMatches.includes('"3"'));

  await page.getByRole('tab', { name: 'Zaman çizelgesi', exact: true }).click();
  await page.locator('.planning-gantt-row').first().waitFor();
  assert.match(await page.locator('.planning-risk-note').innerText(), /1 bağımlılık · 1 tarih çakışması/);
  await page.screenshot({ path: resolve(output, 'desktop-timeline-gantt.png'), fullPage: true });
  await page.getByRole('button', { name: 'Tabloyu göster' }).click();
  assert.ok(await page.locator('.planning-table tbody tr').count() > 180);
  await page.screenshot({ path: resolve(output, 'desktop-timeline-table.png'), fullPage: true });

  await page.getByRole('tab', { name: 'Yol haritası', exact: true }).click();
  await page.getByText('Teslimat yol haritası', { exact: true }).waitFor();
  assert.ok(await page.getByText('Pilot onayı', { exact: true }).isVisible());
  assert.ok(await page.getByText('Sürüm 1.0', { exact: true }).isVisible());
  await page.getByRole('button', { name: /Grafi/ }).click();
  await page.locator('.planning-gantt-row').first().waitFor();
  await page.screenshot({ path: resolve(output, 'desktop-roadmap.png'), fullPage: true });
  const overflow = await page.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(overflow.scrollWidth <= overflow.width + 1);
  assert.deepEqual(errors, []);
  await owner.context.close();

  const viewer = await contextFor({ width: 1280, height: 900 }, 'Viewer');
  const viewerPage = await viewer.context.newPage();
  await viewerPage.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=calendar&anchor=2026-07-23`, { waitUntil: 'networkidle' });
  await viewerPage.getByText('Tüm proje kapsamı', { exact: true }).waitFor();
  assert.equal(await viewerPage.locator('.planning-surface-v3 input[type="date"]').count(), 0);
  assert.equal(await viewerPage.locator('.planning-surface-v3 [draggable="true"]').count(), 0);
  await viewer.context.close();

  const mobile = await contextFor({ width: 390, height: 844 });
  const mobilePage = await mobile.context.newPage();
  const mobileErrors = consoleErrors(mobilePage);
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/projects/project-1/plan?mode=calendar&anchor=2026-07-23`, { waitUntil: 'networkidle' });
  await mobilePage.locator('.mobile-plan-surface').waitFor();
  await mobilePage.getByText('Tüm proje kapsamı', { exact: false }).waitFor();
  assert.match(await mobilePage.locator('.mobile-plan-scope').innerText(), /205 iş, 55 sprint/);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-calendar.png'), fullPage: true });
  await mobilePage.getByRole('button', { name: 'Zaman', exact: true }).click();
  await mobilePage.getByText('İş zaman çizelgesi', { exact: true }).waitFor();
  assert.ok(await mobilePage.locator('.mobile-plan-row[data-risk="true"]').count() >= 1);
  await mobilePage.getByRole('button', { name: 'Yol haritası', exact: true }).click();
  await mobilePage.getByText('Teslimat yol haritası', { exact: true }).waitFor();
  assert.ok(await mobilePage.getByText('Pilot onayı', { exact: true }).isVisible());
  await mobilePage.screenshot({ path: resolve(output, 'mobile-roadmap.png'), fullPage: true });
  const mobileOverflow = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(mobileOverflow.scrollWidth <= mobileOverflow.width + 1);
  assert.deepEqual(mobileErrors, []);
  await mobile.context.close();
} finally {
  await browser.close();
  await server.close();
}

console.log('V3-UX-007 browser passed: complete pagination, calendar, Gantt/table, dependency risk, roadmap, conflict rollback, Viewer and mobile parity.');
