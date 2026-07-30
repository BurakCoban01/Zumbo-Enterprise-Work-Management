import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { performance as nodePerformance } from 'node:perf_hooks';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const labelArgument = process.argv.find(argument => argument.startsWith('--label='));
const label = (labelArgument?.split('=', 2)[1] || 'observation').replace(/[^a-z0-9-]/gi, '-').toLowerCase();
const enforceBudgets = process.argv.includes('--assert');
const output = resolve(root, '../artifacts/performance/v3-harden-008');
await mkdir(output, { recursive: true });
await buildFrontend();

const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = Date.now();
const user = { id: 'user-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const teammate = { id: 'user-2', username: 'mert', email: 'mert@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const columns = [
  { id: 'todo', name: 'To Do', category: 'Todo', position: 0, wipLimit: null },
  { id: 'doing', name: 'In Progress', category: 'InProgress', position: 1, wipLimit: null },
  { id: 'review', name: 'Review', category: 'InProgress', position: 2, wipLimit: null },
  { id: 'done', name: 'Done', category: 'Done', position: 3, wipLimit: null }
];
const board = { id: 'board-1', projectId: 'project-1', name: 'Teslimat Panosu', type: 'Kanban', swimlaneMode: 'None', views: [], columns };
const project = {
  id: 'project-1', organizationId: 'org-1', key: 'OPS', name: 'Operasyon Teslimati', visibility: 'Private',
  members: [{ userId: user.id, role: 'ProjectOwner' }, { userId: teammate.id, role: 'Developer' }], teamIds: []
};
const tasks = Array.from({ length: 100 }, (_, index) => {
  const column = columns[index % columns.length];
  return {
    id: `task-${String(index + 1).padStart(3, '0')}`,
    projectId: project.id,
    boardId: board.id,
    columnId: column.id,
    title: `Olcekli operasyon isi ${String(index + 1).padStart(3, '0')}`,
    description: '',
    type: index % 7 === 0 ? 'Bug' : 'Task',
    status: column.name,
    priority: ['Critical', 'High', 'Medium', 'Low'][index % 4],
    assigneeUserId: index % 3 === 0 ? teammate.id : user.id,
    dueDate: new Date(now + ((index % 30) - 5) * 86400000).toISOString(),
    estimatePoints: [1, 2, 3, 5, 8][index % 5],
    labels: index % 5 === 0 ? ['capacity', 'delivery'] : [],
    relations: index % 19 === 0 ? [{ relatedWorkItemId: 'task-999', relationType: 'BlockedBy' }] : [],
    rank: (index + 1) * 1000,
    version: 1
  };
});
const workflow = {
  projectId: project.id,
  statuses: columns.map(column => ({ name: column.name, category: column.category })),
  transitions: columns.slice(0, -1).map((column, index) => ({ fromStatus: column.name, toStatus: columns[index + 1].name }))
};

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-harden-008' });
}

async function createContext(viewport) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const path = new URL(request.url()).pathname;
    let data;
    if (path === '/api/browser-auth/session') data = { user, csrfToken: 'csrf' };
    else if (path === '/api/projects') data = [project];
    else if (path === `/api/boards/by-project/${project.id}`) data = [board];
    else if (path === '/api/work-items/search') {
      const body = request.postData() ? request.postDataJSON() : {};
      const scoped = body.assigneeUserId ? tasks.filter(task => task.assigneeUserId === body.assigneeUserId) : tasks;
      const pageSize = Number(body.pageSize || scoped.length);
      data = { items: scoped.slice(0, pageSize), totalCount: scoped.length, degraded: false };
    } else if (path === `/api/work-items/reports/project-summary/${project.id}`) {
      data = { total: tasks.length, inProgress: 50, done: 25, overdue: 16 };
    } else if (path === `/api/work-items/reports/status-distribution/${project.id}`) {
      data = columns.map(column => ({ status: column.name, count: tasks.filter(task => task.columnId === column.id).length }));
    } else if (path === `/api/workflows/${project.id}`) data = workflow;
    else if (path === `/api/work-item-schemas/${project.id}`) data = { issueTypes: [], customFields: [], layouts: [] };
    else if (path === `/api/sprints/projects/${project.id}`) data = { items: [], totalCount: 0 };
    else if (path === `/api/sprints/projects/${project.id}/backlog`) data = { items: [], totalCount: 0 };
    else if (path === '/api/auth/users') data = [user, teammate];
    else if (path === '/api/teams') data = [];
    else if (path.startsWith('/api/notifications')) data = [];
    else data = [];
    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return context;
}

async function runtimeMetrics(page, selector, digestCount = 12) {
  await page.locator(selector).waitFor();
  await page.waitForTimeout(100);
  return page.evaluate(({ selector: target, digestCount: count }) => {
    const injector = window.angular.element(document.body).injector();
    const rootScope = injector.get('$rootScope');
    let watchers = 0;
    let scopeCount = 0;
    const visited = new Set();
    const visit = scope => {
      if (!scope || visited.has(scope.$id)) return;
      visited.add(scope.$id);
      scopeCount += 1;
      watchers += scope.$$watchers ? scope.$$watchers.length : 0;
      let child = scope.$$childHead;
      while (child) {
        visit(child);
        child = child.$$nextSibling;
      }
    };
    visit(rootScope);

    let sortCalls = 0;
    const originalSort = Array.prototype.sort;
    Array.prototype.sort = function(...arguments_) {
      sortCalls += 1;
      return originalSort.apply(this, arguments_);
    };
    const digestMs = [];
    try {
      for (let index = 0; index < count; index += 1) {
        const started = window.performance.now();
        rootScope.$digest();
        digestMs.push(window.performance.now() - started);
      }
    } finally {
      Array.prototype.sort = originalSort;
    }
    digestMs.sort((left, right) => left - right);
    const percentile = value => digestMs[Math.min(digestMs.length - 1, Math.ceil(digestMs.length * value) - 1)];
    return {
      watchers,
      scopes: scopeCount,
      domNodes: document.querySelectorAll('*').length,
      surfaceNodes: document.querySelector(target).querySelectorAll('*').length,
      digestCount: count,
      noOpSortCalls: sortCalls,
      noOpSortCallsPerDigest: sortCalls / count,
      digestMedianMs: Number(percentile(0.5).toFixed(3)),
      digestP95Ms: Number(percentile(0.95).toFixed(3))
    };
  }, { selector, digestCount });
}

async function measureDesktop() {
  const context = await createContext({ width: 1440, height: 1000 });
  const page = await context.newPage();
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=board`, { waitUntil: 'networkidle' });
  await page.locator('.board-shell .task').first().waitFor();
  assert.equal(await page.locator('.board-shell .task').count(), 100);
  const boardMetrics = await runtimeMetrics(page, '.board-shell');
  await page.screenshot({ path: resolve(output, `${label}-desktop-board.png`), fullPage: true });

  await page.getByRole('tab', { name: 'Liste', exact: true }).click();
  await page.locator('.list-work-view tbody tr').first().waitFor();
  assert.equal(await page.locator('.list-work-view tbody tr').count(), 100);
  const listMetrics = await runtimeMetrics(page, '.list-work-view');
  const interactionStarted = nodePerformance.now();
  await page.locator('.work-table th button').filter({ hasText: 'Öncelik' }).click();
  await page.evaluate(() => new Promise(resolveFrame => window.requestAnimationFrame(() => window.requestAnimationFrame(resolveFrame))));
  const sortInteractionMs = Number((nodePerformance.now() - interactionStarted).toFixed(3));
  await page.screenshot({ path: resolve(output, `${label}-desktop-list.png`), fullPage: true });
  await context.close();
  return { board: boardMetrics, list: { ...listMetrics, sortInteractionMs } };
}

async function measureMobile() {
  const context = await createContext({ width: 390, height: 844 });
  const page = await context.newPage();
  await page.goto(`${server.origin}/mobile-ionic/index.html#/app/dashboard`, { waitUntil: 'networkidle' });
  await page.locator('.task-row').first().waitFor();
  const assignedCount = await page.locator('.task-row').count();
  const expectedOpenCount = tasks
    .filter(task => task.assigneeUserId === user.id)
    .slice(0, 50)
    .filter(task => task.status !== 'Done')
    .length;
  assert.equal(assignedCount, expectedOpenCount);
  await page.getByRole('tab', { name: 'Tarihli', exact: true }).click();
  const dashboardMetrics = await runtimeMetrics(page, 'ion-view[view-title="Ana sayfa"] ion-content');
  await page.screenshot({ path: resolve(output, `${label}-mobile-dashboard.png`), fullPage: true });
  await context.close();
  return { dashboard: { ...dashboardMetrics, taskRows: assignedCount } };
}

let report;
try {
  report = {
    schemaVersion: 1,
    taskId: 'V3-HARDEN-008',
    label,
    syntheticTaskCount: tasks.length,
    generatedAtUtc: new Date().toISOString(),
    desktop: await measureDesktop(),
    mobile: await measureMobile(),
    budgets: {
      desktopListNoOpSortCallsPerDigestMax: 0,
      mobileDashboardNoOpSortCallsPerDigestMax: 0,
      desktopDigestP95MsMax: 50,
      mobileDigestP95MsMax: 50,
      desktopSortInteractionMsMax: 250
    }
  };

  if (enforceBudgets) {
    assert.equal(report.desktop.list.noOpSortCallsPerDigest, 0);
    assert.equal(report.mobile.dashboard.noOpSortCallsPerDigest, 0);
    assert.ok(report.desktop.board.digestP95Ms <= report.budgets.desktopDigestP95MsMax);
    assert.ok(report.desktop.list.digestP95Ms <= report.budgets.desktopDigestP95MsMax);
    assert.ok(report.mobile.dashboard.digestP95Ms <= report.budgets.mobileDigestP95MsMax);
    assert.ok(report.desktop.list.sortInteractionMs <= report.budgets.desktopSortInteractionMsMax);
  }
  await writeFile(resolve(output, `${label}.json`), `${JSON.stringify(report, null, 2)}\n`, 'utf8');
} finally {
  await browser.close();
  await server.close();
}

console.log(JSON.stringify(report, null, 2));
console.log(`V3-HARDEN-008 ${label} browser performance ${enforceBudgets ? 'gate' : 'observation'} passed.`);
