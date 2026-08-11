import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import { URLSearchParams } from 'node:url';
import vmModule from 'node:vm';

const root = resolve(import.meta.dirname, '..');
const coreSource = await readFile(resolve(root, 'shared/planning-core.js'), 'utf8');
const desktopSource = await readFile(resolve(root, 'desktop-bulma/planning-views.js'), 'utf8');
const boardViewSource = await readFile(resolve(root, 'desktop-bulma/board-view.js'), 'utf8');
const planningSource = await readFile(resolve(root, 'desktop-bulma/planning.js'), 'utf8');
const workItemsSource = await readFile(resolve(root, 'desktop-bulma/work-items.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/planning-views.css'), 'utf8');
const mobileApp = await readFile(resolve(root, 'mobile-ionic/app.js'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/planning-views.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');

function planningCore() {
  const window = {};
  vmModule.runInNewContext(coreSource, { window, Date, Intl, Set, Array, Object, Number, String, Math });
  return window.ZumboPlanningCore;
}

const core = planningCore();

function task(id, overrides = {}) {
  return {
    id,
    title: `Plan işi ${id}`,
    description: '',
    type: 'Task',
    priority: 'Medium',
    status: 'To Do',
    assigneeUserId: 'user-1',
    teamId: 'team-1',
    dueDate: null,
    sprintId: null,
    completedAt: null,
    labels: [],
    relations: [],
    version: 3,
    ...overrides
  };
}

test('planning dates preserve date-only fields and convert instants in the selected timezone', () => {
  assert.equal(core.dateKey('2026-07-23', 'Pacific/Honolulu', true), '2026-07-23');
  assert.equal(core.dateKey('2026-07-23T22:30:00Z', 'Europe/Istanbul', false), '2026-07-24');
  assert.equal(core.dateKey('2026-07-23T22:30:00Z', 'America/New_York', false), '2026-07-23');

  const model = core.buildModel({
    anchorDate: '2026-07-24',
    timeZone: 'Europe/Istanbul',
    project: {
      milestones: [{ id: 'm1', name: 'Pilot', dueAt: '2026-07-23T22:30:00Z', status: 'Open' }],
      releases: [{ id: 'r1', name: '1.0', scheduledAt: '2026-07-23T22:30:00Z', status: 'Approved' }]
    }
  });
  assert.deepEqual(Array.from(model.calendarEvents, event => [event.kind, event.key]), [
    ['Sürüm', '2026-07-24'],
    ['Kilometre taşı', '2026-07-24']
  ]);
  assert.equal(model.roadmapRows.find(row => row.kind === 'Kilometre taşı').startKey, '2026-07-24');
  assert.equal(model.roadmapRows.find(row => row.kind === 'Sürüm').startKey, '2026-07-24');
});

test('timeline uses real due dates, labels sprint-derived ranges and exposes dependency conflicts', () => {
  const tasks = [
    task('foundation', {
      title: 'Temel teslimat',
      dueDate: '2026-07-20T00:00:00Z',
      sprintId: 'sprint-1',
      relations: [{ relatedWorkItemId: 'launch', relationType: 'Blocks' }]
    }),
    task('launch', { title: 'Canlıya hazırlık', dueDate: '2026-07-19T00:00:00Z', sprintId: 'sprint-1' }),
    task('date-only', { title: 'Tarihsiz backlog' })
  ];
  const model = core.buildModel({
    tasks,
    sprints: [{ id: 'sprint-1', name: 'Teslimat', startDate: '2026-07-10', endDate: '2026-07-24', status: 'Active' }],
    anchorDate: '2026-07-18',
    today: '2026-07-18',
    zoom: 'month'
  });
  const launch = model.timelineRows.find(row => row.id === 'launch');
  assert.equal(launch.startKey, '2026-07-10');
  assert.equal(launch.endKey, '2026-07-19');
  assert.equal(launch.source, 'Sprint başlangıcı → görev bitişi');
  assert.equal(launch.dependencyRisk, true);
  assert.deepEqual(Array.from(launch.blockedBy), ['foundation']);
  assert.deepEqual(Array.from(model.dependencyRisks, edge => edge.id), ['foundation>launch']);
  assert.equal(model.unscheduledTasks[0].id, 'date-only');
});

test('large project projection is complete, filterable and never capped by the view model', () => {
  const tasks = Array.from({ length: 1205 }, (_, index) => task(`task-${index}`, {
    title: index === 1204 ? 'Özel teslimat' : `Kapsam ${index}`,
    type: index % 2 ? 'Bug' : 'Task',
    dueDate: `2026-08-${String(index % 28 + 1).padStart(2, '0')}T00:00:00Z`
  }));
  const complete = core.buildModel({ tasks, anchorDate: '2026-08-14', today: '2026-08-01' });
  assert.equal(complete.totals.tasks, 1205);
  assert.equal(complete.timelineRows.length, 1205);
  assert.equal(complete.calendarEvents.filter(event => event.task).length, 1205);

  const filtered = core.buildModel({ tasks, filters: { query: 'özel', type: 'Task' }, anchorDate: '2026-08-14' });
  assert.equal(filtered.totals.tasks, 1);
  assert.equal(filtered.timelineRows[0].id, 'task-1204');
});

test('roadmap completion and exact segments follow workflow metadata instead of status names', () => {
  const workflow = {
    statuses: [
      { name: 'Hazır', category: 'Todo' },
      { name: 'İncelemede', category: 'InProgress' },
      { name: 'Yayında', category: 'Done' }
    ]
  };
  const tasks = [
    task('ready', { status: 'Hazır', sprintId: 'sprint-1' }),
    task('review-1', { status: 'İncelemede', sprintId: 'sprint-1' }),
    task('review-2', { status: 'İncelemede', sprintId: 'sprint-1' }),
    task('live', { status: 'Yayında', sprintId: 'sprint-1' })
  ];
  const model = core.buildModel({
    tasks,
    workflow,
    statusDistribution: [
      { status: 'Hazır', count: 3 },
      { status: 'İncelemede', count: 2 },
      { status: 'Yayında', count: 5 }
    ],
    sprints: [{ id: 'sprint-1', name: 'Dil bağımsız akış', startDate: '2026-08-01', endDate: '2026-08-14', status: 'Active' }],
    anchorDate: '2026-08-05'
  });
  assert.equal(model.totals.done, 1);
  assert.equal(model.totals.projectDone, 5);
  assert.equal(model.totals.projectProgress, 50);
  assert.deepEqual(Array.from(model.totals.projectSegments, item => [item.status, item.count, item.percentage]), [
    ['Hazır', 3, 30], ['İncelemede', 2, 20], ['Yayında', 5, 50]
  ]);
  const sprint = model.roadmapRows.find(row => row.id === 'sprint-sprint-1');
  assert.equal(sprint.progress, 25);
  assert.deepEqual(Array.from(sprint.segments, item => [item.status, item.count, item.percentage]), [
    ['Hazır', 1, 25], ['İncelemede', 2, 50], ['Yayında', 1, 25]
  ]);
});

test('task and bulk status changes are selected from workflow transitions', () => {
  assert.match(boardViewSource, /vm\.workflow && vm\.workflow\.transitions/);
  assert.doesNotMatch(boardViewSource, /status === 'To Do'|status === 'In Progress'|return 'Done'/);
  assert.match(desktopHtml, /vm\.bulkTransitionOptions\(\)/);
  assert.doesNotMatch(desktopHtml, /vm\.bulkMove\('In Progress'\)/);
  assert.match(planningSource, /vm\.reconcileStatusDistribution/);
  assert.match(workItemsSource, /reconcileStatusDistribution\(previousStatus, task\.status, previousDistribution\)/);
});

test('status segment rounding remains deterministic and totals exactly one hundred percent', () => {
  const segments = core.statusSegments([
    { status: 'A', count: 1 }, { status: 'B', count: 1 }, { status: 'C', count: 1 }
  ], { statuses: [] });
  assert.deepEqual(Array.from(segments, segment => segment.percentage), [33.34, 33.33, 33.33]);
  assert.equal(segments.reduce((sum, segment) => sum + segment.percentage, 0), 100);
});

function desktopFeature({ put } = {}) {
  let provider;
  const module = {
    factory(name, factory) {
      assert.equal(name, 'desktopPlanningViewsFeature');
      provider = factory;
      return module;
    },
    directive() { return module; }
  };
  const angular = {
    module: () => module,
    copy: value => JSON.parse(JSON.stringify(value)),
    extend: (...values) => Object.assign(...values)
  };
  vmModule.runInNewContext(desktopSource, { angular, Date, Intl, Set, Array, URLSearchParams, encodeURIComponent });
  const calls = [];
  const api = {
    remember: (path, value) => calls.push(['remember', path, value.version]),
    put: put || (() => Promise.resolve({})),
    get: () => Promise.resolve({ items: [], nextCursor: null })
  };
  const q = { when: value => Promise.resolve(value) };
  const window = { ZumboPlanningCore: core };
  return { feature: provider(q, window, api), calls };
}

test('desktop reschedule rolls back a stale optimistic date and reloads authoritative scope', async () => {
  const conflict = { data: { error: { code: 'CONCURRENCY_CONFLICT' } } };
  const { feature, calls } = desktopFeature({ put: () => Promise.reject(conflict) });
  const current = task('work-1', { dueDate: '2026-07-23T00:00:00Z' });
  const vm = {
    workMode: 'calendar', activeSection: 'board', project: { id: 'project-1', milestones: [], releases: [] },
    tasks: [current], sprints: [], hasMoreTasks: false, sprintNextCursor: null, pwa: { offline: false },
    rebuildAdvancedViews() {}, canEditWorkItems: () => true, projectTeams: () => [],
    notify() {}, loadTasks() { vm.reloads = (vm.reloads || 0) + 1; return Promise.resolve(); }
  };
  feature.install(vm, {
    storage: { getItem: () => null, setItem() {} },
    updateLocation() {},
    apiActionError: (_error, fallback) => fallback
  });
  const changed = await vm.reschedulePlanningTask(current, '2026-07-30');
  assert.equal(changed, false);
  assert.equal(current.dueDate, '2026-07-23T00:00:00Z');
  assert.equal(vm.reloads, 1);
  assert.match(vm.planningMutationMessage.text, /başka bir kullanıcı/);
  assert.deepEqual(calls[0], ['remember', '/api/work-items/work-1', 3]);
});

test('desktop and mobile surfaces expose scope, alternatives, zoom, filters and date mutation parity', () => {
  assert.match(desktopHtml, /class="planning-table"/);
  assert.match(desktopHtml, /caption class="sr-only">İş zaman çizelgesinin tablo görünümü/);
  assert.match(desktopHtml, /planning-drop-date="vm\.dropPlanningTask/);
  assert.match(desktopHtml, /vm\.planningScopeComplete/);
  assert.match(desktopCss, /grid-template-columns: repeat\(12,/);
  assert.match(desktopHtml, /roadmap-segmented-bar/);
  assert.match(desktopSource, /loadEveryTaskPage/);
  assert.match(desktopSource, /loadEverySprintPage/);
  assert.match(desktopSource, /zumbo\.planningViews/);

  assert.match(mobileApp, /state\('project-planning'/);
  assert.match(mobileHtml, /templates\/project-planning\.html/);
  assert.match(mobileHtml, /vm\.reschedule\(event\.task/);
  assert.match(mobileHtml, /vm\.model\.dependencyRisks\.length/);
  assert.match(mobileSource, /loadTaskPages\(page \+ 1/);
  assert.match(mobileSource, /loadSprintPages\(result\.nextCursor/);
  assert.match(mobileSource, /hasProjectPermission\(membership\.role, 'WorkItemUpdate'\)/);
});
