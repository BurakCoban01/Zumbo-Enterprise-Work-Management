import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const require = createRequire(import.meta.url);
const core = require(resolve(root, 'shared/development-integration-core.js'));
const desktop = await readFile(resolve(root, 'desktop-bulma/integration-center.js'), 'utf8');
const desktopWorkItems = await readFile(resolve(root, 'desktop-bulma/work-items.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobile = await readFile(resolve(root, 'mobile-ionic/integration-center.js'), 'utf8');
const mobileDetails = await readFile(resolve(root, 'mobile-ionic/details.js'), 'utf8');
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const endpoints = await readFile(
  resolve(root, '../Backend/src/Zumbo.Api/Endpoints/DevelopmentIntegrationEndpoints.cs'),
  'utf8'
);

test('provider drafts use bounded least-privilege connection contracts', () => {
  const github = core.validateConnectionDraft({
    name: '  Product repositories ',
    provider: 'github',
    baseUrl: '',
    accessToken: 'synthetic-provider-token-123456'
  });
  assert.deepEqual(github, {
    name: 'Product repositories',
    provider: 'GitHub',
    baseUrl: 'https://api.github.com',
    accessToken: 'synthetic-provider-token-123456'
  });
  assert.throws(
    () => core.validateConnectionDraft({
      name: 'Unsafe',
      provider: 'GitHub',
      baseUrl: 'https://api.github.com?token=private',
      accessToken: 'synthetic-provider-token-123456'
    }),
    error => error.code === 'DEVELOPMENT_BASE_URL_INVALID'
  );
  assert.throws(
    () => core.validateConnectionDraft({
      name: 'Short',
      provider: 'GitLab',
      baseUrl: '',
      accessToken: 'short'
    }),
    error => error.code === 'DEVELOPMENT_CREDENTIAL_INVALID'
  );
});

test('repository mapping and work-item links stay on the selected provider host', () => {
  const repository = {
    externalRepositoryId: '42',
    name: 'product',
    fullName: 'zumbo/product',
    url: 'https://github.com/zumbo/product',
    defaultBranch: 'main'
  };
  assert.equal(core.mappingRequest('project-1', repository).repositoryFullName, 'zumbo/product');
  const mapping = {
    id: 'mapping-1',
    repositoryUrl: repository.url,
    isActive: true
  };
  const link = core.validateLinkDraft({
    mappingId: mapping.id,
    kind: 'pullrequest',
    externalId: 'pr:17',
    title: 'Ship integration',
    url: 'https://github.com/zumbo/product/pull/17?view=files',
    branch: 'feature/integration',
    commitSha: '0123456789abcdef',
    status: 'open'
  }, [mapping]);
  assert.equal(link.kind, 'PullRequest');
  assert.equal(link.status, 'Open');
  assert.throws(
    () => core.validateLinkDraft({ ...link, url: 'https://example.test/private' }, [mapping]),
    error => error.code === 'DEVELOPMENT_LINK_HOST_INVALID'
  );
});

test('desktop and mobile expose the complete provider and mapping lifecycle', () => {
  const paths = [
    '/api/integrations/development',
    '/rotate-credential',
    '/rotate-webhook-secret',
    '/repositories',
    '/mappings',
    '/health',
    '/disconnect'
  ];
  for (const path of paths) {
    assert.ok(desktop.includes(path), `desktop path ${path} is missing`);
    assert.ok(mobileApi.includes(path), `mobile path ${path} is missing`);
  }
  for (const binding of [
    'vm.saveDevelopmentConnection()',
    'vm.createDevelopmentMapping()',
    'vm.disconnectDevelopmentConnection()',
    'vm.deleteDevelopmentConnection()'
  ]) assert.ok(desktopHtml.includes(binding), `${binding} desktop binding is missing`);
  for (const binding of [
    'vm.saveDevelopment()',
    'vm.createDevelopmentMapping()',
    'vm.disconnectDevelopment()',
    'vm.deleteDevelopment()'
  ]) assert.ok(mobileHtml.includes(binding), `${binding} mobile binding is missing`);
  assert.match(
    desktop,
    /\/api\/projects\?organizationId=' \+ encodeURIComponent\(\s*vm\.session\.currentUser\.organizationId/
  );
  assert.match(
    mobileApi,
    /\/api\/projects\?organizationId=' \+ sessionStore\.state\.currentUser\.organizationId/
  );
});

test('work-item development links have desktop and mobile create/read/delete parity', () => {
  for (const source of [desktopWorkItems, mobileApi]) {
    assert.match(source, /\/api\/work-items\/.*\/development-links/);
    assert.match(source, /development-links\/mappings/);
  }
  assert.match(mobileDetails, /createTaskDevelopmentLink/);
  assert.match(mobileDetails, /deleteTaskDevelopmentLink/);
  assert.match(desktopHtml, /task-development-section/);
  assert.match(mobileHtml, /mobile-task-development/);
  assert.match(desktopHtml, /link\.source === 'Manual'/);
  assert.match(mobileHtml, /link\.source === 'Manual'/);
});

test('one-time secrets and credentials stay memory-only and leave no unsafe projection', () => {
  assert.match(desktop, /clearDevelopmentSensitiveState/);
  assert.match(mobile, /\$ionicView\.afterLeave', clearSecret/);
  assert.doesNotMatch(desktop + mobile + desktopHtml + mobileHtml, /localStorage.*(?:accessToken|webhookSecret)/i);
  assert.doesNotMatch(desktopHtml + mobileHtml, /credentialProtected|webhookSecretProtected/);
  assert.match(desktopHtml, /type="password".*developmentCenter\.draft\.accessToken/);
  assert.match(mobileHtml, /type="password".*development\.draft\.accessToken/);
});

test('backend keeps anonymous ingress separate from authorized management and link routes', () => {
  assert.match(endpoints, /MapGroup\("\/integrations\/development"\)[\s\S]*RequireAuthorization\(\)/);
  assert.match(endpoints, /WithZumboPermission\(PermissionCatalog\.IntegrationManage\)/);
  assert.match(
    endpoints,
    /MapGroup\([\s\S]*development-links[\s\S]*MapGet\("\/mappings"/
  );
  assert.match(endpoints, /WithZumboPermission\(PermissionCatalog\.WorkItemLink\)/);
  assert.match(endpoints, /MapPost\("\/\{connectionId\}\/webhook", ReceiveWebhookAsync\)[\s\S]*AllowAnonymous\(\)/);
});
