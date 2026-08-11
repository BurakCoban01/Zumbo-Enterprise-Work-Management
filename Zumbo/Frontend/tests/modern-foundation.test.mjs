import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import ts from 'typescript';
import { verifyModernAssetManifest } from './build-modern-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const require = createRequire(import.meta.url);

test('modern workspace pins the accepted Angular and Ionic compatibility set', async () => {
  const packageJson = JSON.parse(await read('package.json'));
  assert.equal(packageJson.packageManager, 'pnpm@9.0.0');
  assert.equal(packageJson.engines.node, '>=20.9.0 <21 || >=22.22.3 <23');
  assert.deepEqual({
    angular: packageJson.dependencies['@angular/core'],
    ionic: packageJson.dependencies['@ionic/angular'],
    rxjs: packageJson.dependencies.rxjs,
    zone: packageJson.dependencies['zone.js'],
    cli: packageJson.devDependencies['@angular/cli'],
    typescript: packageJson.devDependencies.typescript
  }, {
    angular: '22.0.8',
    ionic: '8.8.15',
    rxjs: '7.8.2',
    zone: '0.16.2',
    cli: '22.0.9',
    typescript: '6.0.2'
  });
  const npmrc = await read('.npmrc');
  const lockfile = await read('pnpm-lock.yaml');
  assert.match(npmrc, /^strict-peer-dependencies=true$/m);
  assert.doesNotMatch(lockfile, /peerDependencyRules|allowedVersions|ignoreMissing/);
});

test('desktop and mobile Angular applications have isolated outputs and PWA scopes', async () => {
  const workspace = JSON.parse(await read('angular.json'));
  const desktop = workspace.projects['modern-desktop'].architect.build.options;
  const mobile = workspace.projects['modern-mobile'].architect.build.options;
  assert.equal(desktop.outputPath.base, 'dist-modern/modern-desktop');
  assert.equal(mobile.outputPath.base, 'dist-modern/modern-mobile');
  assert.equal(desktop.baseHref, '/modern-desktop/');
  assert.equal(mobile.baseHref, '/modern-mobile/');
  assert.notEqual(desktop.serviceWorker, mobile.serviceWorker);
  assert.ok(desktop.styles.some(path => path.includes('bulma')));
  assert.ok(mobile.styles.some(path => path.includes('@ionic/angular/css/core.css')));
  const manifests = await Promise.all(['modern-desktop', 'modern-mobile'].map(async directory =>
    JSON.parse(await read(`projects/${directory}/public/manifest.webmanifest`))));
  assert.deepEqual(manifests.map(value => value.scope), ['/modern-desktop/', '/modern-mobile/']);
  assert.ok(manifests.every(value => !/[?&](?:fresh|cache|v)=/i.test(value.start_url)));
});

test('API core preserves replay, idempotency, cookie and resource identity behavior', async () => {
  const source = await read('projects/modern-shared/src/lib/api-core.ts');
  const api = transpileCommonJs(source);

  assert.equal(api.isSafeMethod('get'), true);
  assert.equal(api.canReplay('POST', null), false);
  assert.equal(api.canReplay('POST', 'idem-123'), true);
  assert.equal(api.validateIdempotencyKey('  idem-123  '), 'idem-123');
  assert.throws(() => api.validateIdempotencyKey('bad\nkey'), error => error.code === 'IDEMPOTENCY_KEY_INVALID');
  assert.equal(api.readCookie('first=1; zumbo-csrf=a%2Bb', 'zumbo-csrf'), 'a+b');
  assert.deepEqual(api.resourceIdentity('/api/work-items/abc'), { kind: 'work-items', id: 'abc' });
  assert.deepEqual(api.resourceIdentity('/api/sprints/s1/items/w1'), { kind: 'work-items', id: 'w1' });
  const conflict = api.normalizeApiError({ status: 409, error: { error: { code: 'CONCURRENCY_CONFLICT' } } });
  assert.equal(conflict.status, 409);
  assert.equal(conflict.code, 'CONCURRENCY_CONFLICT');
});

test('typed project catalog core matches the accepted legacy calculations', async () => {
  const legacy = require(resolve(root, 'shared/project-catalog-core.js'));
  const modern = transpileCommonJs(await read('projects/modern-shared/src/lib/project-catalog-core.ts'));
  const roles = [
    { name: 'ProjectOwner', permissions: ['BoardManage'], isProtected: true },
    { name: 'Developer', permissions: ['WorkItemUpdate'], isProtected: false }
  ];
  const project = {
    members: [{ userId: 'u1', role: 'ProjectOwner' }],
    templates: [{ id: 't1', archived: false }, { id: 't2', archived: true }],
    components: [{ id: 'c1', archived: false }],
    versions: [{ id: 'v1', name: '2.0', status: 'Planned' }],
    releases: [{ id: 'r1' }],
    milestones: [
      { id: 'm2', status: 'Completed', dueAt: '2026-09-02T00:00:00Z' },
      { id: 'm1', status: 'Open', dueAt: '2026-08-01T00:00:00Z' }
    ]
  };
  const entries = [{ action: 'ProjectVersionCreated' }, { action: 'WorkItemUpdated' }];

  assert.deepEqual(modern.projectCatalogLimits, legacy.limits);
  assert.equal(modern.projectRoleOf(project, 'u1'), legacy.roleOf(project, 'u1'));
  assert.equal(modern.canManageProjectCatalog('ProjectOwner', roles), legacy.canManage('ProjectOwner', roles));
  assert.equal(modern.canReleaseProjectCatalog('ProjectOwner', roles), legacy.canRelease('ProjectOwner', roles));
  assert.deepEqual(modern.normalizeProjectComponentNames('API, Web\napi'), legacy.normalizeComponentNames('API, Web\napi'));
  assert.equal(modern.toProjectCatalogDate('2026-08-11')?.getTime(), legacy.toDateInput('2026-08-11')?.getTime());
  assert.equal(modern.projectVersionName(project, 'v1'), legacy.versionName(project, 'v1'));
  assert.deepEqual(modern.projectCatalogSnapshot(project), legacy.snapshot(project));
  assert.deepEqual(modern.projectCatalogAuditEntries(entries), legacy.auditEntries(entries));
  for (const code of ['PROJECT_TEMPLATE_EXISTS', 'CONCURRENCY_CONFLICT', 'FORBIDDEN', 'VALIDATION_ERROR']) {
    assert.equal(modern.projectCatalogErrorMessage({ code }, 'fallback'), legacy.errorMessage({ code }, 'fallback'));
  }
});

test('typed session, interceptor, client and realtime adapters retain required transport contracts', async () => {
  const session = await read('projects/modern-shared/src/lib/session.service.ts');
  const interceptor = await read('projects/modern-shared/src/lib/api.interceptor.ts');
  const client = await read('projects/modern-shared/src/lib/api-client.service.ts');
  const realtime = await read('projects/modern-shared/src/lib/realtime.service.ts');

  assert.match(session, /refreshPromise: Promise<AuthResponse> \| null/);
  assert.match(session, /withCredentials: true/g);
  assert.match(session, /X-CSRF-Token/);
  assert.match(session, /zumbo\.modern\.currentUser/);
  assert.doesNotMatch(session, /['"]zumbo\.currentUser['"]/);
  assert.match(interceptor, /canReplay\(request\.method, idempotencyKey\)/);
  assert.match(interceptor, /Idempotency-Key/);
  assert.match(interceptor, /If-Match/);
  assert.match(client, /responseType: 'blob'/);
  assert.match(client, /new FormData\(\)/);
  assert.match(client, /AbortSignal/);
  assert.match(client, /normalized\.status === 409/);
  assert.match(realtime, /withStatefulReconnect/);
  assert.match(realtime, /withAutomaticReconnect/);
  assert.match(realtime, /version-gap/);
  assert.match(realtime, /SubscribeProject/);
});

test('hardened modern output has verified integrity and per-response CSP nonces', async () => {
  const manifest = await verifyModernAssetManifest();
  assert.equal(manifest.schemaVersion, 1);
  assert.deepEqual(manifest.surfaces.map(surface => surface.scope), ['/modern-desktop/', '/modern-mobile/']);
  assert.ok(manifest.assets.every(asset => asset.bytes > 0 && /^[a-f0-9]{64}$/.test(asset.sha256)));

  const server = await startStaticServer(resolve(root, 'dist-modern'));
  try {
    const response = await fetch(`${server.origin}/modern-desktop/workspace/project-1`);
    assert.equal(response.status, 200);
    const html = await response.text();
    const policy = response.headers.get('content-security-policy') || '';
    const nonce = html.match(/ngcspnonce="([^"]+)"/i)?.[1];
    assert.ok(nonce);
    assert.notEqual(nonce, '__ZUMBO_CSP_NONCE__');
    assert.match(html, new RegExp(`<meta name="csp-nonce" content="${nonce}">`, 'i'));
    assert.match(policy, new RegExp(`'nonce-${nonce}'`));
    assert.doesNotMatch(policy, /unsafe-inline|unsafe-eval|\*/i);
    assert.doesNotMatch(html, /__ZUMBO_CSP_NONCE__|[?&](?:fresh|cache|v)=/i);
  } finally {
    await server.close();
  }
});

function read(path) {
  return readFile(resolve(root, path), 'utf8');
}

function transpileCommonJs(source) {
  const output = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText;
  const module = { exports: {} };
  Function('exports', 'module', output)(module.exports, module);
  return module.exports;
}
