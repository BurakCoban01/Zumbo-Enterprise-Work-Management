import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const require = createRequire(import.meta.url);
const core = require(resolve(root, 'shared/operations-core.js'));
const desktop = await readFile(resolve(root, 'desktop-bulma/operations-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobile = await readFile(resolve(root, 'mobile-ionic/operations-center.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const workItemEndpoints = (await Promise.all([
  '../Backend/src/Zumbo.Api/Presentation/Endpoints/WorkItems/WorkItemsCore/ListDurableMessageDeadLettersEndpoint.cs',
  '../Backend/src/Zumbo.Api/Presentation/Endpoints/WorkItems/WorkItemsCore/ReplayDurableMessageDeadLetterEndpoint.cs',
  '../Backend/src/Zumbo.Api/Presentation/Endpoints/WorkItems/Search/ReconcileSearchIndexEndpoint.cs'
].map(path => readFile(resolve(root, path), 'utf8')))).join('\n');
const notificationEndpoints = await readFile(
  resolve(root, '../Backend/src/Zumbo.Api/Presentation/Endpoints/Notifications/NotificationsCore/NotificationEndpoints.cs'),
  'utf8'
);
const operationsEndpoints = await readFile(
  resolve(root, '../Backend/src/Zumbo.Api/Presentation/Endpoints/Platform/PlatformCore/OperationsEndpoints.cs'),
  'utf8'
);
const operationsAdapters = await readFile(
  resolve(root, '../Backend/src/Zumbo.Api/Infrastructure/Adapters/Platform/Storage/OperationsAdapters.cs'),
  'utf8'
);
const desktopOperationsTemplate = desktopHtml.slice(
  desktopHtml.indexOf('<div class="settings-content operations-center"'),
  desktopHtml.indexOf('\n      </div>\n    </section>', desktopHtml.indexOf('<div class="settings-content operations-center"'))
);
const mobileOperationsTemplate = mobileHtml.slice(
  mobileHtml.indexOf('<script id="templates/operations-center.html"'),
  mobileHtml.indexOf('</script>', mobileHtml.indexOf('<script id="templates/operations-center.html"'))
);

test('operations permission remains restricted to system administrators', () => {
  const roles = [
    { name: 'SystemAdmin', permissions: ['OperationsManage'] },
    { name: 'OrganizationAdmin', permissions: ['OrganizationManage'] },
    { name: 'User', permissions: ['ProfileRead'] }
  ];
  assert.equal(core.hasPermission({ roles: ['SystemAdmin'] }, roles), true);
  assert.equal(core.hasPermission({ roles: ['OrganizationAdmin'] }, roles), false);
  assert.equal(core.hasPermission({ roles: ['User'] }, roles), false);
  assert.match(desktopHtml, /ng-if="vm\.canManageOperations\(\)"/);
  assert.match(mobileHtml, /vm\.forbidden/);
});

test('health model distinguishes available degraded unavailable and unknown', () => {
  assert.equal(core.dependencyState(null).key, 'unknown');
  assert.equal(core.dependencyState({ circuitOpen: true }).key, 'unavailable');
  assert.equal(core.dependencyState({ timedOut: 1, succeeded: 4 }).key, 'degraded');
  assert.equal(core.dependencyState({ succeeded: 4 }).key, 'available');
  assert.equal(core.overallState([], { deadLetter: 1 }, {}, {}).key, 'attention');
});

test('operator labels do not echo provider or unknown event identifiers', () => {
  assert.equal(core.dependencyLabel('mongodb'), 'Belge verisi');
  assert.notEqual(core.dependencyLabel('mongodb'), 'mongodb');
  assert.equal(core.eventLabel('private.event.with.identifier'), 'Sınıflandırılmış sistem olayı');
  assert.doesNotMatch(core.eventLabel('private.event.with.identifier'), /private|identifier/i);
});

test('desktop and mobile consume every operations capability with confirmed safe actions', () => {
  for (const path of [
    '/api/operations/external-dependencies',
    '/api/work-items/durable-messaging/metrics',
    '/api/work-items/durable-messaging/dead-letters',
    '/api/notifications/delivery/status',
    '/api/notifications/delivery/dead-letters',
    '/api/operations/storage/security',
    '/api/work-items/search/reconcile'
  ]) {
    assert.ok(desktop.includes(path), `desktop path ${path} is missing`);
    assert.ok(mobile.includes(path), `mobile path ${path} is missing`);
  }
  assert.match(desktop, /\$window\.confirm/);
  assert.match(mobile, /\$ionicPopup\.confirm/);
  assert.match(desktopHtml, /vm\.pwa\.offline/);
  assert.match(mobileHtml, /shell\.pwa\.offline/);
});

test('operations templates keep raw identifiers payloads and storage keys out of visible copy', () => {
  const templates = desktopOperationsTemplate + mobileOperationsTemplate;
  assert.doesNotMatch(templates, /\{\{\s*item\.id\s*\}\}/);
  assert.doesNotMatch(templates, /\{\{[^}]*payload[^}]*\}\}/i);
  assert.doesNotMatch(templates, /\{\{[^}]*storagePath[^}]*\}\}/i);
  assert.doesNotMatch(templates, /\{\{[^}]*correlation[^}]*\}\}/i);
  assert.doesNotMatch(templates, /\{\{[^}]*lastError[^}]*\}\}/i);
});

test('backend recovery routes are globally authorized bounded and audited', () => {
  assert.match(workItemEndpoints, /ListDeadLettersAsync/);
  assert.match(workItemEndpoints, /DurableMessageReplayed/);
  assert.match(workItemEndpoints, /SearchIndexReconciled/);
  assert.match(notificationEndpoints, /NotificationDeliveryReplayed/);
  assert.match(operationsEndpoints, /OperationsStorageSecurityCoordinator/);
  assert.match(operationsAdapters, /AttachmentSecurityMaintenanceRun/);
  assert.doesNotMatch(operationsEndpoints, /using Zumbo\.Modules\./);
  for (const source of [workItemEndpoints, notificationEndpoints, operationsEndpoints]) {
    assert.match(source, /PermissionCatalog\.OperationsManage/);
  }
});
