import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const html = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopScripts = ['app.js', 'realtime.js', 'task-board.js', 'settings.js', 'privacy-center.js', 'audit-center.js', 'integration-center.js', 'planning.js', 'planning-views.js', 'reporting-views.js', 'portfolio-center.js', 'goal-center.js', 'capacity-center.js', 'knowledge-center.js', 'board-view.js', 'board-excellence.js', 'work-items.js', 'management.js', 'directives.js', 'shell.js', 'work-automation.js', 'bulk-job-center.js', 'intake-center.js'];
const scriptSources = await Promise.all(desktopScripts.map(file =>
  readFile(resolve(root, 'desktop-bulma', file), 'utf8').catch(() => '')));
const sourceByName = Object.fromEntries(desktopScripts.map((file, index) => [file, scriptSources[index]]));
const app = scriptSources.join('\n');

test('desktop auth, routing ve realtime gorunur sozlesmesi karakterize edilir', () => {
  assert.match(app, /\/api\/browser-auth\/login/);
  assert.match(app, /\/api\/browser-auth\/logout/);
  assert.match(app, /\/api\/browser-auth\/session/);
  assert.match(app, /new URLSearchParams\(\$window\.location\.hash\.slice\(1\)\)/);
  assert.match(app, /updateLocation\(section, taskId, push\)/);
  assert.match(app, /eventType === 'resyncRequired'/);
  assert.match(app, /realtimeService\.synchronize\(vm\.tasks\)/);
  assert.match(app, /apiClient\.transitionContext\('project:' \+ project\.id\)/);
});

test('desktop board, detail ve yonetim komutlari karakterize edilir', () => {
  for (const method of [
    'createTask', 'bulkMove', 'moveTaskToColumn', 'loadArchivedTasks', 'restoreTask',
    'selectTask', 'saveSelectedTask', 'addComment', 'uploadAttachment', 'moveSelected',
    'loadRoleAdministration', 'saveOrganization', 'loadReports', 'saveWorkflow',
    'selectTeam', 'saveTeam', 'selectBoard', 'saveBoard', 'selectProject', 'saveProject'
  ]) {
    assert.match(app, new RegExp(`vm\\.${method}\\s*=\\s*function`), `${method} behavior is missing`);
  }
  assert.match(app, /CONCURRENCY_CONFLICT/);
  assert.match(app, /movementError\(error\)/);
  assert.match(app, /snapshot\.status/);
});

test('desktop template ana feature bindinglerini korur', () => {
  for (const binding of [
    'vm.login()', 'vm.logout()', 'vm.openEntityCreator(', 'vm.bulkMove(',
    'vm.selectTask(', 'vm.saveSelectedTask()', 'vm.saveWorkflow()',
    'vm.saveOrganization()', 'vm.saveTeam()',
    'vm.saveBoard()', 'vm.saveProject()'
  ]) {
    assert.ok(html.includes(binding), `${binding} template binding is missing`);
  }
  for (const directive of ['file-change', 'lucide-icon', 'command-focus', 'draggable-task', 'drop-lane', 'drop-task-before']) {
    assert.ok(html.includes(directive), `${directive} directive binding is missing`);
  }
});

test('realtime ve DOM adapterlari monolit controller disinda explicit modullerdir', () => {
  const main = sourceByName['app.js'];
  assert.doesNotMatch(main, /factory\('realtimeService'/);
  assert.doesNotMatch(main, /\.directive\(/);
  assert.match(sourceByName['realtime.js'], /factory\('realtimeService'/);
  assert.equal((sourceByName['directives.js'].match(/\.directive\(/g) || []).length, 6);
  assert.match(sourceByName['shell.js'], /factory\('desktopShellFeature'/);
  assert.match(sourceByName['planning-views.js'], /factory\('desktopPlanningViewsFeature'/);
  assert.match(sourceByName['reporting-views.js'], /factory\('desktopReportingViewsFeature'/);
  assert.match(sourceByName['portfolio-center.js'], /factory\('desktopPortfolioFeature'/);
  assert.match(sourceByName['goal-center.js'], /factory\('desktopGoalFeature'/);
  assert.match(sourceByName['capacity-center.js'], /factory\('desktopCapacityFeature'/);
  assert.match(sourceByName['knowledge-center.js'], /factory\('desktopKnowledgeFeature'/);
  assert.match(sourceByName['work-automation.js'], /factory\('desktopWorkAutomationFeature'/);
  assert.match(sourceByName['bulk-job-center.js'], /factory\('desktopBulkJobFeature'/);
  assert.match(sourceByName['intake-center.js'], /factory\('desktopIntakeFeature'/);
  assert.match(sourceByName['privacy-center.js'], /factory\('desktopPrivacyFeature'/);
  assert.match(sourceByName['audit-center.js'], /factory\('desktopAuditFeature'/);
  assert.match(sourceByName['integration-center.js'], /factory\('desktopIntegrationFeature'/);
  assert.match(html, /<script src="\.\/app\.js"><\/script>\s*<script src="\.\/realtime\.js"><\/script>\s*<script src="\.\/task-board\.js"><\/script>\s*<script src="\.\/settings\.js"><\/script>\s*<script src="\.\/planning\.js"><\/script>\s*<script src="\.\/planning-views\.js"><\/script>\s*<script src="\.\/reporting-views\.js"><\/script>\s*<script src="\.\/portfolio-center\.js"><\/script>\s*<script src="\.\/goal-center\.js"><\/script>\s*<script src="\.\/capacity-center\.js"><\/script>\s*<script src="\.\/knowledge-center\.js"><\/script>\s*<script src="\.\/board-view\.js"><\/script>\s*<script src="\.\/board-excellence\.js"><\/script>\s*<script src="\.\/work-items\.js"><\/script>\s*<script src="\.\/management\.js"><\/script>\s*<script src="\.\/directives\.js"><\/script>/);
  assert.ok(main.split(/\r?\n/).length <= 420, 'desktop app.js composition root exceeded its bounded budget');
});

test('settings, organization, access ve privacy davranisi explicit feature service tarafindan sahiplenilir', () => {
  const main = sourceByName['app.js'];
  const settings = sourceByName['settings.js'];
  const privacy = sourceByName['privacy-center.js'];
  assert.match(main, /desktopSettingsFeature\.install\(vm, desktopTasks\.apiActionError\)/);
  assert.match(main, /desktopPrivacyFeature\.install\(vm, desktopTasks\.apiActionError\)/);
  assert.match(settings, /factory\('desktopSettingsFeature'/);
  for (const method of [
    'loadSettings', 'loadRoleAdministration', 'saveUserRoles', 'saveOrganization',
    'addDepartment', 'changePassword', 'beginMfaSetup', 'createApiKey',
    'saveNotificationPreferences'
  ]) {
    assert.doesNotMatch(main, new RegExp(`vm\\.${method}\\s*=\\s*function`));
    assert.match(settings, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
  for (const method of ['exportPrivacyData', 'anonymizeAccount', 'loadPrivacyWorkflowStatus']) {
    assert.doesNotMatch(main, new RegExp(`vm\\.${method}\\s*=\\s*function`));
    assert.match(privacy, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
});

test('ortak feedback gorunumu explicit component binding kullanir', () => {
  const adapters = sourceByName['directives.js'];
  assert.match(adapters, /\.component\('zumboFeedback'/);
  assert.match(adapters, /bindings:\s*\{\s*feedback:\s*'='/);
  assert.match(adapters, /class="toast"/);
  assert.match(adapters, /role="status" aria-live="polite"/);
  assert.match(html, /<zumbo-feedback feedback="vm\.feedback"><\/zumbo-feedback>/);
});

test('reporting ve workflow feature service controller disinda sahiplenilir', () => {
  const main = sourceByName['app.js'];
  const planning = sourceByName['planning.js'];
  assert.match(main, /desktopPlanningFeature\.install\(vm, desktopTasks\.apiActionError\)/);
  assert.match(planning, /factory\('desktopPlanningFeature'/);
  for (const method of ['loadReports', 'loadWorkflow', 'addWorkflowStatus', 'saveWorkflow']) {
    assert.doesNotMatch(main, new RegExp(`vm\\.${method}\\s*=\\s*function`));
    assert.match(planning, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
});

test('work-item detail feature service explicit helper binding kullanir', () => {
  const main = sourceByName['app.js'];
  const workItems = sourceByName['work-items.js'];
  assert.match(main, /desktopWorkItemFeature\.install\(vm, \{/);
  assert.match(workItems, /factory\('desktopWorkItemFeature'/);
  for (const helper of ['updateLocation', 'nextStatusFor', 'apiActionError']) {
    assert.match(workItems, new RegExp(`var ${helper} = helpers\\.${helper}`));
  }
  for (const method of [
    'selectTask', 'saveSelectedTask', 'archiveSelectedTask', 'addComment',
    'uploadAttachment', 'moveSelected', 'loadWorkItemSchema', 'addRelation',
    'requestApproval', 'decideApproval'
  ]) {
    assert.doesNotMatch(main, new RegExp(`vm\\.${method}\\s*=\\s*function`));
    assert.match(workItems, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
});

test('project, team ve board yonetimi tek feature service ve acik state API kullanir', () => {
  const main = sourceByName['app.js'];
  const management = sourceByName['management.js'];
  assert.match(main, /desktopManagementFeature\.install\(vm, \{/);
  assert.match(management, /factory\('desktopManagementFeature'/);
  for (const stateMethod of ['membershipFor', 'firstAccessibleProject', 'setBoardState', 'setProjectState', 'rememberProject']) {
    assert.match(management, new RegExp(`${stateMethod}: ${stateMethod}`));
  }
  for (const method of [
    'loadProjectAudit', 'loadTeams', 'selectTeam', 'saveTeam', 'loadBoards',
    'selectBoard', 'saveBoard', 'addBoardColumn', 'saveProject',
    'addProjectMember', 'selectProject'
  ]) {
    assert.doesNotMatch(main, new RegExp(`vm\\.${method}\\s*=\\s*function`));
    assert.match(management, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
});

test('board view modeli ve status helper board feature service tarafindan sahiplenilir', () => {
  const main = sourceByName['app.js'];
  const boardView = sourceByName['board-view.js'];
  assert.match(main, /desktopBoardViewFeature\.install\(vm, \{/);
  assert.match(boardView, /factory\('desktopBoardViewFeature'/);
  assert.match(boardView, /return \{ nextStatusFor: nextStatusFor \}/);
  for (const method of ['selectView', 'updateSwimlane', 'saveCurrentView', 'deleteCurrentView', 'refreshBoardModel']) {
    assert.doesNotMatch(main, new RegExp(`vm\\.${method}\\s*=\\s*function`));
    assert.match(boardView, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
});

test('task list, bulk, archive ve notification feature service tarafindan sahiplenilir', () => {
  const main = sourceByName['app.js'];
  const taskBoard = sourceByName['task-board.js'];
  assert.match(main, /desktopTaskBoardFeature\.install\(vm, \{/);
  assert.match(taskBoard, /factory\('desktopTaskBoardFeature'/);
  assert.match(taskBoard, /return \{ apiActionError: apiActionError \}/);
  for (const method of [
    'seed', 'openEntityCreator', 'submitEntityCreator', 'bulkMove',
    'dropTaskBefore', 'moveTaskToColumn', 'loadTasks', 'loadArchivedTasks',
    'restoreTask', 'restoreLifecycleEntity', 'loadNotifications', 'readNotification'
  ]) {
    assert.doesNotMatch(main, new RegExp(`vm\\.${method}\\s*=\\s*function`));
    assert.match(taskBoard, new RegExp(`vm\\.${method}\\s*=\\s*function`));
  }
});
