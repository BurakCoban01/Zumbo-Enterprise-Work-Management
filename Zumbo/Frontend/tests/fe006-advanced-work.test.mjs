import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const planning = await readFile(resolve(root, 'desktop-bulma/planning.js'), 'utf8');
const desktopApp = await readFile(resolve(root, 'desktop-bulma/app.js'), 'utf8');
const taskBoard = await readFile(resolve(root, 'desktop-bulma/task-board.js'), 'utf8');
const boardView = await readFile(resolve(root, 'desktop-bulma/board-view.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/styles.css'), 'utf8');
const mobile = await readFile(resolve(root, 'mobile-ionic/tasks.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');

test('backlog ve sprint verisi gercek project endpointlerinden yuklenir', () => {
  assert.ok(planning.includes("'/api/sprints/projects/' + projectId + '?pageSize=50'"));
  assert.ok(planning.includes("'/api/sprints/projects/' + projectId + '/backlog?pageSize=100'"));
  assert.match(planning, /vm\.backlogItems = data\.items/);
  assert.match(planning, /vm\.sprints = data\.items/);
});

test('sprint yasam dongusu gercek create plan unplan start complete komutlarini tasir', () => {
  for (const endpoint of [
    "apiClient.post('/api/sprints'",
    "apiClient.put(\n              '/api/sprints/'",
    "apiClient.delete(\n              '/api/sprints/'",
    "'/start'",
    "'/complete'"
  ]) assert.ok(planning.includes(endpoint), `${endpoint} is missing`);
  for (const method of ['createSprint', 'planBacklogItem', 'unplanSprintItem', 'startSelectedSprint', 'completeSelectedSprint']) {
    assert.match(planning, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
});

test('sprint komutlari viewer icin istemci tarafinda gizlenir ve API yetkisi esas kalir', () => {
  assert.match(planning, /vm\.projectMembership\.role !== 'Viewer'/);
  assert.match(desktopHtml, /ng-if="vm\.canPlanSprint\(\)[^"]*"/);
  assert.doesNotMatch(planning, /permission\s*=|role\s*=\s*'ProjectOwner'/);
});

test('pano liste backlog sprint takvim timeline ve roadmap tek erisilebilir tablist icindedir', () => {
  assert.match(desktopHtml, /class="work-mode-tabs"[^>]+role="tablist"/);
  for (const mode of ['board', 'list', 'backlog', 'sprint', 'calendar', 'timeline', 'roadmap']) {
    assert.ok(desktopHtml.includes(`vm.setWorkMode('${mode}')`), `${mode} tab is missing`);
    assert.ok(desktopHtml.includes(`vm.workMode === '${mode}'`), `${mode} surface is missing`);
  }
});

test('liste ve bulk yuzeyleri ayni authoritative task koleksiyonunu kullanir', () => {
  assert.match(desktopHtml, /class="work-table-row"[^>]+ng-repeat="task in vm\.tasks/);
  assert.match(desktopHtml, /vm\.toggleTaskSelection\(task\.id\)/);
  assert.match(taskBoard, /\/api\/work-items\/bulk\/move/);
  assert.match(taskBoard, /\/api\/work-items\/bulk\/assign/);
  assert.match(taskBoard, /\/api\/work-items\/bulk\/archive/);
});

test('filter ve kayitli gorunum backend board view kontratini korur', () => {
  assert.match(boardView, /'\/api\/boards\/' \+ vm\.board\.id \+ '\/views'/);
  assert.match(boardView, /filter:\s*\{/);
  assert.match(desktopHtml, /id="saved-view"/);
  assert.match(desktopHtml, /id="priority-filter"/);
});

test('conflict ve optimistic rollback ileri gorunumlerde korunur', () => {
  assert.match(taskBoard, /var snapshot = angular\.copy\(task\)/);
  assert.match(taskBoard, /angular\.extend\(task, snapshot\)/);
  assert.match(taskBoard, /movementError\(error\)/);
  assert.match(taskBoard, /return vm\.loadTasks\(\)/);
});

test('timeline yalniz yetkili ve bounded entity audit kaynaklarini birlestirir', () => {
  assert.match(planning, /\/api\/audit\/entity\/' \+ type \+ '\/' \+ id/);
  assert.match(planning, /entityAudit\('Project', projectId\)/);
  assert.match(planning, /entityAudit\('Board', vm\.board\.id\)/);
  assert.match(planning, /entityAudit\('Sprint', sprint\.id\)/);
  assert.match(planning, /entityAudit\('WorkItem', vm\.selectedTask\.id\)/);
  assert.doesNotMatch(planning, /\/api\/audit\/\?organizationId=/);
  assert.match(desktopHtml, /class="project-timeline"/);
});

test('takvim due date ve roadmap sprint tarihlerini gercek modellerden turetir', () => {
  assert.match(planning, /task\.dueDate/);
  assert.match(planning, /vm\.calendarGroups = Object\.keys\(byDate\)/);
  assert.match(planning, /vm\.roadmapSprints = \(vm\.sprints \|\| \[\]\)/);
  assert.match(desktopHtml, /datetime="\{\{day\.key\}\}"/);
  assert.match(desktopHtml, /sprint\.startDate/);
  assert.match(desktopHtml, /sprint\.endDate/);
});

test('desktop responsive reflow ve mobile essential modlari birlikte korunur', () => {
  assert.match(desktopCss, /@media \(max-width: 760px\)[\s\S]+\.sprint-create, \.sprint-summary/);
  assert.match(desktopCss, /\.roadmap-list article \{ grid-template-columns: 34px minmax\(0, 1fr\); \}/);
  assert.match(desktopCss, /\.side-nav nav,[\s\S]+display: flex;[\s\S]+overflow-x: auto/);
  assert.match(desktopCss, /\.nav-secondary,[\s\S]+display: none/);
  assert.match(mobile, /var modes = \['my', 'backlog', 'sprint', 'board', 'list'\]/);
  for (const mode of ['backlog', 'sprint', 'board', 'list']) {
    assert.ok(mobileHtml.includes(`vm.setMode('${mode}')`));
  }
});

test('permission loss deep link stale board task ve planning state tasimaz', () => {
  assert.match(desktopApp, /if \(linked && !membershipFor\(linked\)\)/);
  assert.match(desktopApp, /apiClient\.transitionContext\('permission-lost:' \+ linked\.id\)/);
  for (const state of ['board', 'boards', 'tasks', 'backlogItems', 'sprints', 'timelineEntries', 'selectedTask']) {
    assert.match(desktopApp, new RegExp(`vm\\.${state} = (?:null|\\[\\])`), `${state} was not cleared`);
  }
  assert.match(desktopApp, /updateLocation\('projects', null, false\)/);
});
