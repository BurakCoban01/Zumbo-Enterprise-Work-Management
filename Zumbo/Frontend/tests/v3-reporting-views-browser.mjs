import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-ux-008');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const user = { id: 'user-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const users = [user, { id: 'user-2', username: 'deniz', email: 'deniz@zumbo.local', organizationId: 'org-1', roles: ['User'] }];
const project = { id: 'project-1', organizationId: 'org-1', key: 'DEL', name: 'Teslimat Programı', visibility: 'Private', members: users.map((item, index) => ({ userId: item.id, role: index ? 'Developer' : 'ProjectOwner' })), teamIds: ['team-a', 'team-z'], milestones: [], releases: [], components: [], versions: [] };
const board = { id: 'board-1', projectId: project.id, name: 'Teslimat Panosu', type: 'Kanban', swimlaneMode: 'None', views: [], columns: [{ id: 'todo', name: 'To Do', category: 'Todo', position: 0 }, { id: 'doing', name: 'In Progress', category: 'InProgress', position: 1 }, { id: 'done', name: 'Done', category: 'Done', position: 2 }] };
const tasks = Array.from({ length: 205 }, (_, index) => ({
  id: `task-${index}`, projectId: project.id, boardId: board.id, columnId: index % 3 ? 'todo' : 'doing',
  title: index === 0 ? 'Kritik teslimat riski' : `Rapor kapsamı ${index}`, description: '', type: 'Task',
  priority: index < 3 ? 'High' : 'Medium', status: index % 3 ? 'To Do' : 'In Progress',
  assigneeUserId: index % 2 ? 'user-2' : 'user-1', teamId: index % 2 ? 'team-z' : 'team-a',
  dueDate: index < 5 ? '2026-07-25T00:00:00Z' : null, sprintId: null,
  estimatePoints: index % 5 ? 2 : null, completedAt: null, archived: false, labels: [], checklist: [], comments: [], attachments: [], workLogs: [], relations: [], approvals: [], customFields: [], rank: index + 1, version: 1
}));

function envelope(data) { return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-008' }); }
function reportHeaders() { return { 'Access-Control-Expose-Headers': 'X-Zumbo-Report-Generated-At, X-Zumbo-Report-Source-Version, X-Zumbo-Report-Stale, X-Zumbo-Report-Age-Seconds', 'X-Zumbo-Report-Generated-At': '2026-07-23T02:00:00Z', 'X-Zumbo-Report-Source-Version': '42', 'X-Zumbo-Report-Stale': 'false', 'X-Zumbo-Report-Age-Seconds': '14' }; }

async function contextFor(viewport) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await context.addInitScript(auth => { localStorage.setItem('zumbo.currentUser', JSON.stringify(auth)); sessionStorage.setItem('zumbo.csrfToken', 'csrf'); }, user);
  await context.route(`${apiBaseUrl}/**`, route => handle(route));
  return context;
}

async function handle(route) {
  const request = route.request();
  if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
  const url = new URL(request.url());
  const path = url.pathname;
  const body = request.postData() ? request.postDataJSON() : null;
  if (path === '/api/browser-auth/session') return json(route, { user, csrfToken: 'csrf' });
  if (path === '/api/projects' || path === '/api/projects/') return json(route, [project]);
  if (path === '/api/projects/project-1') return json(route, project);
  if (path === '/api/boards/by-project/project-1') return json(route, [board]);
  if (path === '/api/work-items/search' && request.method() === 'POST') {
    const page = body.page || 1; const size = body.pageSize || 100;
    return json(route, { items: tasks.slice((page - 1) * size, page * size), totalCount: tasks.length, degraded: false });
  }
  if (path === '/api/work-items/reports/project-summary/project-1') return report(route, { total: 205, done: 0, inProgress: 69, overdue: 5 });
  if (path === '/api/work-items/reports/status-distribution/project-1') return report(route, [{ status: 'In Progress', count: 69 }, { status: 'To Do', count: 136 }]);
  if (path === '/api/work-items/reports/user-workload/project-1') return report(route, [{ userId: 'user-1', openItems: 103, overdueItems: 3, loggedHours: 28.5 }, { userId: 'user-2', openItems: 102, overdueItems: 2, loggedHours: 24 }]);
  if (path === '/api/work-items/reports/due-date-risks/project-1') return report(route, [{ id: 'task-0', title: 'Kritik teslimat riski', assigneeUserId: 'user-1', dueDate: '2026-07-25T00:00:00Z', status: 'In Progress' }]);
  if (path === '/api/work-items/reports/flow-time/project-1') return report(route, { from: '2026-06-24', to: '2026-07-23', completedItems: 18, cycleTimeSampleSize: 15, averageLeadTimeHours: 52, medianLeadTimeHours: 48, averageCycleTimeHours: 31, medianCycleTimeHours: 28 });
  if (path === '/api/work-items/reports/completion-rate/project-1') return report(route, { from: '2026-06-24', to: '2026-07-23', createdItems: 24, completedItems: 18, completionRatePercent: 75 });
  if (path === '/api/work-items/reports/team-performance/project-1') return report(route, [{ teamId: 'team-z', teamName: 'Zeta', assignedItems: 12, completedItems: 8, completionRatePercent: 66.67, averageLeadTimeHours: 54, loggedHours: 24 }, { teamId: 'team-a', teamName: 'Alfa', assignedItems: 12, completedItems: 10, completionRatePercent: 83.33, averageLeadTimeHours: 43, loggedHours: 28.5 }]);
  if (path === '/api/work-items/reports/sprint-velocity/project-1') return report(route, []);
  if (path === '/api/sprints/projects/project-1') return json(route, { items: [], nextCursor: null });
  if (path === '/api/sprints/projects/project-1/backlog') return json(route, { items: tasks.slice(0, 100), nextCursor: 'more' });
  if (path === '/api/workflows/project-1') return json(route, { projectId: project.id, statuses: board.columns.map(column => ({ name: column.name, category: column.category })), transitions: [] });
  if (path === '/api/work-item-schemas/project-1') return json(route, { issueTypes: [], customFields: [], layouts: [] });
  if (path === '/api/auth/users') return json(route, users);
  if (path === '/api/teams') return json(route, [{ id: 'team-a', name: 'Alfa' }, { id: 'team-z', name: 'Zeta' }]);
  if (path.startsWith('/api/audit/entity/') || path.startsWith('/api/notifications')) return json(route, []);
  return json(route, []);
}

function json(route, data) { return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) }); }
function report(route, data) { return route.fulfill({ status: 200, contentType: 'application/json', headers: reportHeaders(), body: envelope(data) }); }
function errorsFor(page) { const errors = []; page.on('pageerror', error => errors.push(error.message)); page.on('console', message => { if (message.type() === 'error' && !/WebSocket|Failed to start/.test(message.text())) errors.push(message.text()); }); return errors; }

try {
  const desktop = await contextFor({ width: 1440, height: 1000 });
  const page = await desktop.newPage();
  const desktopErrors = errorsFor(page);
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=reports&project=project-1&view=workload&range=30`, { waitUntil: 'networkidle' });
  await page.locator('.reporting-surface').waitFor();
  await page.getByText('Kapasite eşiği yapılandırılmadı', { exact: false }).waitFor();
  await page.getByText(/14 sn yaşında/).waitFor();
  assert.equal(await page.locator('.workload-table tbody tr').count(), 2);
  assert.match(await page.locator('.reporting-freshness').innerText(), /14 sn yaşında/);
  await page.getByRole('button', { name: 'İşleri aç' }).first().click();
  assert.equal(await page.locator('.reporting-drilldown .reporting-risk-list button').count(), 50);
  assert.ok(await page.getByRole('button', { name: 'Daha fazla iş göster' }).isVisible());
  await page.screenshot({ path: resolve(output, 'desktop-workload.png'), fullPage: true });
  await page.getByRole('tab', { name: 'Raporlar', exact: true }).click();
  await page.getByText('Akış ve tamamlama', { exact: true }).waitFor();
  assert.ok(await page.getByText('%75', { exact: true }).isVisible());
  const teamTable = await page.locator('.reporting-table').last().innerText();
  assert.ok(teamTable.indexOf('Alfa') < teamTable.indexOf('Zeta'));
  await page.getByRole('button', { name: 'Tabloyu göster' }).click();
  await page.screenshot({ path: resolve(output, 'desktop-reports.png'), fullPage: true });
  assert.deepEqual(desktopErrors, []);
  await desktop.close();

  const mobile = await contextFor({ width: 390, height: 844 });
  const mobilePage = await mobile.newPage();
  const mobileErrors = errorsFor(mobilePage);
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/projects/project-1/insights?mode=workload&range=30`, { waitUntil: 'networkidle' });
  await mobilePage.locator('.mobile-report-surface').waitFor();
  await mobilePage.getByText('Kapasite eşiği yapılandırılmadı.', { exact: true }).waitFor();
  await mobilePage.locator('.mobile-workload-row').first().click();
  assert.equal(await mobilePage.locator('.mobile-report-row').count(), 20);
  assert.ok(await mobilePage.getByRole('button', { name: 'Daha fazla iş göster' }).isVisible());
  await mobilePage.screenshot({ path: resolve(output, 'mobile-workload.png'), fullPage: true });
  await mobilePage.getByRole('tab', { name: 'Raporlar' }).click();
  await mobilePage.getByText('Ekip teslimat özeti', { exact: true }).waitFor();
  const overflow = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(overflow.scrollWidth <= overflow.width + 1);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-reports.png'), fullPage: true });
  assert.deepEqual(mobileErrors, []);
  await mobile.close();
  console.log('V3-UX-008 browser passed: complete workload scope, freshness, no invented capacity/ranking, drill-down, report table and mobile parity.');
} finally {
  await browser.close();
  await server.close();
}
