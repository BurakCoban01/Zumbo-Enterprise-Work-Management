import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import vmModule from 'node:vm';

const root = resolve(import.meta.dirname, '..');
const excellenceSource = await readFile(resolve(root, 'desktop-bulma/board-excellence.js'), 'utf8');
const boardViewSource = await readFile(resolve(root, 'desktop-bulma/board-view.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileApp = await readFile(resolve(root, 'mobile-ionic/app.js'), 'utf8');
const mobileAuth = await readFile(resolve(root, 'mobile-ionic/auth.js'), 'utf8');
const mobileTasks = await readFile(resolve(root, 'mobile-ionic/tasks.js'), 'utf8');

const angular = {
  copy: value => JSON.parse(JSON.stringify(value)),
  extend: (...values) => Object.assign(...values)
};
const q = { when: value => Promise.resolve(value) };

function loadFactory(source, name, dependencies) {
  let provider;
  const module = {
    factory(actualName, factory) {
      assert.equal(actualName, name);
      provider = factory;
      return module;
    }
  };
  vmModule.runInNewContext(source, { angular: { ...angular, module: () => module }, Date });
  return provider(...dependencies);
}

function storage(initial = {}) {
  const values = new Map(Object.entries(initial));
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    value: key => values.get(key)
  };
}

function model({ role = 'Developer', api = {} } = {}) {
  const tasks = [
    task('task-low', 'Zulu işi', 'Low', 'todo', 'To Do', 20),
    task('task-high', 'Alfa işi', 'High', 'doing', 'In Progress', 10)
  ];
  const vm = {
    board: { id: 'board-1', columns: [column('todo', 'To Do'), column('doing', 'In Progress'), column('review', 'Review')] },
    projectMembership: { role },
    session: { currentUser: { id: 'user-1' } },
    tasks,
    selectedTaskIds: {},
    pendingTaskIds: {},
    priorityFilter: '',
    projectRoleHasPermission(roleName, permission) {
      const roles = [
        { name: 'Developer', permissions: ['WorkItemUpdate'] },
        { name: 'Viewer', permissions: ['WorkItemView'] }
      ];
      return roles.some(item => item.name === roleName && item.permissions.includes(permission));
    },
    userName: id => id === 'user-1' ? 'Ada' : 'Mert',
    moveTaskToColumn: (id, target) => Promise.resolve({ id, target }),
    dropTaskBefore(id, anchor, placement) { vm.dropCall = { id, anchor, placement }; return Promise.resolve(); },
    refreshBoardModel() {},
    loadTasks() { vm.reloads = (vm.reloads || 0) + 1; return Promise.resolve(); },
    selectedIds() { return Object.keys(vm.selectedTaskIds).filter(id => vm.selectedTaskIds[id]); },
    taskTitle(id) { return vm.tasks.find(item => item.id === id)?.title || id; },
    notify(kind, message) { vm.notice = { kind, message }; }
  };
  const client = {
    remember: api.remember || (() => {}),
    put: api.put || ((_path, body) => Promise.resolve({ ...body, version: 2 })),
    post: api.post || (() => Promise.resolve({ results: [], succeeded: 0, failed: 0 }))
  };
  return { vm, client };
}

function task(id, title, priority, columnId, status, rank) {
  return {
    id, title, priority, columnId, status, rank, description: '', dueDate: null,
    assigneeUserId: id === 'task-high' ? 'user-1' : 'user-2', relations: [], labels: [], estimatePoints: 3, version: 1
  };
}

function column(id, name, wipLimit = null) {
  return { id, name, position: 0, wipLimit };
}

function installExcellence(options = {}) {
  const context = model(options);
  const feature = loadFactory(excellenceSource, 'desktopBoardExcellenceFeature', [q, context.client]);
  const localStorage = options.storage || storage();
  feature.install(context.vm, {
    storage: localStorage,
    apiActionError: (error, fallback) => error?.data?.error?.code === 'CONCURRENCY_CONFLICT' ? 'Çakışma; güncel veri yüklendi.' : fallback
  });
  return { ...context, localStorage };
}

test('list preferences persist density, columns and stable domain sorting', () => {
  const localStorage = storage({
    'zumbo.listPreferences': JSON.stringify({ density: 'compact', columns: { estimate: true } })
  });
  const { vm } = installExcellence({ storage: localStorage });
  assert.equal(vm.listPreferences.density, 'compact');
  assert.equal(vm.listColumnVisible('estimate'), true);
  assert.equal(vm.listColumnVisible('status'), true);
  vm.sortListBy('priority');
  assert.deepEqual(Array.from(vm.visibleListTasks(), item => item.id), ['task-high', 'task-low']);
  vm.setListDensity('comfortable');
  vm.toggleListColumn('assignee');
  const persisted = JSON.parse(localStorage.value('zumbo.listPreferences'));
  assert.equal(persisted.density, 'comfortable');
  assert.equal(persisted.columns.assignee, false);
});

test('list projection is rebuilt on explicit model changes instead of every digest read', () => {
  const { vm } = installExcellence();
  const initial = vm.visibleListTasks();
  assert.strictEqual(vm.visibleListTasks(), initial);
  vm.sortListBy('priority');
  assert.notStrictEqual(vm.visibleListTasks(), initial);
  assert.deepEqual(Array.from(vm.visibleListTasks(), item => item.id), ['task-high', 'task-low']);
  const sorted = vm.visibleListTasks();
  vm.tasks[0].priority = 'Critical';
  vm.refreshBoardModel();
  assert.notStrictEqual(vm.visibleListTasks(), sorted);
  assert.deepEqual(Array.from(vm.visibleListTasks(), item => item.id), ['task-low', 'task-high']);
});

test('inline edit is optimistic and restores the authoritative snapshot on conflict', async () => {
  let remembered;
  const api = {
    remember: (path, value) => { remembered = { path, version: value.version }; },
    put: () => Promise.reject({ data: { error: { code: 'CONCURRENCY_CONFLICT' } } })
  };
  const { vm } = installExcellence({ api });
  const target = vm.tasks[0];
  vm.beginListEdit(target);
  assert.deepEqual(remembered, { path: '/api/work-items/task-low', version: 1 });
  vm.listEditDraft.title = 'Yerel değişiklik';
  vm.listEditDraft.priority = 'Critical';
  const request = vm.saveListEdit(target);
  assert.equal(target.title, 'Yerel değişiklik');
  assert.equal(vm.pendingTaskIds[target.id], true);
  await request;
  assert.equal(target.title, 'Zulu işi');
  assert.equal(target.priority, 'Low');
  assert.equal(vm.pendingTaskIds[target.id], undefined);
  assert.equal(vm.listEditTaskId, null);
  assert.match(vm.listEditError, /Çakışma/);
});

test('partial bulk results keep failed work selected and report item-level reason', async () => {
  const api = {
    post: () => Promise.resolve({
      succeeded: 1,
      failed: 1,
      results: [
        { workItemId: 'task-low', success: true },
        { workItemId: 'task-high', success: false, errorCode: 'BOARD_WIP_LIMIT_EXCEEDED' }
      ]
    })
  };
  const { vm } = installExcellence({ api });
  vm.selectedTaskIds = { 'task-low': true, 'task-high': true };
  await vm.bulkMove('Review');
  assert.deepEqual(vm.selectedIds(), ['task-high']);
  assert.equal(vm.bulkResult.succeeded, 1);
  assert.equal(vm.bulkResult.failed, 1);
  assert.match(vm.bulkResult.failures[0].message, /WIP/);
  assert.equal(vm.reloads, 1);
});

test('viewer remains read-only across selection, inline edit and movement affordances', async () => {
  const { vm } = installExcellence({ role: 'Viewer' });
  assert.equal(vm.canEditWorkItems(), false);
  vm.beginListEdit(vm.tasks[0]);
  assert.equal(vm.listEditTaskId, null);
  assert.equal(vm.canMoveTaskDirection(vm.tasks[0], 1), false);
  await vm.moveTaskToColumn(vm.tasks[0].id, vm.board.columns[1]);
  assert.equal(vm.pendingTaskIds[vm.tasks[0].id], undefined);
});

test('board WIP state uses the whole loaded column, not each swimlane subset', () => {
  const feature = loadFactory(boardViewSource, 'desktopBoardViewFeature', [{}]);
  const vm = {
    board: {
      id: 'board-1', swimlaneMode: 'Priority', views: [],
      columns: [column('doing', 'In Progress', 2), column('done', 'Done')]
    },
    tasks: [
      task('high', 'Yüksek', 'High', 'doing', 'In Progress', 1),
      task('low', 'Düşük', 'Low', 'doing', 'In Progress', 2)
    ],
    priorityFilter: '',
    swimlaneMode: 'Priority',
    collapsedColumns: {},
    hasMoreTasks: false
  };
  feature.install(vm, { setBoardState() {}, apiActionError() {} });
  vm.refreshBoardModel();
  assert.equal(vm.boardRows.length, 2);
  for (const row of vm.boardRows) {
    assert.equal(row.columns[0].count, 1);
    assert.equal(row.columns[0].loadedCount, 2);
    assert.equal(row.columns[0].wipState, 'full');
    assert.equal(row.columns[0].atWipLimit, true);
  }
});

test('movement affordance is disabled before requesting a visibly WIP-full column', async () => {
  const { vm } = installExcellence();
  vm.board.columns[2].wipLimit = 1;
  vm.tasks.push(task('review-task', 'Review item', 'Medium', 'review', 'Review', 30));
  vm.refreshBoardModel();

  const moving = vm.tasks.find(item => item.id === 'task-high');
  assert.equal(vm.canMoveTaskDirection(moving, 1), false);
  await vm.moveTaskDirection(moving, 1);
  assert.equal(vm.pendingTaskIds[moving.id], undefined);
});

test('keyboard vertical movement reuses before-after rank placement', async () => {
  const { vm } = installExcellence();
  const first = vm.tasks.find(item => item.id === 'task-high');
  const second = vm.tasks.find(item => item.id === 'task-low');
  second.columnId = first.columnId;
  second.status = first.status;
  vm.boardRows = [{ columns: [{ tasks: [first, second] }] }];
  const event = { key: 'ArrowDown', altKey: true, preventDefault() { this.prevented = true; } };

  vm.handleTaskKey(event, first);
  await Promise.resolve();

  assert.equal(event.prevented, true);
  assert.equal(vm.dropCall.id, first.id);
  assert.equal(vm.dropCall.anchor.id, second.id);
  assert.equal(vm.dropCall.placement, 'after');
});

test('templates expose semantic table, keyboard/touch movement and permission-aware controls', () => {
  assert.match(desktopHtml, /<table class="work-table" aria-label="Proje işleri">/);
  assert.match(desktopHtml, /vm\.moveTaskDirection\(task, -1\)/);
  assert.match(desktopHtml, /data-wip-state="\{\{column\.wipState\}\}"/);
  assert.match(desktopHtml, /vm\.listEditTaskId === task\.id/);
  assert.match(desktopHtml, /vm\.canEditWorkItems\(\)/);
  assert.match(desktopHtml, /row in vm\.boardRows track by row\.label/);
  assert.match(desktopHtml, /column in row\.columns track by column\.id/);
  assert.match(desktopHtml, /task in vm\.listTasks track by task\.id/);
  assert.doesNotMatch(desktopHtml, /vm\.visibleListTasks\(\)/);
  assert.match(mobileHtml, /vm\.moveTask\(task, -1\)/);
  assert.match(mobileHtml, /task in vm\.visibleTaskItems track by task\.id/);
  assert.doesNotMatch(mobileHtml, /vm\.visibleTasks\(\)/);
  assert.match(mobileHtml, /'session-restoring': shell\.sessionRestoring/);
  assert.match(mobileApp, /browserSession: function\(authService\) \{ return authService\.restore\(\); \}/);
  assert.match(mobileApp, /\.state\('project-detail', protectedState\(/);
  assert.match(mobileAuth, /var restorePromise = null/);
  assert.match(mobileAuth, /restorePromise = apiClient\.get\('\/api\/browser-auth\/session'\)/);
  assert.match(mobileTasks, /BOARD_WIP_LIMIT_EXCEEDED/);
  assert.match(mobileTasks, /hasProjectPermission\(membership\.role, 'WorkItemUpdate'\)/);
});
