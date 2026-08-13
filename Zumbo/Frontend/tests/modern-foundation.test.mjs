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
  assert.equal(packageJson.engines.node, '>=20.9.0 <21 || >=22.22.3 <23 || >=24.15.0 <25');
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

test('production frontend has no AngularJS or Ionic 1 dependency, source root or default command', async () => {
  const packageJson = JSON.parse(await read('package.json'));
  const lockfile = await read('pnpm-lock.yaml');
  assert.equal(packageJson.dependencies.angular, undefined);
  assert.equal(packageJson.dependencies['ionic-sdk'], undefined);
  assert.equal(packageJson.scripts.build, packageJson.scripts['build:modern']);
  assert.equal(packageJson.scripts.preview, 'node tests/static-server.mjs dist-modern --canonical');
  assert.equal(packageJson.scripts.unit, 'node tests/run-modern-tests.mjs');
  assert.doesNotMatch(lockfile, /^\s{2}(?:angular@1\.8\.3|ionic-sdk@1\.3\.2):/m);
  for (const path of [
    'desktop-bulma/index.html',
    'mobile-ionic/index.html',
    'vendor/angular/angular.min.js',
    'vendor/ionic/ionic.bundle.min.js'
  ]) {
    await assert.rejects(read(path), { code: 'ENOENT' });
  }
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

  for (const surface of ['modern-desktop', 'modern-mobile']) {
    const config = JSON.parse(await read(`projects/${surface}/ngsw-config.json`));
    const assetFiles = config.assetGroups.flatMap(group => group.resources.files ?? []);
    const assetUrls = config.assetGroups.flatMap(group => group.resources.urls ?? []);
    assert.ok(!assetFiles.includes('/index.html'));
    assert.ok(assetFiles.includes('!/runtime-config.js'));
    assert.deepEqual(assetUrls, [`/${surface}/index.html`]);
    assert.deepEqual(config.dataGroups, [{
      name: `${surface === 'modern-desktop' ? 'desktop' : 'mobile'}-runtime-config`,
      urls: [`/${surface}/runtime-config.js`],
      cacheConfig: { strategy: 'freshness', maxSize: 1, maxAge: '1d', timeout: '2s' }
    }]);
    assert.doesNotMatch(JSON.stringify(config.dataGroups), /\/api|\/hubs|browser-auth/i);
  }
});

test('modern roots expose a user-controlled service-worker update path', async () => {
  const updateService = await read('projects/modern-shared/src/lib/pwa-update.service.ts');
  assert.match(updateService, /event\.type === 'VERSION_READY'/);
  assert.match(updateService, /await this\.updates\.activateUpdate\(\)/);
  assert.match(updateService, /location\.reload\(\)/);
  for (const surface of ['modern-desktop', 'modern-mobile']) {
    const root = await read(`projects/${surface}/src/app/app.ts`);
    const template = await read(`projects/${surface}/src/app/app.html`);
    assert.match(root, /inject\(PwaUpdateService\)/);
    assert.match(template, /pwaUpdate\.ready\(\)/);
    assert.match(template, /'Güncelle'/);
  }
});

test('service-worker registration uses one fixed local TrustedScriptURL policy', async () => {
  const policy = await read('projects/modern-shared/src/lib/trusted-service-worker.ts');
  assert.match(policy, /createPolicy\('zumbo#service-worker'/);
  assert.match(policy, /value !== 'ngsw-worker\.js'/);
  assert.doesNotMatch(policy, /unsafe|bypass/i);
  for (const surface of ['modern-desktop', 'modern-mobile']) {
    const config = await read(`projects/${surface}/src/app/app.config.ts`);
    assert.match(config, /provideServiceWorker\(trustedServiceWorkerScript\(\)/);
  }
});

test('Ionicons TrustedHTML fallback accepts only sanitized SVG input', async () => {
  const source = await read('projects/modern-mobile/src/app/trusted-ionicons.ts');
  const policy = transpileCommonJs(source);
  assert.match(source, /createPolicy\('default'/);
  assert.match(source, /script\|foreignObject\|iframe\|object\|embed/);
  assert.match(source, /javascript/);
  assert.match(source, /installIoniconsTrustedTypesPolicy\(\)/);
  const main = await read('projects/modern-mobile/src/main.ts');
  assert.ok(main.indexOf('installIoniconsTrustedTypesPolicy();') < main.indexOf('bootstrapApplication('));
  assert.equal(typeof policy.installIoniconsTrustedTypesPolicy, 'function');
});

test('legacy mobile hashes map to complete modern routes without cache-buster state', async () => {
  const source = await read('projects/modern-mobile/src/app/legacy-mobile-route.ts');
  const adapter = transpileCommonJs(source);
  const cases = {
    '#/app/dashboard': '/workspace/home',
    '#/app/tasks': '/workspace/work',
    '#/app/notifications': '/workspace/inbox',
    '#/app/profile': '/workspace/account',
    '#/projects/project-1': '/projects/project-1',
    '#/projects/project-1/insights?mode=reports&range=90': '/projects/project-1/insights?mode=reports&range=90',
    '#/tasks/task-1': '/tasks/task-1',
    '#/profile/operations': '/profile/operations',
    '#/knowledge': '/workspace/knowledge'
  };
  for (const [legacy, modern] of Object.entries(cases)) assert.equal(adapter.legacyMobilePath(legacy), modern);
  assert.equal(adapter.legacyMobilePath(''), null);
  assert.doesNotMatch(JSON.stringify(Object.values(cases)), /[?&](?:fresh|cache|v)=/i);
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
  assert.match(session, /restorePromise/);
  assert.match(interceptor, /!session\.getCsrf\(\)/);
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
    assert.match(policy, /trusted-types angular angular#bundler zumbo#service-worker default/);
    assert.match(policy, /require-trusted-types-for 'script'/);
    assert.doesNotMatch(html, /__ZUMBO_CSP_NONCE__|[?&](?:fresh|cache|v)=/i);

    const runtimeConfig = await fetch(`${server.origin}/modern-desktop/runtime-config.js`);
    assert.equal(runtimeConfig.headers.get('cache-control'), 'no-store');
    const worker = await fetch(`${server.origin}/modern-desktop/ngsw-worker.js`);
    assert.equal(worker.headers.get('cache-control'), 'no-cache');
  } finally {
    await server.close();
  }
});

test('canonical preview redirects only legacy entry documents and remains reversible', async () => {
  const server = await startStaticServer(resolve(root, 'dist-modern'), { canonical: true });
  try {
    for (const [path, location] of [
      ['/', '/modern-desktop/'],
      ['/desktop-bulma/index.html', '/modern-desktop/'],
      ['/mobile-ionic/index.html', '/modern-mobile/']
    ]) {
      const response = await fetch(`${server.origin}${path}`, { redirect: 'manual' });
      assert.equal(response.status, 307);
      assert.equal(response.headers.get('location'), location);
      assert.equal(response.headers.get('cache-control'), 'no-store');
    }
    const modern = await fetch(`${server.origin}/modern-desktop/workspace/home`);
    assert.equal(modern.status, 200);
    const unknownLegacyAsset = await fetch(`${server.origin}/desktop-bulma/app.js`, { redirect: 'manual' });
    assert.equal(unknownLegacyAsset.status, 404);
    for (const [surface, prefix] of [
      ['desktop-bulma', 'zumbo-desktop-shell-'],
      ['mobile-ionic', 'zumbo-mobile-shell-']
    ]) {
      const worker = await fetch(`${server.origin}/${surface}/service-worker.js`);
      assert.equal(worker.status, 200);
      assert.equal(worker.headers.get('cache-control'), 'no-cache, no-store, must-revalidate');
      assert.equal(worker.headers.get('service-worker-allowed'), `/${surface}/`);
      const source = await worker.text();
      assert.match(source, new RegExp(prefix));
      assert.match(source, /registration\.unregister\(\)/);
      assert.doesNotMatch(source, /caches\.keys\(\).*modern|modern-(?:desktop|mobile)/s);
    }
  } finally {
    await server.close();
  }
});

test('local demo lifecycle owns the modern canonical preview and compatibility entries', async () => {
  const start = await read('../scripts/operations/demo-start.mjs');
  const stop = await read('../scripts/operations/demo-stop.mjs');
  assert.match(start, /resolve\(frontendDirectory, 'dist-modern'\)/);
  assert.match(start, /'run', 'build:modern'/);
  assert.match(start, /\['tests\/static-server\.mjs', 'dist-modern', '--canonical'\]/);
  assert.match(start, /\/modern-desktop\//);
  assert.match(start, /\/modern-mobile\//);
  assert.match(start, /legacyDesktopBookmark/);
  assert.match(start, /legacyMobileBookmark/);
  assert.match(stop, /includes\(' dist-modern'\)/);
  assert.match(stop, /includes\('--canonical'\)/);
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
