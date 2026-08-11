import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '../mobile-ionic');
const [api, tasks, details, workspace, html, css] = await Promise.all([
  'api.js', 'tasks.js', 'details.js', 'workspace.js', 'index.html', 'styles.css'
].map(file => readFile(resolve(root, file), 'utf8')));

test('mobile work modes keep real API-backed board list backlog and sprint commands', () => {
  for (const method of [
    'tasks', 'projectTasks', 'backlog', 'boards', 'sprints', 'sprintBurndown',
    'createSprint', 'planSprintItem', 'unplanSprintItem', 'startSprint',
    'completeSprint', 'createTask', 'moveTask'
  ]) {
    assert.match(api, new RegExp(`${method}: function`), `${method} API adapter is missing`);
  }
  for (const mode of ['my', 'backlog', 'sprint', 'board', 'list']) {
    assert.ok(html.includes(`vm.mode === '${mode}'`), `${mode} mobile mode is missing`);
  }
  for (const command of [
    'vm.openCreateSprint()', 'vm.planBacklogItem(task)', 'vm.unplanSprintItem(task)',
    'vm.startSprint()', 'vm.completeSprint()', 'vm.moveTask(task, -1)',
    'vm.moveTask(task, 1)', 'vm.quickAdd()'
  ]) {
    assert.ok(html.includes(command), `${command} is missing from the mobile surface`);
  }
  assert.match(tasks, /vm\.canEditTasks = function/);
  assert.match(tasks, /hasProjectPermission\(membership\.role, 'WorkItemUpdate'\)/);
  assert.match(tasks, /vm\.loadError = mobileActionError/);
  assert.match(tasks, /\$getByHandle\('taskWorkScroll'\)\.scrollTop\(true\)/);
  assert.match(html, /delegate-handle="taskWorkScroll"/);
  assert.match(html, /vm\.projectMissing/);
});

test('mobile task detail exposes essential edit and collaboration mutations', () => {
  for (const method of [
    'updateTask', 'moveTask', 'addComment', 'addChecklist', 'completeChecklist',
    'uploadAttachment', 'addWorkLog', 'setTaskWatch', 'setTaskVote',
    'decideTaskApproval', 'addTaskRelation'
  ]) {
    assert.match(api, new RegExp(`${method}: function`), `${method} API adapter is missing`);
  }
  for (const method of [
    'saveTask', 'move', 'addComment', 'addChecklist', 'toggleChecklist',
    'upload', 'addWorkLog', 'toggleWatch', 'toggleVote', 'decideApproval',
    'addRelation'
  ]) {
    assert.match(details, new RegExp(`vm\\.${method} = function`), `${method} controller command is missing`);
  }
  assert.match(details, /vm\.canEditTask = editableRole/);
  assert.match(details, /vm\.canApprove = managerRole/);
  assert.match(details, /vm\.offline = function/);
  assert.match(details, /return relation\.relatedWorkItemKey \|\| \(related && related\.title\) \|\| 'Bağlı görev'/);
  assert.doesNotMatch(html, /\{\{relation\.relatedWorkItemId\}\}/);
  for (const state of ['vm.loading', 'vm.loadError', 'vm.partial', '!vm.canEditTask()', 'shell.pwa.offline']) {
    assert.ok(html.includes(state), `${state} task detail state is missing`);
  }
  assert.match(html, /aria-pressed="\{\{vm\.collaboration\.watching\}\}"/);
  assert.match(html, /aria-pressed="\{\{vm\.collaboration\.voted\}\}"/);
});

test('touch-safe alternatives and stable controls do not depend on drag gestures', () => {
  assert.match(html, /aria-label="\{\{task\.title\}\} görevini önceki kolona taşı"/);
  assert.match(html, /aria-label="\{\{task\.title\}\} görevini sonraki kolona taşı"/);
  assert.match(html, /aria-label="\{\{task\.title\}\} işini sprint kapsamına al"/);
  assert.match(html, /aria-label="\{\{task\.title\}\} işini backlog alanına taşı"/);
  assert.match(css, /\.mobile-task-move \.button\s*\{[\s\S]*?min-width:\s*44px/);
  assert.match(css, /\.mobile-task-move \.button\s*\{[\s\S]*?min-height:\s*44px/);
  assert.match(css, /\.mobile-plan-button\s*\{[\s\S]*?width:\s*44px/);
  assert.match(css, /\.mobile-plan-button[\s\S]*min-height:\s*44px/);
});

test('search and inbox expose bounded loading error empty and read states', () => {
  assert.match(api, /searchWork: function/);
  assert.match(api, /notifications: function/);
  assert.match(api, /read: function/);
  assert.match(api, /code === 'STALE_RESPONSE'/);
  assert.match(api, /code === 'FORBIDDEN'/);
  assert.match(workspace, /vm\.refresh = function/);
  assert.match(workspace, /scroll\.refreshComplete/);
  assert.match(workspace, /notification\.reading = true/);
  assert.match(html, /vm\.visibleNotifications\(\)/);
  assert.match(html, /ng-attr-aria-busy/);
  assert.match(html, /!vm\.visibleNotifications\(\)\.length/);
});
