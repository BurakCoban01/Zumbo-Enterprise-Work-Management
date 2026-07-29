import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-feature-003');
const checks = [];
const failures = [];
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });

const owner = {
  id: 'owner-1',
  username: 'ada',
  displayName: 'Ada Yılmaz',
  email: 'ada@zumbo.local',
  organizationId: 'org-1',
  roles: ['User']
};
const users = [
  owner,
  {
    id: 'viewer-1',
    username: 'deniz',
    displayName: 'Deniz Kaya',
    email: 'deniz@zumbo.local',
    organizationId: 'org-1',
    roles: ['User']
  }
];
const projects = [
  project('project-1', 'ATL', 'Atlas Teslimat'),
  project('project-2', 'MOB', 'Mobil Dönüşüm')
];
const readyDashboard = dashboard('dashboard-ready', true, 'Portföy teslimat nabzı');
const degradedDashboard = dashboard('dashboard-degraded', false, 'Operasyon görünümü');

function project(id, key, name) {
  return {
    id,
    organizationId: 'org-1',
    key,
    name,
    visibility: 'Private',
    members: users.map((user, index) => ({
      userId: user.id,
      role: index === 0 ? 'ProjectOwner' : 'Viewer'
    })),
    teamIds: [],
    milestones: [],
    releases: [],
    components: [],
    versions: []
  };
}

function dashboard(id, canEdit, name) {
  return {
    id,
    ownerUserId: 'owner-1',
    name,
    description: 'Sentetik dashboard',
    scope: 'Portfolio',
    projectIds: projects.map(item => item.id),
    widgets: [{
      id: `${id}-summary`,
      type: 'ProjectSummary',
      title: 'Proje özeti',
      column: 1,
      row: 1,
      width: 12,
      height: 2,
      projectId: null,
      filter: null
    }, {
      id: `${id}-workload`,
      type: 'UserWorkload',
      title: 'İş yükü',
      column: 1,
      row: 3,
      width: 12,
      height: 2,
      projectId: 'project-1',
      filter: null
    }],
    filter: { rangeDays: 30, dueRiskDays: 30, statuses: [] },
    viewerUserIds: canEdit ? ['viewer-1'] : [],
    canEdit,
    archived: false,
    updatedAt: '2026-07-28T10:00:00Z',
    version: 3
  };
}

function renderReady() {
  return {
    dashboard: readyDashboard,
    widgets: [{
      id: 'dashboard-ready-summary',
      type: 'ProjectSummary',
      title: 'Proje özeti',
      status: 'Ready',
      errorCode: null,
      sources: projects.map((item, index) => ({
        projectId: item.id,
        data: { total: 14 + index, done: 8, inProgress: 4, overdue: 2 },
        columns: [
          { key: 'total', label: 'Toplam' },
          { key: 'done', label: 'Tamamlanan' },
          { key: 'inProgress', label: 'Devam eden' },
          { key: 'overdue', label: 'Geciken' }
        ],
        rows: [{ total: String(14 + index), done: '8', inProgress: '4', overdue: '2' }],
        generatedAt: '2026-07-28T10:00:00Z',
        sourceVersion: 42 + index,
        stale: false
      }))
    }, {
      id: 'dashboard-ready-workload',
      type: 'UserWorkload',
      title: 'İş yükü',
      status: 'Stale',
      errorCode: null,
      sources: [{
        projectId: 'project-1',
        data: [],
        columns: [
          { key: 'userId', label: 'Kullanıcı' },
          { key: 'openItems', label: 'Açık iş' }
        ],
        rows: [{ userId: 'viewer-1', openItems: '0' }],
        generatedAt: '2026-07-28T09:58:00Z',
        sourceVersion: 41,
        stale: true
      }]
    }],
    generatedAt: '2026-07-28T09:58:00Z',
    sourceVersions: [41, 42, 43],
    stale: true,
    partial: false,
    renderedAt: '2026-07-28T10:01:00Z'
  };
}

function renderDegraded() {
  return {
    dashboard: degradedDashboard,
    widgets: [{
      id: 'dashboard-degraded-summary',
      type: 'ProjectSummary',
      title: 'Proje özeti',
      status: 'Degraded',
      errorCode: 'DASHBOARD_WIDGET_SOURCE_UNAVAILABLE',
      sources: []
    }],
    generatedAt: null,
    sourceVersions: [],
    stale: false,
    partial: true,
    renderedAt: '2026-07-28T10:01:00Z'
  };
}

function envelope(data) {
  return JSON.stringify({
    success: true,
    data,
    error: null,
    correlationId: 'v3-feature-003'
  });
}

async function contextFor(viewport) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth));
    sessionStorage.setItem('zumbo.csrfToken', 'csrf');
  }, owner);
  await context.route(`${apiBaseUrl}/**`, route => handle(route));
  return context;
}

async function handle(route) {
  const request = route.request();
  if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
  const path = new URL(request.url()).pathname;
  if (path === '/api/browser-auth/session') return json(route, { user: owner, csrfToken: 'csrf' });
  if (path === '/api/projects' || path === '/api/projects/') return json(route, projects);
  if (path === '/api/projects/project-1') return json(route, projects[0]);
  if (path === '/api/boards/by-project/project-1') return json(route, []);
  if (path === '/api/dashboards' || path === '/api/dashboards/') {
    return json(route, { items: [readyDashboard, degradedDashboard], page: 1, pageSize: 100, total: 2 });
  }
  if (path === '/api/dashboards/dashboard-ready') return json(route, readyDashboard, { ETag: '"3"' });
  if (path === '/api/dashboards/dashboard-degraded') return json(route, degradedDashboard, { ETag: '"3"' });
  if (path === '/api/dashboards/dashboard-ready/render') return json(route, renderReady());
  if (path === '/api/dashboards/dashboard-degraded/render') return json(route, renderDegraded());
  if (path === '/api/auth/users') return json(route, users);
  if (path === '/api/teams' || path.startsWith('/api/notifications')) return json(route, []);
  if (path.startsWith('/api/work-items/reports/')) return json(route, []);
  if (path === '/api/work-items/search') return json(route, { items: [], totalCount: 0, degraded: false });
  if (path === '/api/sprints/projects/project-1') return json(route, { items: [], nextCursor: null });
  if (path === '/api/sprints/projects/project-1/backlog') return json(route, { items: [], nextCursor: null });
  if (path === '/api/workflows/project-1') return json(route, { projectId: 'project-1', statuses: [], transitions: [] });
  if (path === '/api/work-item-schemas/project-1') return json(route, { issueTypes: [], customFields: [], layouts: [] });
  return json(route, []);
}

function json(route, data, headers = {}) {
  return route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers,
    body: envelope(data)
  });
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const value = message.text();
    if (!/WebSocket|Failed to start|Failed to load resource/.test(value)) {
      failures.push(`${label}: ${value}`);
    }
  });
}

try {
  const desktop = await contextFor({ width: 1440, height: 1000 });
  const page = await desktop.newPage();
  diagnostics(page, 'desktop');
  await page.goto(
    `${server.origin}/desktop-bulma/index.html#section=reports&project=project-1&view=dashboards`,
    { waitUntil: 'networkidle' }
  );
  await page.getByRole('heading', { name: 'Dashboardlar', exact: true }).waitFor();
  await page.getByText('Portföy teslimat nabzı', { exact: true }).waitFor();
  assert.equal(await page.locator('.dashboard-widget').count(), 2);
  assert.equal(await page.locator('.dashboard-widget th[scope="col"]').count() > 0, true);
  assert.match(await page.locator('.dashboard-render-grid').innerText(), /Atlas Teslimat/);
  assert.match(await page.locator('.dashboard-render-grid').innerText(), /Mobil Dönüşüm/);
  assert.match(await page.locator('.dashboard-render-grid').innerText(), /Deniz Kaya/);
  assert.match(await page.locator('.dashboard-render-grid').innerText(), /\b0\b/);
  assert.doesNotMatch(await page.locator('.dashboard-render-grid').innerText(), /project-1/);
  checks.push('desktop-ready-stale-named-table');
  await page.getByRole('button', { name: /Operasyon görünümü/ }).click();
  await page.locator('.dashboard-degraded').waitFor();
  assert.ok(await page.locator('.dashboard-readonly').isVisible());
  checks.push('desktop-readonly-degraded');
  await page.screenshot({ path: resolve(output, 'desktop-degraded.png'), fullPage: true });
  await desktop.close();

  const mobile = await contextFor({ width: 390, height: 844 });
  const mobilePage = await mobile.newPage();
  diagnostics(mobilePage, 'mobile');
  await mobilePage.goto(
    `${server.origin}/mobile-ionic/index.html#/projects/project-1/insights?mode=dashboards&range=30`,
    { waitUntil: 'networkidle' }
  );
  await mobilePage.getByRole('tab', { name: 'Dashboardlar' }).waitFor();
  await mobilePage.getByText('Portföy teslimat nabzı', { exact: true }).waitFor();
  assert.equal(await mobilePage.locator('.mobile-dashboard-widget').count(), 2);
  assert.match(await mobilePage.locator('.mobile-dashboard').innerText(), /Deniz Kaya/);
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  checks.push('mobile-ready-table-no-page-overflow');
  await mobilePage.getByRole('button', { name: /Operasyon görünümü/ }).click();
  await mobilePage.locator('.mobile-dashboard-degraded').waitFor();
  assert.ok(await mobilePage.locator('.mobile-dashboard-readonly').isVisible());
  checks.push('mobile-readonly-degraded');
  await mobilePage.screenshot({ path: resolve(output, 'mobile-degraded.png'), fullPage: true });
  await mobile.close();

  assert.deepEqual(failures, []);
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-003',
    mode: 'deterministic-browser',
    passed: failures.length === 0 && checks.length === 4,
    viewports: ['1440x1000', '390x844'],
    checks,
    failures,
    noDeployment: true
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 4);
console.log('V3-FEATURE-003 browser passed: desktop/mobile ready, stale, read-only and degraded dashboard states.');
