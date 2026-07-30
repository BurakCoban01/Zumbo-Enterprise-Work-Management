import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import vmModule from 'node:vm';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const dashboardCore = require(resolve(root, 'shared/dashboard-core.js'));
const reportingCore = require(resolve(root, 'shared/reporting-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/reporting-views.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/dashboard.css'), 'utf8');
const overviewSource = await readFile(resolve(root, 'desktop-bulma/project-overview.js'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/reporting-views.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/reporting-views.css'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');

function response(overrides = {}) {
  return {
    id: 'dashboard-1',
    ownerUserId: 'owner-1',
    name: 'Teslimat görünümü',
    description: '',
    scope: 'Personal',
    projectIds: ['project-1'],
    widgets: [{
      id: 'summary',
      type: 'ProjectSummary',
      title: 'Proje özeti',
      column: 1,
      row: 1,
      width: 12,
      height: 2,
      projectId: null,
      filter: null
    }],
    filter: { rangeDays: 30, dueRiskDays: 30, statuses: [] },
    viewerUserIds: [],
    canEdit: true,
    archived: false,
    version: 3,
    ...overrides
  };
}

test('dashboard core normalizes layout, limits fanout inputs and preserves API ownership boundaries', () => {
  const draft = dashboardCore.create('project-1');
  assert.equal(dashboardCore.validate(draft), null);
  assert.equal(dashboardCore.addWidget(draft, 'StatusDistribution'), true);
  assert.equal(draft.widgets[1].row, 3);
  assert.notEqual(draft.widgets[0].id, draft.widgets[1].id);
  dashboardCore.moveWidget(draft, 1, -1);
  assert.deepEqual(draft.widgets.map(item => item.row), [1, 3]);
  assert.equal(dashboardCore.removeWidget(draft, draft.widgets[1].id), true);
  assert.equal(dashboardCore.removeWidget(draft, draft.widgets[0].id), false);

  draft.scope = 'Portfolio';
  assert.match(dashboardCore.validate(draft), /en az iki proje/);
  draft.projectIds.push('project-2');
  assert.equal(dashboardCore.validate(draft), null);
  const body = dashboardCore.payload(draft);
  assert.equal(body.viewerUserIds, undefined);
  assert.equal(body.canEdit, undefined);
  assert.deepEqual(body.projectIds, ['project-1', 'project-2']);

  while (draft.widgets.length < 12) dashboardCore.addWidget(draft, 'ProjectSummary');
  assert.equal(dashboardCore.addWidget(draft, 'ProjectSummary'), false);
});

function desktopFeature(api) {
  let provider;
  const module = {
    factory(name, factory) {
      assert.equal(name, 'desktopReportingViewsFeature');
      provider = factory;
      return module;
    }
  };
  const angular = { module: () => module };
  const window = {
    ZumboDashboardCore: dashboardCore,
    ZumboReportingCore: reportingCore,
    URL: { createObjectURL: () => 'blob:dashboard', revokeObjectURL() {} },
    document: { createElement: () => ({ click() {} }) },
    confirm: () => true
  };
  vmModule.runInNewContext(desktopSource, { angular, window, Date, Object, Array, String, Number });
  return provider({
    all: values => Promise.all(values),
    when: value => Promise.resolve(value)
  }, window, api);
}

test('desktop dashboard flow filters by project, validates portfolio scope and saves through the versioned API', async () => {
  const calls = [];
  const current = response();
  const rendered = {
    generatedAt: '2026-07-28T10:00:00Z',
    stale: false,
    partial: false,
    widgets: []
  };
  const api = {
    get(url) {
      calls.push(['get', url]);
      if (url.includes('?page=')) {
        return Promise.resolve({ items: [current, response({ id: 'other', projectIds: ['project-3'] })] });
      }
      if (url.endsWith('/render')) return Promise.resolve(rendered);
      if (url === '/api/dashboards/dashboard-1') return Promise.resolve(current);
      throw new Error(`Unexpected GET ${url}`);
    },
    post(url, body) {
      calls.push(['post', url, body]);
      return Promise.resolve(response({
        id: 'dashboard-2',
        scope: body.scope,
        projectIds: body.projectIds,
        widgets: body.widgets,
        filter: body.filter,
        name: body.name,
        version: 1
      }));
    },
    put(url, body) { calls.push(['put', url, body]); return Promise.resolve(current); },
    delete(url) { calls.push(['delete', url]); return Promise.resolve({ archived: true }); },
    download(url) { calls.push(['download', url]); return Promise.resolve({}); }
  };
  const feature = desktopFeature(api);
  const vm = {
    project: { id: 'project-1', name: 'Atlas Teslimat' },
    projects: [
      { id: 'project-1', name: 'Atlas Teslimat' },
      { id: 'project-2', name: 'Mobil Dönüşüm' }
    ],
    users: [{ id: 'viewer-1', displayName: 'Deniz Kaya' }],
    loadReports: () => Promise.resolve(),
    userName: id => id === 'viewer-1' ? 'Deniz Kaya' : id,
    workMode: 'dashboards',
    activeSection: 'reports'
  };
  feature.install(vm, {
    updateLocation() {},
    apiActionError(error, fallback) { return fallback; }
  });

  await vm.loadDashboards();
  assert.equal(vm.dashboards.length, 1);
  assert.equal(vm.dashboardDraft.id, 'dashboard-1');
  assert.equal(vm.dashboardRender, rendered);
  assert.equal(vm.dashboardProjectName('project-1'), 'Atlas Teslimat');

  vm.newDashboard();
  vm.dashboardDraft.scope = 'Portfolio';
  await vm.saveDashboard();
  assert.match(vm.dashboardError, /en az iki proje/);
  assert.equal(calls.some(call => call[0] === 'post'), false);

  vm.dashboardDraft.projectIds = ['project-1', 'project-2'];
  await vm.saveDashboard();
  const create = calls.find(call => call[0] === 'post');
  assert.equal(create[1], '/api/dashboards');
  assert.deepEqual(create[2].projectIds, ['project-1', 'project-2']);
});

test('desktop and mobile expose named, accessible, responsive dashboard workflows', () => {
  assert.match(overviewSource, /view\('dashboards'/);
  assert.match(desktopHtml, /Paylaşılacak kişiler/);
  assert.match(desktopHtml, /vm\.userName\(user\.id\)/);
  assert.match(desktopHtml, /scope="col"/);
  assert.doesNotMatch(desktopHtml, /Paylaşılacak kullanıcı kimlikleri/);
  assert.match(desktopCss, /\.dashboard-render-grid\s*\{[\s\S]*grid-template-columns:/);
  assert.match(desktopCss, /overflow-x:\s*auto/);

  assert.match(mobileSource, /\['workload', 'reports', 'dashboards'\]/);
  assert.match(mobileSource, /\/api\/dashboards/);
  assert.match(mobileHtml, /vm\.setMode\('dashboards'\)/);
  assert.match(mobileHtml, /mobile-dashboard-multiple/);
  assert.match(mobileHtml, /scope="col"/);
  assert.match(mobileCss, /min-height:\s*44px/);
  assert.match(mobileCss, /\.mobile-dashboard-table-wrap\s*\{[\s\S]*overflow-x:\s*auto/);
  assert.match(apiClientSource, /workflows\|automations\|dashboards/);
});
