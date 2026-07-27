import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const require = createRequire(import.meta.url);
const core = require(resolve(root, 'shared/webhook-core.js'));
const desktop = await readFile(resolve(root, 'desktop-bulma/integration-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobile = await readFile(resolve(root, 'mobile-ionic/integration-center.js'), 'utf8');
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const endpoint = await readFile(resolve(root, '../Backend/src/Zumbo.Api/Endpoints/WebhookEndpoints.cs'), 'utf8');
const service = await readFile(resolve(root, '../Backend/src/Zumbo.Modules.WorkItems/Webhooks.cs'), 'utf8');

test('webhook draft validation normalizes bounded known scopes', () => {
  const draft = core.validateDraft({
    name: '  Delivery flow  ',
    targetUrl: 'https://hooks.example.test/zumbo?token=private',
    eventScopes: ['work-item.updated', 'work-item.created', 'work-item.updated']
  });
  assert.equal(draft.name, 'Delivery flow');
  assert.deepEqual(draft.eventScopes, ['work-item.created', 'work-item.updated']);
  assert.throws(
    () => core.validateDraft({ name: '', targetUrl: 'https://hooks.example.test', eventScopes: [] }),
    error => error.code === 'WEBHOOK_NAME_INVALID'
  );
  assert.throws(
    () => core.validateDraft({ name: 'Flow', targetUrl: 'javascript:alert(1)', eventScopes: ['work-item.created'] }),
    error => error.code === 'WEBHOOK_TARGET_INVALID'
  );
});

test('safe target projection redacts query data and delivery payload stays out of templates', () => {
  assert.equal(
    core.safeTargetLabel('https://hooks.example.test/zumbo?token=private#fragment'),
    'https://hooks.example.test/zumbo?…'
  );
  assert.doesNotMatch(desktopHtml, /delivery\.payload\b/);
  assert.doesNotMatch(mobileHtml, /delivery\.payload\b/);
  assert.match(desktopHtml, /payloadSha256/);
  assert.match(mobileHtml, /payloadSha256/);
});

test('integration permission is role aware without granting ordinary users', () => {
  const customRoles = [{ name: 'IntegrationOperator', permissions: ['IntegrationManage'] }];
  assert.equal(core.hasPermission({ roles: ['OrganizationAdmin'] }, []), true);
  assert.equal(core.hasPermission({ roles: ['IntegrationOperator'] }, customRoles), true);
  assert.equal(core.hasPermission({ roles: ['User'] }, customRoles), false);
  assert.match(desktopHtml, /ng-if="vm\.canManageIntegrations\(\)"/);
  assert.match(mobileHtml, /vm\.forbidden/);
});

test('delivery state exposes safe recovery semantics', () => {
  assert.deepEqual(core.deliveryState({ status: 'Delivered' }), { label: 'Teslim edildi', tone: 'success' });
  assert.equal(core.canReplay({ status: 'DeadLetter' }), true);
  assert.equal(core.canReplay({ status: 'Pending' }), false);
  assert.equal(core.safeError('HTTP_503'), 'Alıcı geçici olarak kullanılamıyor (503).');
  assert.doesNotMatch(core.safeError('UNEXPECTED_PRIVATE_DETAIL'), /UNEXPECTED_PRIVATE_DETAIL/);
});

test('desktop and mobile provide the complete managed webhook lifecycle', () => {
  for (const path of [
    '/api/integrations/webhooks',
    '/api/integrations/webhooks/metrics',
    '/rotate-secret',
    '/test-delivery',
    '/deliveries',
    '/replay'
  ]) {
    assert.ok(desktop.includes(path), `desktop path ${path} is missing`);
    assert.ok(mobileApi.includes(path), `mobile path ${path} is missing`);
  }
  for (const binding of [
    'vm.saveWebhookSubscription()', 'vm.sendWebhookTest()', 'vm.rotateWebhookSecret()',
    'vm.setWebhookActive(', 'vm.replayWebhookDelivery('
  ]) assert.ok(desktopHtml.includes(binding), `${binding} desktop binding is missing`);
  for (const binding of [
    'vm.save()', 'vm.sendTest()', 'vm.rotateSecret()', 'vm.setActive(', 'vm.replay('
  ]) assert.ok(mobileHtml.includes(binding), `${binding} mobile binding is missing`);
});

test('one-time secret is memory-only and cleared on navigation lifecycle', () => {
  assert.match(desktop, /secretReceipt = null/);
  assert.match(desktop, /tab !== 'integrations'\) clearSensitiveState\(\)/);
  assert.match(mobile, /\$ionicView\.afterLeave', clearSecret/);
  assert.match(mobile, /\$destroy', clearSecret/);
  assert.doesNotMatch(desktop + mobile, /localStorage.*secret|sessionStorage.*secret/i);
  assert.match(desktopHtml, /Bu sır yalnız şimdi gösterilir/);
  assert.match(mobileHtml, /Bu sır yalnız şimdi gösterilir/);
});

test('backend test delivery is permission gated audited and contains no work item payload', () => {
  assert.match(endpoint, /MapPost\("\/\{id\}\/test-delivery"/);
  assert.match(endpoint, /WithZumboPermission\(PermissionCatalog\.IntegrationManage\)/);
  assert.match(endpoint, /RequireRateLimiting\("bulk"\)/);
  assert.match(service, /type = "webhook\.test"/);
  assert.match(service, /data = new \{ test = true \}/);
  assert.doesNotMatch(
    service.slice(service.indexOf('QueueTestDeliveryAsync'), service.indexOf('ListDeliveriesAsync')),
    /WorkItemRealtimeItem|workItem\s*=/
  );
  assert.match(service, /WebhookTestDeliveryQueued/);
  assert.match(service, /WEBHOOK_SUBSCRIPTION_DISABLED/);
});
