import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-ux-003');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = Date.now();
const user = { id: 'user-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const teammate = { id: 'user-2', username: 'mert', email: 'mert@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const project = {
  id: 'project-1', organizationId: 'org-1', key: 'PLT', name: 'Platform Teslimatı', visibility: 'Private',
  members: [{ userId: user.id, role: 'ProjectOwner' }, { userId: teammate.id, role: 'Developer' }], teamIds: ['team-1'],
  milestones: [{ id: 'milestone-1', name: 'Pilot çıkışı', dueAt: new Date(now + 864000000).toISOString(), status: 'Open' }],
  releases: [{ id: 'release-1', versionId: 'version-1', name: 'Sürüm 1.4', status: 'Approved', scheduledAt: new Date(now + 1209600000).toISOString() }]
};
const emptyProject = {
  id: 'project-2', organizationId: 'org-1', key: 'OPS', name: 'Operasyon Hazırlığı', visibility: 'Internal',
  members: [{ userId: user.id, role: 'Viewer' }], teamIds: [], milestones: [], releases: []
};
const board = { id: 'board-1', projectId: project.id, name: 'Teslimat Panosu', type: 'Kanban', swimlaneMode: 'None', views: [], columns: [{ id: 'todo', name: 'To Do', category: 'Todo', position: 0 }] };
const sprint = { id: 'sprint-1', projectId: project.id, name: 'Sprint 14', goal: 'Pilot akışını güvenle aç', status: 'Active', startDate: new Date(now - 604800000).toISOString(), endDate: new Date(now + 604800000).toISOString() };
const tasks = [
  task('task-1', 'Kritik erişim akışını doğrula', 'High'),
  task('task-2', 'Yayın notlarını tamamla', 'Medium')
];
const audit = [{ id: 'audit-1', action: 'ProjectUpdated', entityType: 'Project', entityId: project.id, actorUserId: teammate.id, createdAt: new Date(now - 3600000).toISOString() }];

function task(id, title, priority) {
  return {
    id, title, projectId: project.id, boardId: board.id, columnId: 'todo', type: 'Task', status: 'In Progress', priority,
    assigneeUserId: teammate.id, dueDate: new Date(now + 172800000).toISOString(), description: '', labels: [], checklist: [], comments: [],
    attachments: [], workLogs: [], relations: [], approvals: [], customFields: [], statusHistory: [], rank: 1000, version: 1
  };
}

function envelope(data) { return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-003' }); }
function response(url, method, requestBody) {
  const path = url.pathname;
  if (path === '/api/browser-auth/session') return { user, csrfToken: 'csrf' };
  if (path === '/api/projects') return [project, emptyProject];
  if (path === '/api/boards/by-project/project-1') return url.searchParams.get('archived') === 'true' ? [] : [board];
  if (path === '/api/boards/by-project/project-2') return [];
  if (path === '/api/work-items/search' && method === 'POST') {
    return requestBody && requestBody.projectId === emptyProject.id
      ? { items: [], totalCount: 0, degraded: false }
      : { items: tasks, totalCount: tasks.length, degraded: false };
  }
  if (path === '/api/work-items/reports/project-summary/project-1') return { total: 12, inProgress: 5, done: 5, overdue: 2 };
  if (path === '/api/work-items/reports/project-summary/project-2') return { total: 0, inProgress: 0, done: 0, overdue: 0 };
  if (path === '/api/work-items/reports/status-distribution/project-1') return [{ status: 'In Progress', count: 5 }];
  if (path === '/api/work-items/reports/user-workload/project-1') return [{ userId: teammate.id, openItems: 4, loggedHours: 12 }];
  if (path === '/api/work-items/reports/due-date-risks/project-1') return [{ id: tasks[0].id, title: tasks[0].title, dueDate: tasks[0].dueDate, status: tasks[0].status }];
  if (path === '/api/work-items/reports/sprint-velocity/project-1') return [{ sprintId: sprint.id, completedPoints: 8, completedItems: 3 }];
  if (path.startsWith('/api/work-items/reports/')) return [];
  if (path === '/api/sprints/projects/project-1') return { items: [sprint], totalCount: 1 };
  if (path === '/api/sprints/projects/project-1/backlog') return { items: [], totalCount: 0 };
  if (path.startsWith('/api/sprints/projects/project-2')) return { items: [], totalCount: 0 };
  if (path === '/api/sprints/sprint-1/burndown') return [];
  if (path === '/api/audit/entity/Project/project-2') return [];
  if (path.startsWith('/api/audit/entity/')) return audit;
  if (path === '/api/teams') return [{ id: 'team-1', name: 'Platform Ekibi', members: [] }];
  if (path === '/api/auth/users') return [user, teammate];
  if (path === '/api/workflows/project-1' || path === '/api/workflows/project-2') return { projectId: path.split('/').at(-1), statuses: [], transitions: [] };
  if (path.startsWith('/api/work-item-schemas/')) return { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
  if (path === '/api/notifications' || path === `/api/notifications/${user.id}`) return [];
  return [];
}

async function createContext(viewport) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(route.request().url());
    const requestBody = route.request().postData() ? route.request().postDataJSON() : null;
    await route.fulfill({ status: 200, contentType: 'application/json', body: envelope(response(url, route.request().method(), requestBody)) });
  });
  return context;
}

try {
  const desktop = await createContext({ width: 1440, height: 1000 });
  const page = await desktop.newPage();
  const consoleErrors = [];
  page.on('console', message => {
    if (message.type() !== 'error') return;
    var text = message.text();
    if (/WebSocket connection .*\/hubs\/work-items|Failed to start the connection/.test(text)) return;
    consoleErrors.push(text);
  });
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=overview`, { waitUntil: 'networkidle' });
  await page.locator('.project-overview h2').getByText(project.name, { exact: true }).waitFor();
  assert.equal(await page.getByRole('tab').count(), 11);
  assert.ok(await page.getByText('Takip gerekli', { exact: true }).isVisible());
  assert.ok(await page.getByText('Pilot çıkışı', { exact: true }).isVisible());
  assert.ok(await page.getByText('Sürüm 1.4', { exact: true }).isVisible());
  await page.getByText(teammate.username, { exact: true }).first().waitFor();
  await page.screenshot({ path: resolve(output, 'desktop-overview.png'), fullPage: true });

  await page.getByRole('tab', { name: 'İş yükü', exact: true }).click();
  await page.getByText('4 açık / 12 sa', { exact: true }).waitFor();
  assert.match(page.url(), /section=reports/);
  assert.match(page.url(), /view=workload/);
  await page.screenshot({ path: resolve(output, 'desktop-workload.png'), fullPage: true });

  await page.getByRole('tab', { name: 'Pano', exact: true }).click();
  await page.getByLabel('Arama').fill('kritik');
  await page.getByLabel('Öncelik').selectOption('High');
  await page.waitForFunction(() => location.hash.includes('query=kritik') && location.hash.includes('priority=High'));
  await page.reload({ waitUntil: 'networkidle' });
  assert.equal(await page.getByLabel('Arama').inputValue(), 'kritik');
  assert.equal(await page.getByLabel('Öncelik').inputValue(), 'High');

  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=${emptyProject.id}&view=board`, { waitUntil: 'networkidle' });
  await page.locator('.project-overview h2').getByText(emptyProject.name, { exact: true }).waitFor();
  await page.locator('.overview-metrics').waitFor();
  assert.equal(await page.getByRole('tab').count(), 3);
  assert.equal(await page.getByRole('tab', { name: 'Genel bakış', exact: true }).getAttribute('aria-selected'), 'true');
  assert.equal(await page.getByText('Pano yüklenmedi', { exact: true }).count(), 0);
  const desktopSize = await page.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(desktopSize.scrollWidth <= desktopSize.width + 1);
  assert.deepEqual(consoleErrors, []);
  await desktop.close();

  const mobile = await createContext({ width: 390, height: 844 });
  const mobilePage = await mobile.newPage();
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/projects/${project.id}`, { waitUntil: 'networkidle' });
  await mobilePage.locator('.mobile-project-overview h2').getByText(project.name, { exact: true }).waitFor();
  assert.ok(await mobilePage.getByText('Pilot çıkışı', { exact: true }).isVisible());
  assert.ok(await mobilePage.getByText('Sürüm 1.4', { exact: true }).isVisible());
  const mobileSize = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(mobileSize.scrollWidth <= mobileSize.width + 1);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-overview.png'), fullPage: true });
  await mobilePage.getByRole('button', { name: 'Sprint', exact: true }).click();
  await mobilePage.getByRole('tab', { name: 'Sprint', exact: true }).waitFor();
  assert.equal(await mobilePage.getByRole('tab', { name: 'Sprint', exact: true }).getAttribute('aria-selected'), 'true');
  await mobile.close();
} finally {
  await browser.close();
  await server.close();
}

console.log('V3-UX-003 browser passed: overview, capability-aware switcher, deep-link filters, no-board fallback and mobile handoff.');
