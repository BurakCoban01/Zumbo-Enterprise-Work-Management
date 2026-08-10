import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';
import {
  matchSpecificity,
  selectMostSpecificOperations
} from '../product/api-consumer-matching.mjs';

const generator = spawnSync(
  process.execPath,
  ['scripts/product/Build-ProductCapabilityMatrix.mjs', '--check'],
  {
    cwd: applicationRoot,
    encoding: 'utf8',
    timeout: 30_000
  });
assert.equal(
  generator.status,
  0,
  generator.stderr || generator.stdout);

const matrix = json('docs/product/api-ui-capability-matrix.json');
const ownership = json('docs/product/api-consumer-ownership.json');
const openApi = json('contracts/openapi.v1.json');
const routeRows = text(
  'Backend/tests/Zumbo.ApiTests/RouteInventory.approved.txt')
  .split(/\r?\n/)
  .filter(Boolean);
const routeIds = new Set(routeRows.map(row => {
  const [method, path] = row.split('|');
  return `${method} ${normalizeRoute(path)}`;
}));
const openApiIds = new Set();
for (const [path, definition] of Object.entries(openApi.paths)) {
  for (const method of ['get', 'post', 'put', 'patch', 'delete']) {
    if (definition[method]) openApiIds.add(`${method.toUpperCase()} ${normalizeRoute(path)}`);
  }
}
const permissionCatalog = new Set(
  [...text(
    'Backend/src/Zumbo.Modules.Identity.Contracts/Security/PermissionCatalog.cs')
    .matchAll(/public const string \w+ = "([^"]+)";/g)]
    .map(match => match[1]));
const ownershipById = new Map(
  ownership.policies.map(policy => [policy.id, policy]));

assert.equal(matrix.schemaVersion, 2);
assert.equal(matrix.task, 'V3-PRODUCT-001');
assert.equal(matrix.driftGate, 'V3-HARDEN-007');
assert.deepEqual(matrix.summary, {
  operations: 324,
  openApiOperations: 320,
  frontendCalls: 548,
  desktopCalls: 309,
  mobileCalls: 239,
  backgroundCapabilities: 9,
  gapCapabilities: 3,
  byStatus: { surfaced: 230, partial: 46, absent: 38, intentional: 10 },
  duplicateOperations: 0,
  unmatchedFrontendCalls: 0,
  unownedOperations: 0,
  ambiguousFrontendCalls: 0,
  explicitMultiOperationCalls: 5
});
assert.deepEqual(
  new Set(matrix.operations.map(operation => operation.id)),
  routeIds);
assert.deepEqual(
  new Set(matrix.operations
    .filter(operation => operation.method !== '*')
    .map(operation => operation.id)),
  openApiIds);

const consumerGroups = new Map();
for (const operation of matrix.operations) {
  assert.deepEqual(
    operationErrors(operation),
    [],
    `${operation.id} has route/permission/test/documentation drift.`);
  for (const surface of ['desktop', 'mobile']) {
    for (const consumer of operation.consumers[surface]) {
      validateUiConsumer(consumer);
      const sourcePath = consumer.source.replace(/:\d+$/, '');
      const key = `${surface}|${sourcePath}|${consumer.requestPattern}`;
      const group = consumerGroups.get(key) ?? [];
      group.push({
        operationId: operation.id,
        ownershipPolicy: consumer.ownershipPolicy
      });
      consumerGroups.set(key, group);
    }
  }
}

const usedOwnership = new Set();
for (const [key, references] of consumerGroups) {
  const operationIds = [...new Set(references.map(reference => reference.operationId))];
  if (operationIds.length <= 1) continue;
  const policyIds = [...new Set(
    references.map(reference => reference.ownershipPolicy).filter(Boolean))];
  assert.equal(
    policyIds.length,
    1,
    `Multi-operation frontend call lacks one explicit ownership policy: ${key}`);
  const policy = ownershipById.get(policyIds[0]);
  assert.ok(policy, `Unknown consumer ownership policy: ${policyIds[0]}`);
  assert.deepEqual(
    operationIds.sort(compareCodePoints),
    [...policy.operationIds].sort(compareCodePoints),
    `Consumer ownership operations drifted: ${policy.id}`);
  usedOwnership.add(policy.id);
}
assert.deepEqual(
  [...usedOwnership].sort(compareCodePoints),
  [...ownershipById.keys()].sort(compareCodePoints));

assertFalsePositiveFixtures();
assertNegativeDriftFixtures();

const templateUpdate = operation('PUT /api/work-items/templates/{templateId}');
assert.deepEqual(
  templateUpdate.consumers.desktop.map(consumer => consumer.source),
  ['Frontend/desktop-bulma/work-automation.js:403']);
assert.ok(
  !templateUpdate.consumers.desktop.some(consumer =>
    consumer.source === 'Frontend/desktop-bulma/work-items.js:408'));
assert.equal(
  operation('POST /api/organizations/{organizationId}/restore').status,
  'absent');
assert.ok(
  !operation('POST /api/organizations/{organizationId}/restore')
    .consumers.desktop.some(consumer =>
      consumer.source === 'Frontend/desktop-bulma/task-board.js:407'));

console.log(
  'V3-HARDEN-007 drift gate passed: 324 operations, 320 OpenAPI contracts, '
  + '548 frontend calls, 5 explicit multi-operation policies and negative fixtures.');

function operationErrors(operation) {
  const errors = [];
  if (!routeIds.has(operation.id)) errors.push('route');
  if (operation.method !== '*' && !openApiIds.has(operation.id)) {
    errors.push('openapi');
  }
  if (operation.permission !== 'none') {
    const match = operation.permission.match(/^(.+):(global|resource)$/);
    if (!match || !permissionCatalog.has(match[1])) errors.push('permission');
  }
  if (!operation.test || !exists(operation.test)) errors.push('test');
  if (!Array.isArray(operation.documentation)
      || operation.documentation.length === 0
      || !operation.documentation.every(exists)) {
    errors.push('documentation');
  }
  const uiCount =
    (operation.consumers?.desktop?.length ?? 0)
    + (operation.consumers?.mobile?.length ?? 0);
  if (['surfaced', 'partial'].includes(operation.status) && uiCount === 0) {
    errors.push('consumer');
  }
  if (operation.status === 'absent' && !operation.targetSurface) {
    errors.push('target');
  }
  if (operation.status === 'intentional') {
    if (!operation.intentionalReason) errors.push('intentional-reason');
    if (!['integration', 'background'].includes(operation.intentionalKind)) {
      errors.push('intentional-kind');
    }
  }
  return errors;
}

function validateUiConsumer(consumer) {
  const match = consumer.source.match(/^(.*):(\d+)$/);
  assert.ok(match, `UI consumer needs a source line: ${consumer.source}`);
  const source = match[1];
  const line = Number(match[2]);
  assert.ok(exists(source), `UI consumer source is missing: ${source}`);
  const sourceLines = text(source).split(/\r?\n/);
  assert.ok(
    line > 0 && line <= sourceLines.length,
    `UI consumer source line is out of range: ${consumer.source}`);
  if (consumer.ownershipPolicy === 'shared-browser-refresh') {
    assert.match(
      sourceLines[line - 1],
      /\/api\/browser-auth\/refresh/,
      `Shared UI consumer line no longer contains its API call: ${consumer.source}`);
  } else {
    assert.match(
      sourceLines[line - 1],
      /apiClient\.(get|post|put|patch|delete|upload|download)/,
      `UI consumer line no longer contains its API call: ${consumer.source}`);
  }
  assert.match(consumer.requestPattern, /^(GET|POST|PUT|PATCH|DELETE) \/api\//);
  if (consumer.ownershipPolicy) {
    assert.ok(
      consumer.ownershipPolicy === 'shared-browser-refresh'
      || ownershipById.has(consumer.ownershipPolicy),
      `UI consumer has unknown ownership: ${consumer.ownershipPolicy}`);
  }
}

function assertFalsePositiveFixtures() {
  const workItemRoutes = [
    route('GET /api/work-items/templates'),
    route('GET /api/work-items/recurrences'),
    route('GET /api/work-items/{id}')
  ];
  assert.deepEqual(
    selectMostSpecificOperations('/api/work-items/templates', workItemRoutes)
      .map(candidate => candidate.id),
    ['GET /api/work-items/templates']);
  assert.deepEqual(
    selectMostSpecificOperations('/api/work-items/{*}', workItemRoutes)
      .map(candidate => candidate.id),
    ['GET /api/work-items/{id}']);
  assert.equal(
    matchSpecificity('/api/work-items/{*}', '/api/projects/{projectId}'),
    null);
  assert.ok(
    matchSpecificity('/api/projects/{*}', '/api/projects/{projectId:required}')
    !== null);

  const ambiguous = selectMostSpecificOperations(
    '/api/work-items/{*}/{*}',
    [
      route('PUT /api/work-items/{id}/watch'),
      route('PUT /api/work-items/{id}/vote')
    ]);
  assert.equal(ambiguous.length, 2);
}

function assertNegativeDriftFixtures() {
  const valid = operation('GET /api/projects/{projectId}');
  assert.deepEqual(operationErrors({
    ...valid,
    permission: 'UnknownPermission:resource'
  }), ['permission']);
  assert.deepEqual(operationErrors({
    ...valid,
    test: 'missing-test.cs'
  }), ['test']);
  assert.deepEqual(operationErrors({
    ...valid,
    documentation: ['missing-document.md']
  }), ['documentation']);
  assert.deepEqual(operationErrors({
    ...valid,
    status: 'surfaced',
    consumers: {
      desktop: [],
      mobile: [],
      admin: [],
      integration: [],
      background: []
    }
  }), ['consumer']);
  const intentional = operation('GET /');
  assert.deepEqual(operationErrors({
    ...intentional,
    intentionalReason: null,
    intentionalKind: null
  }), ['intentional-reason', 'intentional-kind']);
}

function route(id) {
  const space = id.indexOf(' ');
  return {
    id,
    method: id.slice(0, space),
    path: id.slice(space + 1)
  };
}

function operation(id) {
  const value = matrix.operations.find(candidate => candidate.id === id);
  assert.ok(value, `Operation is missing: ${id}`);
  return value;
}

function normalizeRoute(path) {
  const normalized = path.replace(/\{([^}:]+):[^}]+\}/g, '{$1}');
  return normalized.length > 1 ? normalized.replace(/\/$/, '') : normalized;
}

function exists(path) {
  return existsSync(resolve(applicationRoot, path));
}

function json(path) {
  return JSON.parse(text(path));
}

function text(path) {
  return readFileSync(resolve(applicationRoot, path), 'utf8');
}

function compareCodePoints(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}
