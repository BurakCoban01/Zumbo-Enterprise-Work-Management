import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '../projects/modern-desktop/src/app');
const feature = resolve(root, 'features/settings/account');
const [page, template, service, core, responsive, workspace, workspaceTemplate] = await Promise.all([
  readFile(resolve(feature, 'account-settings.page.ts'), 'utf8'),
  readFile(resolve(feature, 'account-settings.page.html'), 'utf8'),
  readFile(resolve(feature, 'account-settings.service.ts'), 'utf8'),
  readFile(resolve(feature, 'account-settings.core.ts'), 'utf8'),
  readFile(resolve(feature, 'account-settings-responsive.scss'), 'utf8'),
  readFile(resolve(root, 'workspace.page.ts'), 'utf8'),
  readFile(resolve(root, 'workspace.page.html'), 'utf8')
]);
const [shell, shellTemplate, shellCore, organizationPage, organizationTemplate, organizationService] = await Promise.all([
  readFile(resolve(root, 'features/settings/settings.page.ts'), 'utf8'),
  readFile(resolve(root, 'features/settings/settings.page.html'), 'utf8'),
  readFile(resolve(root, 'features/settings/settings.core.ts'), 'utf8'),
  readFile(resolve(root, 'features/settings/organization-access/organization-access-settings.page.ts'), 'utf8'),
  readFile(resolve(root, 'features/settings/organization-access/organization-access-settings.page.html'), 'utf8'),
  readFile(resolve(root, 'features/settings/organization-access/organization-access.service.ts'), 'utf8')
]);
const [integrationPage, integrationTemplate, integrationService, operationsPage, operationsTemplate, operationsService] = await Promise.all([
  readFile(resolve(root, 'features/settings/integrations/integration.page.ts'), 'utf8'),
  readFile(resolve(root, 'features/settings/integrations/integration.page.html'), 'utf8'),
  readFile(resolve(root, 'features/settings/integrations/integration.service.ts'), 'utf8'),
  readFile(resolve(root, 'features/settings/operations/operations.page.ts'), 'utf8'),
  readFile(resolve(root, 'features/settings/operations/operations.page.html'), 'utf8'),
  readFile(resolve(root, 'features/settings/operations/operations.service.ts'), 'utf8')
]);

test('modern account settings are a bounded feature and wired to the settings section', () => {
  assert.match(workspace, /SettingsPage/);
  assert.match(workspaceTemplate, /section\(\) === 'settings'/);
  assert.match(workspaceTemplate, /zumbo-settings-page/);
  assert.match(page, /providers: \[AccountSettingsService\]/);
});

test('account API service preserves the authenticated security contract', () => {
  for (const endpoint of [
    '/api/auth/mfa', '/api/auth/sessions', '/api/auth/api-keys',
    '/api/notifications/preferences/me', '/api/auth/change-password',
    '/api/auth/mfa/setup', '/api/auth/mfa/confirm', '/api/auth/mfa/disable',
    '/api/auth/mfa/recovery-codes', '/api/auth/privacy/export.ndjson',
    '/api/auth/privacy/anonymization-jobs'
  ]) assert.ok(service.includes(endpoint), `missing ${endpoint}`);
  assert.match(service, /failures\.push\(name\)/, 'partial read failures must not erase usable account data');
});

test('one-time secrets are memory-only and cleared when the settings page closes', () => {
  assert.match(page, /ngOnDestroy\(\): void \{ this\.clearOneTimeSecrets\(\); \}/);
  assert.match(page, /this\.mfaSetup\.set\(null\)/);
  assert.match(page, /this\.recoveryCodes\.set\(\[\]\)/);
  assert.match(page, /this\.createdApiKey\.set\(null\)/);
  assert.doesNotMatch(page, /localStorage\.setItem[^\n]*(secret|recovery|apiKey)/i);
  assert.match(page, /if \(this\.busy\(\) \|\| this\.mfaSetup\(\) \|\| this\.recoveryCodes\(\)\.length \|\| this\.createdApiKey\(\)\) return/);
});

test('session projection is bounded and destructive actions require confirmation', () => {
  assert.match(core, /inactiveLimit = 2/);
  assert.match(core, /Number\(isSessionActive\(right, now\)\) - Number\(isSessionActive\(left, now\)\)/);
  assert.match(page, /confirm\(value\.isCurrent/);
  assert.match(page, /currentSessionRevoked\.emit\(\)/);
  assert.match(page, /confirm\(`\$\{value\.name\} anahtarı kalıcı olarak iptal edilsin mi\?`\)/);
});

test('account settings expose complete states and adaptive controls', () => {
  for (const id of ['security', 'sessions', 'api-keys', 'notifications', 'privacy']) assert.match(template, new RegExp(`id="${id}"`));
  assert.match(template, /role="status"/);
  assert.match(template, /role="alert"/);
  assert.match(template, /@empty/);
  assert.match(responsive, /max-width:520px/);
  assert.match(responsive, /height:44px/);
  assert.match(responsive, /min-height:44px/);
});

test('settings shell derives access visibility from the runtime role catalog', () => {
  assert.match(shell, /SettingsService/);
  assert.match(shellCore, /UserRoleManage/);
  assert.doesNotMatch(shellTemplate, /pending-surface|hazırlanıyor/);
  assert.match(shellTemplate, /tabs\(\)\.includes\('access'\)/);
  assert.match(workspaceTemplate, /zumbo-settings-page/);
});

test('organization and access settings preserve bounded service ownership', () => {
  for (const endpoint of ['/api/organizations', '/api/auth/users', '/api/audit/entity/Organization/', '/api/auth/roles/']) assert.ok(organizationService.includes(endpoint));
  assert.match(organizationTemplate, /id="organization-heading"/);
  assert.match(organizationTemplate, /id="access-heading"/);
  assert.match(organizationTemplate, /permission\.label/);
  assert.match(organizationPage, /canAssignRole/);
  assert.match(organizationPage, /user\.id===this\.context\(\)\.id/);
  assert.match(organizationService, /ifMatch:role\.version/);
  assert.match(organizationService, /ifMatch:user\.version/);
});

test('integration settings keep receipts and credentials memory-only', () => {
  for (const endpoint of ['/api/integrations/webhooks', '/api/integrations/development']) assert.ok(integrationService.includes(endpoint));
  assert.match(integrationPage, /ngOnDestroy\(\)\{this\.clearSensitive\(\);\}/);
  assert.match(integrationPage, /this\.developmentDraft\.accessToken=''/);
  assert.match(integrationPage, /this\.credential=''/);
  assert.doesNotMatch(integrationPage, /localStorage|sessionStorage/);
  assert.match(integrationTemplate, /Bu sır yalnız şimdi gösterilir/);
  assert.match(integrationService, /ifMatch:value\.version/);
});

test('operations settings use independent reads and confirmed interventions', () => {
  for (const endpoint of ['/api/operations/external-dependencies', '/api/work-items/durable-messaging/metrics', '/api/notifications/delivery/status', '/api/operations/storage/security']) assert.ok(operationsService.includes(endpoint));
  assert.match(operationsService, /failures\.push\(name\)/);
  assert.match(operationsPage, /confirm\('Arama görünümü/);
  assert.match(operationsPage, /confirm\('Karantina kayıtları/);
  assert.match(operationsTemplate, /Alıcı ve hata gövdeleri bu görünümde tutulmaz/);
  assert.match(shellTemplate, /zumbo-integration-page/);
  assert.match(shellTemplate, /zumbo-operations-page/);
});
