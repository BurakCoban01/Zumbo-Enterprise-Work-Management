import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const html = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const serviceWorker = await readFile(resolve(root, 'mobile-ionic/service-worker.js'), 'utf8');
const buildScript = await readFile(resolve(root, 'tests/build-frontend.mjs'), 'utf8');
const parity = JSON.parse(await readFile(resolve(root, '../docs/frontend-parity.json'), 'utf8'));
const mobileScripts = [
  'app.js', 'realtime.js', 'api.js', 'auth.js', 'workspace.js', 'tasks.js',
  'details.js', 'project-catalog.js', 'intake-center.js', 'work-automation.js', 'bulk-job-center.js', 'planning-views.js',
  'reporting-views.js', 'profile-security.js', 'privacy-center.js', 'integration-center.js',
  'operations-center.js', 'mobile-shell.js', 'directives.js', 'pwa.js'
];
const scriptSources = await Promise.all(mobileScripts.map(file =>
  readFile(resolve(root, 'mobile-ionic', file), 'utf8').catch(() => '')));
const app = scriptSources.join('\n');

test('mobile auth, session ve route sozlesmesi karakterize edilir', () => {
  for (const endpoint of [
    '/api/browser-auth/login', '/api/browser-auth/logout', '/api/browser-auth/session',
    '/api/auth/forgot-password', '/api/auth/reset-password'
  ]) {
    assert.ok(app.includes(endpoint), `${endpoint} behavior is missing`);
  }
  for (const route of [
    "state('login'", "state('forgot-password'", "state('reset-password'",
    "state('project-detail'", "state('project-jobs'", "state('team-detail'", "state('task-detail'",
    "state('integration-center'", "state('operations-center'",
    "state('app.dashboard'", "state('app.projects'", "state('app.tasks'",
    "state('app.create'", "state('app.notifications'", "state('app.more'",
    "state('app.search'", "state('app.profile'"
  ]) {
    assert.ok(app.includes(route), `${route} route is missing`);
  }
  assert.match(app, /MFA_REQUIRED/);
  assert.match(app, /apiClient\.clearSession\('logout'\)/);
});

test('mobile API adapter mevcut workspace ve work-item kabiliyetlerini korur', () => {
  for (const method of [
    'projects', 'createProject', 'updateProject', 'archiveProject', 'restoreProject',
    'boards', 'createBoard', 'updateBoard', 'archiveBoard', 'restoreBoard',
    'teams', 'createTeam', 'updateTeam', 'inviteTeamMember', 'removeTeamMember',
    'tasks', 'createTask', 'task', 'workflow', 'moveTask', 'addComment',
    'addChecklist', 'completeChecklist', 'addLabel', 'uploadAttachment',
    'deleteAttachment', 'downloadAttachment', 'summary', 'notifications', 'read'
  ]) {
    assert.match(app, new RegExp(`${method}: function`), `${method} API adapter is missing`);
  }
  assert.match(app, /scope: 'mobile-task-load', replace: true/);
});

test('mobile controller komutlari ve Ionic yasam dongusu karakterize edilir', () => {
  for (const controller of [
    'ShellController', 'LoginController', 'ForgotPasswordController',
    'ResetPasswordController', 'DashboardController', 'ProjectsController',
    'TasksController', 'NotificationsController', 'ProjectDetailController',
    'TeamDetailController', 'TaskDetailController', 'BulkJobCenterController', 'ProfileSecurityController',
    'IntegrationCenterController', 'MobileCreateController', 'MobileSearchController',
    'MobileMoreController'
  ]) {
    assert.match(app, new RegExp(`controller\\('${controller}'`), `${controller} is missing`);
  }
  for (const method of [
    'refresh', 'openTask', 'setMode', 'select', 'selectTeam', 'createProject',
    'createTeam', 'restoreProject', 'filter', 'loadMore', 'quickAdd',
    'selectBoard', 'createBoard', 'saveProject', 'saveBoard', 'invite',
    'removeMember', 'move', 'addComment', 'addChecklist', 'toggleChecklist',
    'addLabel', 'upload', 'removeAttachment', 'download'
  ]) {
    assert.match(app, new RegExp(`vm\\.${method}\\s*=\\s*function`), `${method} command is missing`);
  }
  assert.match(app, /\$ionicView\.beforeEnter/);
  assert.match(app, /scroll\.infiniteScrollComplete/);
});

test('mobile realtime, stale context ve concurrency davranisi korunur', () => {
  assert.match(app, /eventType === 'resyncRequired'/);
  assert.match(app, /requestResync\('version-gap'\)/);
  assert.match(app, /withStatefulReconnect\(\{ bufferSize: 65536 \}\)/);
  assert.match(app, /apiClient\.transitionContext\('project:' \+/);
  assert.match(app, /zumbo:concurrency-conflict/);
  assert.match(app, /CONCURRENCY_CONFLICT/);
  assert.match(app, /realtimeService\.synchronize\(vm\.tasks\)/);
});

test('mobile template essential komut ve erisilebilir alternatifleri korur', () => {
  for (const binding of [
    'vm.login()', 'vm.demo()', 'vm.refresh()', 'vm.createProject()',
    'vm.createTeam()', 'vm.quickAdd()', 'vm.createBoard()', 'vm.saveProject()',
    'vm.invite()', 'vm.removeMember(', 'vm.move(', 'vm.addComment()',
    'vm.addChecklist()', 'vm.upload()', 'shell.toggleTheme()', 'shell.logout()'
  ]) {
    assert.ok(html.includes(binding), `${binding} template binding is missing`);
  }
  assert.match(html, /<ion-refresher[^>]+on-refresh="vm\.refresh\(\)"/);
  assert.match(html, /aria-label="Mobil ekip üyesini kaldır"/);
  assert.match(html, /aria-label="Dosya yükle"/);
});

test('mobile PWA yalniz shell assetlerini cacheler ve API hub isteklerini dislar', () => {
  assert.match(app, /serviceWorker\.register\('\.\/service-worker\.js', \{ updateViaCache: 'none' \}\)/);
  assert.match(serviceWorker, /fetch\(MANIFEST_URL, \{ cache: 'no-store'/);
  assert.match(serviceWorker, /sha256\(bytes\) !== asset\.sha256/);
  assert.match(serviceWorker, /url\.pathname\.startsWith\('\/api\/'\)/);
  assert.match(serviceWorker, /url\.pathname\.startsWith\('\/hubs\/'\)/);
  assert.match(serviceWorker, /event\.request\.mode === 'navigate'/);
  assert.match(serviceWorker, /cache\.match\(fallbackUrl\)/);
  assert.match(serviceWorker, /self\.Response\.redirect\(fallbackUrl\.href, 302\)/);
  assert.doesNotMatch(serviceWorker, /Authorization|X-CSRF-Token/);
});

test('mobile composition on explicit modul ve ince route root kullanir', () => {
  const main = scriptSources[0];
  assert.ok(main.split(/\r?\n/).length < 80, 'mobile app.js composition root is too large');
  assert.match(scriptSources[1], /factory\('realtimeService'/);
  assert.match(scriptSources[2], /factory\('zumboApi'/);
  assert.match(scriptSources[3], /factory\('authService'/);
  assert.match(scriptSources[4], /controller\('ProjectsController'/);
  assert.match(scriptSources[5], /controller\('TasksController'/);
  assert.match(scriptSources[6], /controller\('TaskDetailController'/);
  assert.match(scriptSources[7], /controller\('ProjectCatalogController'/);
  assert.match(scriptSources[8], /controller\('MobileIntakeController'/);
  assert.match(scriptSources[8], /controller\('PublicIntakeController'/);
  assert.match(scriptSources[9], /controller\('WorkAutomationController'/);
  assert.match(scriptSources[10], /controller\('BulkJobCenterController'/);
  assert.match(scriptSources[11], /controller\('ProjectPlanningController'/);
  assert.match(scriptSources[12], /controller\('ProjectReportingController'/);
  assert.match(scriptSources[13], /controller\('ProfileSecurityController'/);
  assert.match(scriptSources[14], /factory\('mobilePrivacyFeature'/);
  assert.match(scriptSources[15], /controller\('IntegrationCenterController'/);
  assert.match(scriptSources[16], /controller\('OperationsCenterController'/);
  assert.match(scriptSources[17], /controller\('MobileCreateController'/);
  assert.match(scriptSources[18], /directive\('fileChange'/);
  assert.match(scriptSources[19], /factory\('mobilePwaService'/);
  assert.match(html, /<script src="\.\/app\.js"><\/script>\s*<script src="\.\/realtime\.js"><\/script>\s*<script src="\.\/api\.js"><\/script>\s*<script src="\.\/auth\.js"><\/script>\s*<script src="\.\/workspace\.js"><\/script>\s*<script src="\.\/tasks\.js"><\/script>\s*<script src="\.\/details\.js"><\/script>\s*<script src="\.\/project-catalog\.js"><\/script>\s*<script src="\.\/intake-center\.js"><\/script>\s*<script src="\.\/work-automation\.js"><\/script>\s*<script src="\.\/bulk-job-center\.js"><\/script>\s*<script src="\.\/planning-views\.js"><\/script>\s*<script src="\.\/reporting-views\.js"><\/script>\s*<script src="\.\/profile-security\.js"><\/script>\s*<script src="\.\/privacy-center\.js"><\/script>\s*<script src="\.\/integration-center\.js"><\/script>\s*<script src="\.\/operations-center\.js"><\/script>\s*<script src="\.\/mobile-shell\.js"><\/script>\s*<script src="\.\/directives\.js"><\/script>\s*<script src="\.\/pwa\.js"><\/script>/);
});

test('mobile essential backlog sprint board list ve work-item komutlari aciktir', () => {
  for (const method of [
    'projectTasks', 'sprints', 'backlog', 'updateTask', 'addWorkLog',
    'users', 'addProjectMember', 'changeProjectMemberRole', 'removeProjectMember'
  ]) {
    assert.match(app, new RegExp(`${method}: function`), `${method} mobile capability is missing`);
  }
  for (const binding of [
    "vm.setMode('backlog')", "vm.setMode('sprint')", "vm.setMode('board')", "vm.setMode('list')",
    'vm.saveTask()', 'vm.addWorkLog()', 'vm.addProjectMember()',
    'vm.saveProjectMember(', 'vm.removeProjectMember('
  ]) {
    assert.ok(html.includes(binding), `${binding} mobile binding is missing`);
  }
});

test('mobile offline ve update state explicit kullanici geri bildirimi verir', () => {
  assert.match(scriptSources[19], /offline: !\$window\.navigator\.onLine/);
  assert.match(scriptSources[19], /updateReady: false/);
  assert.match(scriptSources[19], /waiting\.postMessage\(\{ type: 'SKIP_WAITING' \}\)/);
  assert.match(serviceWorker, /event\.data\.type === 'SKIP_WAITING'/);
  assert.match(buildScript, /path\.startsWith\(`\$\{surface\.directory\}\/`\)/);
  assert.match(buildScript, /manifestPath: 'mobile-ionic\/pwa-manifest\.json'/);
  assert.match(html, /shell\.pwa\.offline/);
  assert.match(html, /shell\.pwa\.updateReady/);
  assert.match(html, /shell\.applyUpdate\(\)/);
});

test('machine-readable parity matrisi essential kapsam ve admin istisnalarini tamamlar', () => {
  const required = [
    'auth', 'home', 'project', 'backlog', 'sprint', 'board', 'list',
    'work-item', 'member', 'notifications', 'offline', 'update'
  ];
  assert.equal(parity.taskId, 'FE-004');
  assert.deepEqual(parity.essential.map(item => item.capability).sort(), required.sort());
  assert.ok(parity.essential.every(item => item.status !== 'missing' && item.mobileEvidence.length > 0));
  assert.ok(parity.administrativeExceptions.length >= 3);
  assert.ok(parity.administrativeExceptions.every(item =>
    item.classification === 'documented-desktop-first' && item.rationale && item.mobileAlternative));
});
