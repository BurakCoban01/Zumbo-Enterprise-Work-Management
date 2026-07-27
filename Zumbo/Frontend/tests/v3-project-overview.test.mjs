import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import { URLSearchParams } from 'node:url';
import vmModule from 'node:vm';

const root = resolve(import.meta.dirname, '..');
const source = await readFile(resolve(root, 'desktop-bulma/project-overview.js'), 'utf8');
const appSource = await readFile(resolve(root, 'desktop-bulma/app.js'), 'utf8');
const managementSource = await readFile(resolve(root, 'desktop-bulma/management.js'), 'utf8');
const planningSource = await readFile(resolve(root, 'desktop-bulma/planning.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileDetails = await readFile(resolve(root, 'mobile-ionic/details.js'), 'utf8');
const mobileTasks = await readFile(resolve(root, 'mobile-ionic/tasks.js'), 'utf8');

function createFeature() {
  let provider;
  const module = {
    factory(name, factory) {
      assert.equal(name, 'desktopProjectOverviewFeature');
      provider = factory;
      return module;
    }
  };
  vmModule.runInNewContext(source, {
    angular: { module: () => module },
    window: { document: { querySelector: () => null } }
  });
  return provider(callback => callback());
}

function viewModel({ board = true, role = 'Viewer' } = {}) {
  const ownerId = 'opaque-owner-id';
  return {
    project: {
      id: 'project-1',
      key: 'WEB',
      name: 'Web Platform',
      members: [{ userId: ownerId, role: 'ProjectOwner' }, { userId: 'user-1', role }],
      milestones: [
        { id: 'later', name: 'Beta', dueAt: '2026-09-10T00:00:00Z', status: 'Open' },
        { id: 'next', name: 'Pilot', dueAt: '2026-08-01T00:00:00Z', status: 'Open' }
      ],
      releases: [
        { id: 'draft', name: '1.2', status: 'Draft', scheduledAt: null },
        { id: 'scheduled', name: '1.1', status: 'Approved', scheduledAt: '2026-08-12T00:00:00Z' }
      ]
    },
    projectMembership: { userId: 'user-1', role },
    board: board ? { id: 'board-1' } : null,
    session: { currentUser: { id: 'user-1', username: 'viewer' } },
    users: [],
    sprints: [{ id: 'sprint-1', name: 'Sprint 14', status: 'Active' }],
    summary: { total: 8, inProgress: 3, overdue: 0 },
    dueDateRisks: [],
    timelineEntries: [],
    clearSelection() {},
    rebuildAdvancedViews() {},
    loadTimeline() { return Promise.resolve([]); },
    refreshBoardModel() {},
    loadTasks() { return Promise.resolve(); }
  };
}

test('project view availability is membership-aware and removes board dead ends', () => {
  const feature = createFeature();
  const vm = viewModel();
  const locations = [];
  feature.install(vm, { updateLocation: (...args) => locations.push(args) });
  assert.deepEqual(Array.from(vm.availableProjectViews(), item => item.id), [
    'overview', 'board', 'list', 'backlog', 'sprint', 'calendar', 'timeline', 'roadmap', 'catalog', 'intake', 'automation', 'jobs', 'workload', 'reports'
  ]);
  assert.equal(vm.setProjectView('workload').section, 'reports');
  assert.equal(vm.activeSection, 'reports');
  assert.equal(locations.at(-1)[0], 'reports');

  const noBoard = viewModel({ board: false });
  feature.install(noBoard, { updateLocation() {} });
  assert.deepEqual(Array.from(noBoard.availableProjectViews(), item => item.id), ['overview', 'catalog', 'intake', 'automation', 'jobs', 'workload', 'reports']);
  assert.equal(noBoard.setProjectView('board').id, 'overview');

  const denied = viewModel();
  denied.projectMembership = null;
  feature.install(denied, { updateLocation() {} });
  assert.equal(denied.setProjectView('reports'), null);
  assert.equal(denied.activeSection, 'projects');
});

test('deep links preserve supported filter context and normalize invalid targets', () => {
  const feature = createFeature();
  const vm = viewModel();
  feature.install(vm, { updateLocation() {} });
  const selected = vm.applyProjectViewLocation(new URLSearchParams('section=board&view=list&query=kritik&priority=High'));
  assert.equal(selected.id, 'list');
  assert.equal(vm.search, 'kritik');
  assert.equal(vm.priorityFilter, 'High');

  const fallback = vm.applyProjectViewLocation(new URLSearchParams('section=board&view=unknown&priority=Impossible'));
  assert.equal(fallback.id, 'overview');
  assert.equal(vm.priorityFilter, '');
});

test('overview derives health, delivery and safe member labels from authoritative project data', () => {
  const feature = createFeature();
  const vm = viewModel();
  feature.install(vm, { updateLocation() {} });
  assert.equal(vm.projectHealth().label, 'Plan üzerinde');
  assert.equal(vm.nextProjectMilestone().id, 'next');
  assert.equal(vm.nextProjectRelease().id, 'scheduled');
  assert.equal(vm.projectOwnerName(), 'Proje üyesi');
  assert.doesNotMatch(vm.projectOwnerName(), /opaque-owner-id/);
  vm.summary.overdue = 2;
  assert.deepEqual(JSON.parse(JSON.stringify(vm.projectHealth())), {
    level: 'danger', label: 'Takip gerekli', detail: '2 geciken iş bulunuyor.'
  });
});

test('desktop and mobile templates expose unified views, overview states and mobile parity handoff', () => {
  for (const mode of ['overview', 'board', 'list', 'backlog', 'sprint', 'calendar', 'timeline', 'roadmap', 'catalog', 'automation', 'jobs', 'workload', 'reports']) {
    assert.match(source, new RegExp(`view\\('${mode}'`));
  }
  assert.match(desktopHtml, /class="project-view-switcher"/);
  assert.match(desktopHtml, /class="project-overview"/);
  assert.match(desktopHtml, /!vm\.board && vm\.activeSection === 'board' && \['overview','catalog','automation','jobs'\]\.indexOf\(vm\.workMode\) < 0/);
  assert.match(desktopHtml, /vm\.nextProjectMilestone\(\)/);
  assert.match(desktopHtml, /vm\.nextProjectRelease\(\)/);
  assert.match(desktopHtml, /vm\.timelineError/);
  assert.match(appSource, /params\.set\('view', vm\.workMode/);
  assert.match(appSource, /params\.set\('query', vm\.search\)/);
  assert.match(appSource, /vm\.applyProjectViewLocation\(params\)/);
  assert.match(managementSource, /vm\.summary = \{\};[\s\S]+if \(!vm\.board\) return vm\.loadTasks\(\)/);
  assert.match(planningSource, /if \(vm\.workMode === 'timeline'\) \{[\s\S]+entityAudit\('Sprint'/);
  assert.match(mobileHtml, /class="mobile-project-overview"/);
  assert.match(mobileHtml, /vm\.openProjectWork\('backlog'\)/);
  assert.match(mobileHtml, /vm\.openProjectWork\('jobs'\)/);
  assert.match(mobileDetails, /zumboApi\.summary\(vm\.project\.id\)/);
  assert.match(mobileDetails, /sessionStore\.state\.taskMode = mode/);
  assert.match(mobileTasks, /sessionStore\.state\.taskMode \|\| 'my'/);
});
