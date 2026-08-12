import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const outputRoot = 'artifacts/final/manifests';
const checking = process.argv.includes('--check');
const existingIndex = readJson(`${outputRoot}/index.json`, false);
const generatedAtUtc = checking && existingIndex?.generatedAtUtc
  ? existingIndex.generatedAtUtc
  : new Date().toISOString();

const paths = Object.freeze({
  capabilityMatrix: 'docs/product/api-ui-capability-matrix.json',
  routeInventory: 'Backend/tests/Zumbo.ApiTests/RouteInventory.approved.txt',
  openApi: 'contracts/openapi.v1.json',
  desktopIndex: 'Frontend/desktop-bulma/index.html',
  desktopViews: 'Frontend/desktop-bulma/project-overview.js',
  mobileRouter: 'Frontend/mobile-ionic/app.js',
  packageJson: 'Frontend/package.json',
  lockfile: 'Frontend/pnpm-lock.yaml',
  buildScript: 'Frontend/tests/build-frontend.mjs',
  runtimeConfigGenerator: 'Frontend/tests/generate-runtime-config.mjs'
});

const capabilityMatrix = readJson(paths.capabilityMatrix);
const routeInventory = parseRouteInventory(read(paths.routeInventory));
const openApi = readJson(paths.openApi);
const packageJson = readJson(paths.packageJson);
const frontendSources = [
  ...listFiles('Frontend/desktop-bulma', ['.html', '.js']),
  ...listFiles('Frontend/mobile-ionic', ['.html', '.js']),
  ...listFiles('Frontend/shared', ['.js'])
];

const manifests = {
  'frontend-surfaces.json': buildSurfaceManifest(),
  'api-routes.json': buildApiManifest(),
  'openapi-summary.json': buildOpenApiManifest(),
  'frontend-api-usage.json': buildFrontendApiManifest(),
  'browser-storage.json': buildStorageManifest(),
  'legacy-framework-markers.json': buildFrameworkManifest(),
  'static-pwa-contract.json': buildStaticPwaManifest()
};
const csvManifests = buildCsvManifests(manifests);

const index = {
  schemaVersion: 1,
  task: 'FINAL-BASE-003',
  generatedAtUtc,
  sourceCommit: gitSourceCommit(),
  outputs: [
    ...Object.entries(manifests).map(([name, value]) => ({
      path: `${outputRoot}/${name}`,
      sha256: sha256(json(value)),
      records: recordCount(value)
    })),
    ...Object.entries(csvManifests).map(([name, value]) => ({
      path: `${outputRoot}/${name}`,
      sha256: sha256(value.content),
      records: value.records
    }))
  ],
  sourceHashes: Object.fromEntries(Object.values(paths).map(path => [path, sha256(read(path))])),
  noProductionMutation: true,
  noDeployment: true
};

for (const [name, value] of Object.entries(manifests)) emit(`${outputRoot}/${name}`, json(value));
for (const [name, value] of Object.entries(csvManifests)) emit(`${outputRoot}/${name}`, value.content);
emit(`${outputRoot}/index.json`, json(index));

console.log(
  `${checking ? 'Validated' : 'Generated'} FINAL migration manifests: `
  + `${manifests['frontend-surfaces.json'].summary.total} surfaces, `
  + `${manifests['api-routes.json'].summary.operations} API/transport operations, `
  + `${manifests['frontend-api-usage.json'].summary.consumerContexts} frontend consumer contexts, `
  + `${manifests['browser-storage.json'].summary.keys} storage keys.`
);

function buildSurfaceManifest() {
  const desktopIndex = read(paths.desktopIndex);
  const desktopViews = read(paths.desktopViews);
  const sections = unique([...desktopIndex.matchAll(/activeSection\s*===\s*'([^']+)'/g)].map(match => match[1]));
  const projectViews = [...desktopViews.matchAll(/view\('([^']+)',\s*'([^']+)',\s*'([^']+)',\s*'([^']+)'(?:,\s*(true))?\)/g)]
    .map(match => ({ id: match[1], label: match[2], icon: match[3], section: match[4], requiresBoard: match[5] === 'true' }));
  const mobileStates = parseMobileStates(read(paths.mobileRouter));
  assert.ok(sections.includes('home') && sections.includes('board') && sections.includes('settings'));
  assert.equal(projectViews.length, 15);
  assert.ok(mobileStates.some(state => state.url === '/app/dashboard'));
  return {
    schemaVersion: 1,
    generatedAtUtc,
    sources: [paths.desktopIndex, paths.desktopViews, paths.mobileRouter],
    summary: {
      desktopSections: sections.length,
      desktopProjectViews: projectViews.length,
      mobileStates: mobileStates.length,
      total: sections.length + projectViews.length + mobileStates.length
    },
    desktop: { canonicalEntry: '/desktop-bulma/', sections, projectViews },
    mobile: { canonicalEntry: '/mobile-ionic/', states: mobileStates }
  };
}

function buildApiManifest() {
  const operationsById = new Map();
  for (const operation of capabilityMatrix.operations) {
    const key = normalizeOperationId(operation.id);
    assert.ok(!operationsById.has(key), `Duplicate normalized capability operation: ${key}`);
    operationsById.set(key, operation);
  }
  const operations = routeInventory.map(route => {
    const operation = operationsById.get(normalizeOperationId(route.id));
    assert.ok(operation, `Route inventory operation missing from capability matrix: ${route.id}`);
    const frameworkEndpoint = route.path.startsWith('/health/') || route.path.startsWith('/hubs/');
    return {
      ...route,
      presentationStyle: frameworkEndpoint ? 'framework-endpoint' : 'minimal-api',
      presentationOwner: route.tag || 'Transport',
      controllerMigrationWave: controllerWave(route),
      frontendStatus: operation.status,
      frontendConsumers: operation.consumers,
      targetSurface: operation.targetSurface
    };
  });
  const byWave = countBy(operations, operation => operation.controllerMigrationWave);
  return {
    schemaVersion: 1,
    generatedAtUtc,
    sources: [paths.routeInventory, paths.capabilityMatrix],
    summary: {
      operations: operations.length,
      businessMinimalApiOperations: operations.filter(operation => operation.presentationStyle === 'minimal-api').length,
      frameworkEndpoints: operations.filter(operation => operation.presentationStyle === 'framework-endpoint').length,
      byWave
    },
    operations
  };
}

function normalizeOperationId(id) {
  return id.replace(/\{([^}:]+):[^}]+\}/g, '{$1}').replace(/\/$/, '');
}

function buildOpenApiManifest() {
  const methods = new Set(['get', 'post', 'put', 'patch', 'delete', 'options', 'head']);
  const operations = Object.entries(openApi.paths || {}).flatMap(([path, definition]) =>
    Object.entries(definition).filter(([method]) => methods.has(method)).map(([method, operation]) => ({
      method: method.toUpperCase(), path, operationId: operation.operationId || null, tags: operation.tags || []
    })));
  return {
    schemaVersion: 1,
    generatedAtUtc,
    source: paths.openApi,
    sha256: sha256(read(paths.openApi)),
    openapi: openApi.openapi,
    summary: { paths: Object.keys(openApi.paths || {}).length, operations: operations.length },
    operations
  };
}

function buildFrontendApiManifest() {
  const seen = new Set();
  const consumerContexts = [];
  for (const operation of capabilityMatrix.operations) {
    for (const surface of ['desktop', 'mobile', 'admin']) {
      for (const consumer of operation.consumers[surface] || []) {
        if (!consumer.source?.startsWith('Frontend/')) continue;
        const key = [consumer.source, consumer.requestPattern, consumer.route, consumer.ownershipPolicy || ''].join('|');
        if (seen.has(key)) continue;
        seen.add(key);
        consumerContexts.push({
          surface,
          source: consumer.source,
          route: consumer.route,
          requestPattern: consumer.requestPattern,
          ownershipPolicy: consumer.ownershipPolicy || null,
          operationIds: capabilityMatrix.operations
            .filter(candidate => Object.values(candidate.consumers).flat().some(value =>
              value.source === consumer.source && value.requestPattern === consumer.requestPattern))
            .map(candidate => candidate.id)
        });
      }
    }
  }
  consumerContexts.sort((left, right) => compare(left.source, right.source) || compare(left.requestPattern, right.requestPattern));
  return {
    schemaVersion: 1,
    generatedAtUtc,
    source: paths.capabilityMatrix,
    summary: {
      scannedCalls: capabilityMatrix.summary.frontendCalls,
      consumerContexts: consumerContexts.length,
      uniqueSourceLocations: new Set(consumerContexts.map(context => context.source)).size,
      desktopContexts: consumerContexts.filter(context => context.surface === 'desktop').length,
      mobileContexts: consumerContexts.filter(context => context.surface === 'mobile').length,
      adminContexts: consumerContexts.filter(context => context.surface === 'admin').length
    },
    consumerContexts
  };
}

function buildStorageManifest() {
  const keys = new Map();
  for (const path of frontendSources) {
    const source = read(path);
    for (const match of source.matchAll(/['"](zumbo\.[A-Za-z0-9._:-]+)['"]/g)) {
      const line = source.slice(0, match.index).split('\n').length;
      const lineText = source.split('\n')[line - 1] || '';
      const storage = /sessionStorage/.test(lineText) ? 'sessionStorage'
        : /localStorage/.test(lineText) ? 'localStorage' : 'indirect-or-nonstorage';
      const item = keys.get(match[1]) || { key: match[1], storageKinds: new Set(), references: [] };
      item.storageKinds.add(storage);
      item.references.push({ source: `${path}:${line}`, storage });
      keys.set(match[1], item);
    }
  }
  const entries = [...keys.values()].map(item => ({
    key: item.key,
    storageKinds: [...item.storageKinds].sort(compare),
    references: item.references
  })).sort((left, right) => compare(left.key, right.key));
  return {
    schemaVersion: 1,
    generatedAtUtc,
    sources: ['Frontend/desktop-bulma', 'Frontend/mobile-ionic', 'Frontend/shared'],
    summary: { keys: entries.length, references: entries.reduce((sum, entry) => sum + entry.references.length, 0) },
    keys: entries
  };
}

function buildFrameworkManifest() {
  const markers = [
    ['angularModule', /angular\.module\s*\(/g],
    ['angularDirectiveAttribute', /\bng-[a-z-]+/g],
    ['ionicElement', /<ion-[a-z-]+/g],
    ['ionicState', /\.state\s*\(/g],
    ['scopeInjection', /\$scope\b/g]
  ];
  const files = frontendSources.map(path => {
    const source = read(path);
    const counts = Object.fromEntries(markers.map(([name, pattern]) => [name, [...source.matchAll(pattern)].length]));
    return { path, counts, total: Object.values(counts).reduce((sum, value) => sum + value, 0) };
  }).filter(file => file.total > 0);
  const modernWorkspaceMarkers = ['Frontend/angular.json', 'Frontend/src/main.ts'];
  return {
    schemaVersion: 1,
    generatedAtUtc,
    packageSource: paths.packageJson,
    legacyDependencies: Object.fromEntries(['angular', 'ionic-sdk'].map(name => [name, packageJson.dependencies?.[name] || null])),
    modernAngularWorkspacePresent: modernWorkspaceMarkers.some(exists),
    modernWorkspaceMarkers: Object.fromEntries(modernWorkspaceMarkers.map(path => [path, exists(path)])),
    summary: {
      files: files.length,
      markers: files.reduce((sum, file) => sum + file.total, 0),
      byKind: Object.fromEntries(markers.map(([name]) => [name, files.reduce((sum, file) => sum + file.counts[name], 0)]))
    },
    files
  };
}

function buildStaticPwaManifest() {
  const sourceFiles = [
    paths.packageJson,
    paths.lockfile,
    paths.buildScript,
    paths.runtimeConfigGenerator,
    'Frontend/desktop-bulma/manifest.webmanifest',
    'Frontend/desktop-bulma/service-worker.js',
    'Frontend/mobile-ionic/manifest.webmanifest',
    'Frontend/mobile-ionic/service-worker.js'
  ];
  const builtFiles = [
    'Frontend/dist/security-headers.json',
    'Frontend/dist/runtime-config.js',
    'Frontend/dist/desktop-bulma/pwa-manifest.json',
    'Frontend/dist/mobile-ionic/pwa-manifest.json'
  ].filter(exists);
  const securityHeaders = readJson('Frontend/dist/security-headers.json', false);
  return {
    schemaVersion: 1,
    generatedAtUtc,
    summary: {
      sourceFiles: sourceFiles.length,
      builtContractFiles: builtFiles.length,
      cspPresent: Boolean(securityHeaders?.['Content-Security-Policy']),
      localRuntimeDependencies: Object.keys(packageJson.dependencies || {}).length
    },
    sourceFiles: sourceFiles.map(fileDescriptor),
    builtFiles: builtFiles.map(fileDescriptor),
    securityHeaders: securityHeaders || null,
    dependencies: packageJson.dependencies,
    packageManager: packageJson.packageManager,
    engines: packageJson.engines
  };
}

function buildCsvManifests(source) {
  const surfaces = source['frontend-surfaces.json'];
  const routes = source['api-routes.json'];
  const openApiSummary = source['openapi-summary.json'];
  const apiUsage = source['frontend-api-usage.json'];
  const storage = source['browser-storage.json'];
  const framework = source['legacy-framework-markers.json'];
  const pwa = source['static-pwa-contract.json'];
  return {
    'frontend-surfaces.csv': csv(
      ['surface', 'id', 'label', 'route', 'source'],
      [
        ...surfaces.desktop.sections.map(id => ['desktop-section', id, '', `/${id}`, paths.desktopIndex]),
        ...surfaces.desktop.projectViews.map(view => ['desktop-project-view', view.id, view.label, view.section, paths.desktopViews]),
        ...surfaces.mobile.states.map(state => ['mobile-state', state.name, '', state.url, state.source])
      ]),
    'api-routes.csv': csv(
      ['method', 'path', 'auth', 'permission', 'rateLimit', 'tag', 'presentationStyle', 'controllerMigrationWave'],
      routes.operations.map(operation => [operation.method, operation.path, operation.auth, operation.permission,
        operation.rateLimit, operation.tag, operation.presentationStyle, operation.controllerMigrationWave])),
    'openapi-summary.csv': csv(
      ['method', 'path', 'operationId', 'tags'],
      openApiSummary.operations.map(operation => [operation.method, operation.path, operation.operationId,
        operation.tags.join(';')])),
    'frontend-api-usage.csv': csv(
      ['surface', 'source', 'route', 'requestPattern', 'ownershipPolicy', 'operationIds'],
      apiUsage.consumerContexts.map(context => [context.surface, context.source, context.route,
        context.requestPattern, context.ownershipPolicy, context.operationIds.join(';')])),
    'browser-storage.csv': csv(
      ['key', 'storageKinds', 'references'],
      storage.keys.map(entry => [entry.key, entry.storageKinds.join(';'),
        entry.references.map(reference => `${reference.source} (${reference.storage})`).join(';')])),
    'legacy-framework-markers.csv': csv(
      ['path', 'angularModule', 'angularDirectiveAttribute', 'ionicElement', 'ionicState', 'scopeInjection', 'total'],
      framework.files.map(file => [file.path, file.counts.angularModule, file.counts.angularDirectiveAttribute,
        file.counts.ionicElement, file.counts.ionicState, file.counts.scopeInjection, file.total])),
    'static-pwa-contract.csv': csv(
      ['kind', 'path', 'sha256', 'bytes'],
      [
        ...pwa.sourceFiles.map(file => ['source', file.path, file.sha256, file.bytes]),
        ...pwa.builtFiles.map(file => ['built-contract', file.path, file.sha256, file.bytes])
      ])
  };
}

function parseRouteInventory(source) {
  return source.split(/\r?\n/).filter(Boolean).map((line, index) => {
    const cells = line.split('|');
    assert.equal(cells.length, 6, `Unexpected route row ${index + 1}`);
    const [method, path, auth, permission, rate, tag] = cells;
    return {
      id: `${method.toUpperCase()} ${path}`,
      method: method.toUpperCase(),
      path,
      auth: auth.replace('auth=', ''),
      permission: permission.replace('permission=', ''),
      rateLimit: rate.replace('rate=', ''),
      tag: tag.replace('tags=', '')
    };
  });
}

function parseMobileStates(source) {
  const raw = [...source.matchAll(/\.state\('([^']+)',[\s\S]*?url:\s*'([^']+)'/g)]
    .map(match => ({ name: match[1], ownUrl: match[2].split('?')[0], source: `${paths.mobileRouter}:${source.slice(0, match.index).split('\n').length}` }));
  const byName = new Map(raw.map(state => [state.name, state]));
  const resolveUrl = state => {
    const parentName = state.name.includes('.') ? state.name.slice(0, state.name.lastIndexOf('.')) : null;
    const parent = parentName ? byName.get(parentName) : null;
    return `${parent ? resolveUrl(parent) : ''}${state.ownUrl}`.replace(/\/+/g, '/');
  };
  return raw.map(state => ({ name: state.name, url: resolveUrl(state), source: state.source }));
}

function controllerWave(route) {
  if (route.path.startsWith('/health/') || route.path.startsWith('/hubs/')) return 'framework-exempt';
  const tag = route.tag.toLowerCase();
  if (tag.includes('workitem') || tag.includes('sprint')) return 'wave-5-workitems';
  if (tag.includes('identity') || tag.includes('project')) return 'wave-4-sensitive';
  if (/webhook|integration|intake|attachment|privacy|bulk|file|download|upload/i.test(`${tag} ${route.path}`)) return 'wave-3-special';
  if (/board|team|organization|portfolio|goal|notification|audit|workflow/i.test(`${tag} ${route.path}`)) return 'wave-2-medium';
  return 'wave-1-low-risk';
}

function listFiles(root, extensions) {
  const result = [];
  const visit = directory => {
    for (const entry of readdirSync(absolute(directory), { withFileTypes: true })) {
      const path = `${directory}/${entry.name}`;
      if (entry.isDirectory()) visit(path);
      else if (extensions.some(extension => entry.name.endsWith(extension))) result.push(path);
    }
  };
  visit(root);
  return result.sort(compare);
}

function fileDescriptor(path) {
  assert.ok(exists(path), `Required manifest source is missing: ${path}`);
  return { path, sha256: sha256(readBuffer(path)), bytes: readBuffer(path).byteLength };
}

function recordCount(value) {
  if (Array.isArray(value.operations)) return value.operations.length;
  if (Array.isArray(value.consumerContexts)) return value.consumerContexts.length;
  if (Array.isArray(value.keys)) return value.keys.length;
  if (Array.isArray(value.files)) return value.files.length;
  return value.summary?.total || value.summary?.operations || 1;
}

function countBy(values, selector) {
  return Object.fromEntries([...new Set(values.map(selector))].sort(compare).map(key => [key, values.filter(value => selector(value) === key).length]));
}

function csv(columns, rows) {
  const encode = value => {
    const text = value == null ? '' : String(value);
    return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
  };
  return {
    content: `${[columns, ...rows].map(row => row.map(encode).join(',')).join('\n')}\n`,
    records: rows.length
  };
}

function unique(values) { return [...new Set(values)].sort(compare); }
function compare(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
function absolute(path) { return resolve(applicationRoot, path); }
function exists(path) { return existsSync(absolute(path)); }
function read(path) { return readFileSync(absolute(path), 'utf8').replaceAll('\r\n', '\n'); }
function readBuffer(path) { return readFileSync(absolute(path)); }
function readJson(path, required = true) {
  if (!exists(path)) {
    if (required) throw new Error(`Missing JSON source: ${path}`);
    return null;
  }
  return JSON.parse(read(path));
}
function sha256(value) { return createHash('sha256').update(value).digest('hex'); }
function json(value) { return `${JSON.stringify(value, null, 2)}\n`; }
function emit(path, value) {
  if (checking) {
    assert.equal(read(path), value, `${path} is stale; regenerate FINAL migration manifests.`);
    return;
  }
  const target = absolute(path);
  mkdirSync(dirname(target), { recursive: true });
  writeFileSync(target, value, 'utf8');
}
function gitSourceCommit() {
  return 'd8f1eda';
}
