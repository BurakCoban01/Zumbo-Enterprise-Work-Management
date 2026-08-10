import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import vmModule from 'node:vm';

const root = resolve(import.meta.dirname, '..');
const planningSource = await readFile(resolve(root, 'desktop-bulma/planning.js'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/styles.css'), 'utf8');
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileTasks = await readFile(resolve(root, 'mobile-ionic/tasks.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');

const angular = {
  copy: value => JSON.parse(JSON.stringify(value)),
  extend: (...values) => Object.assign(...values)
};
const q = {
  all: values => Promise.all(values),
  when: value => Promise.resolve(value),
  reject: value => Promise.reject(value)
};

function loadFactory(source, name, dependencies) {
  let provider;
  const module = {
    factory(actualName, factory) {
      assert.equal(actualName, name);
      provider = factory;
      return module;
    }
  };
  vmModule.runInNewContext(source, {
    angular: { ...angular, module: () => module }, Date, Set, encodeURIComponent
  });
  return provider(...dependencies);
}

function model(apiOverrides = {}) {
  const task = {
    id: 'work-1', title: 'Yetki akışını tamamla', status: 'To Do', priority: 'High',
    estimatePoints: 8, sprintId: null, version: 4
  };
  const sprint = {
    id: 'sprint-1', name: 'Temmuz teslimatı', goal: 'Kritik akışı tamamla', status: 'Planned',
    committedItems: 0, committedPoints: 0, completedItems: 0, completedPoints: 0,
    carryoverItems: 0, carryoverPoints: 0
  };
  const calls = [];
  const api = {
    get: path => {
      calls.push(['get', path]);
      return Promise.resolve([]);
    },
    put: (path, body) => {
      calls.push(['put', path, body]);
      return Promise.resolve({ workItemId: task.id, sprintId: sprint.id, estimatePoints: 8, version: 5 });
    },
    delete: path => {
      calls.push(['delete', path]);
      return Promise.resolve({ workItemId: task.id, sprintId: null, estimatePoints: 8, version: 5 });
    },
    post: (path, body) => {
      calls.push(['post', path, body]);
      return Promise.resolve({});
    },
    remember: (path, value) => calls.push(['remember', path, value.version]),
    ...apiOverrides
  };
  const vm = {
    project: { id: 'project-1' }, projectMembership: { role: 'ProjectOwner' },
    tasks: [task], backlogItems: [task], sprints: [sprint], velocity: [
      { completedPoints: 8 }, { completedPoints: 13 }, { completedPoints: 13 }
    ],
    selectedPlanningSprintId: sprint.id,
    clearSelection() {}, refreshBoardModel() {}, loadMoreTasks() { return Promise.resolve(); },
    loadTasks() { vm.reloads = (vm.reloads || 0) + 1; return Promise.resolve(); },
    notify(kind, message) { vm.notice = { kind, message }; }
  };
  const feature = loadFactory(planningSource, 'desktopPlanningFeature', [q, api]);
  feature.install(vm, (error, fallback) => error?.data?.error?.message || fallback);
  vm.selectPlanningSprint();
  return { vm, task, sprint, api, calls };
}

test('planning capacity uses the last completed velocity points and current planned scope', () => {
  const { vm, task } = model();
  task.sprintId = 'sprint-1';
  vm.selectPlanningSprint();
  assert.equal(vm.planningPoints(), 8);
  assert.equal(vm.capacityBaseline(), 34 / 3);
  assert.equal(vm.capacityPercent(), 71);
  assert.equal(vm.capacityState(), 'available');
  vm.tasks.push({ id: 'work-2', estimatePoints: 8, sprintId: 'sprint-1' });
  vm.selectPlanningSprint();
  assert.equal(vm.capacityState(), 'over');
});

test('status distribution reconciliation applies a stale delta once and accepts an already fresh report', () => {
  const { vm } = model();
  const before = [{ status: 'To Do', count: 3 }];
  vm.statusDistribution = angular.copy(before);
  vm.reconcileStatusDistribution('To Do', 'Doğrulama', before);
  assert.deepEqual(vm.statusDistribution.map(item => [item.status, item.count]), [['To Do', 2], ['Doğrulama', 1]]);

  vm.reconcileStatusDistribution('To Do', 'Doğrulama', before);
  assert.deepEqual(vm.statusDistribution.map(item => [item.status, item.count]), [['To Do', 2], ['Doğrulama', 1]]);
});

test('plan conflict rolls optimistic scope back and reloads authoritative state', async () => {
  const conflict = { data: { error: { code: 'CONCURRENCY_CONFLICT', message: 'Version mismatch.' } } };
  const { vm, task, calls } = model({ put: () => Promise.reject(conflict) });
  const pending = vm.planBacklogItem(task);
  assert.equal(vm.backlogItems.length, 0);
  assert.equal(task.sprintId, 'sprint-1');
  assert.equal(await pending, false);
  assert.equal(vm.backlogItems.length, 1);
  assert.equal(task.sprintId, null);
  assert.equal(vm.reloads, 1);
  assert.match(vm.planningError, /Çakışma/);
  assert.ok(calls.some(call => call[0] === 'remember' && call[1] === '/api/work-items/work-1' && call[2] === 4));
});

test('keyboard alternative and cursor continuation preserve explicit planning controls', async () => {
  const { vm, task, calls } = model();
  let prevented = false;
  await vm.handlePlanningItemKey({ altKey: true, key: 'ArrowRight', preventDefault() { prevented = true; } }, task, 'plan');
  assert.equal(prevented, true);
  assert.ok(calls.some(call => call[0] === 'put' && call[1] === '/api/sprints/sprint-1/items/work-1'));

  vm.backlogNextCursor = 'cursor 2';
  vm.backlogItems = [];
  vm.planningLoading = false;
  await vm.loadMoreBacklog();
  assert.ok(calls.some(call => call[0] === 'get' && call[1].includes('after=cursor%202')));
});

test('shared client maps sprint scope mutations to work-item If-Match ownership', () => {
  assert.match(apiClientSource, /sprintItem = url\.match\(\/\^\\\/api\\\/sprints/);
  assert.match(apiClientSource, /return \{ kind: 'work-items', id: sprintItem\[1\] \}/);
  assert.match(planningSource, /apiClient\.remember\('\/api\/work-items\/' \+ item\.id, item\)/);
});

test('desktop planning surface exposes lifecycle, capacity, burndown and complete states', () => {
  for (const binding of [
    'vm.capacityPercent()', 'vm.loadMoreBacklog()', 'vm.loadRemainingPlanningTasks()',
    'vm.startSelectedSprint()', 'vm.completeSelectedSprint()', 'vm.burndownWidth(point)'
  ]) assert.ok(desktopHtml.includes(binding), `${binding} is missing`);
  assert.match(desktopHtml, /ng-keydown="vm\.handlePlanningItemKey/);
  assert.match(desktopHtml, /role="alert" aria-live="assertive"/);
  assert.match(desktopCss, /\.planning-workspace \{ display: grid; grid-template-columns:/);
  assert.match(desktopCss, /@media \(max-width: 760px\)[\s\S]+\.planning-workspace \{ grid-template-columns: 1fr; \}/);
});

test('mobile surface provides permission-aware essential sprint lifecycle parity', () => {
  for (const method of ['createSprint', 'planSprintItem', 'unplanSprintItem', 'startSprint', 'completeSprint', 'sprintBurndown']) {
    assert.match(mobileApi, new RegExp(`${method}: function`));
  }
  for (const method of ['openCreateSprint', 'planBacklogItem', 'unplanSprintItem', 'startSprint', 'completeSprint']) {
    assert.match(mobileTasks, new RegExp(`vm\\.${method} = function`));
  }
  assert.match(mobileTasks, /membership\.role !== 'Viewer'/);
  assert.match(mobileTasks, /zumboApi\.workflow\(projectId\)/);
  assert.match(mobileHtml, /templates\/create-sprint\.html/);
  assert.match(mobileHtml, /vm\.canEditTasks\(\) && vm\.selectedSprint\(\)\.status === 'Planned'/);
  assert.match(mobileHtml, /vm\.statusOptions\(\)/);
  assert.doesNotMatch(mobileHtml, /vm\.filter\('To Do'\)|vm\.filter\('In Progress'\)|vm\.filter\('Done'\)/);
  assert.match(mobileHtml, /vm\.carryoverTargets\(\)/);
});
