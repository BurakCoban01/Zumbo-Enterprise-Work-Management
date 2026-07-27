import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const generator = spawnSync(process.execPath, ['scripts/product/Build-ProductCapabilityMatrix.mjs', '--check'], {
  cwd: applicationRoot,
  encoding: 'utf8',
  timeout: 30_000
});
assert.equal(generator.status, 0, generator.stderr || generator.stdout);

const matrix = json('docs/product/api-ui-capability-matrix.json');
assert.equal(matrix.schemaVersion, 1);
assert.equal(matrix.task, 'V3-PRODUCT-001');
assert.deepEqual(matrix.summary, {
  operations: 238,
  openApiOperations: 234,
  frontendCalls: 372,
  desktopCalls: 220,
  mobileCalls: 152,
  backgroundCapabilities: 8,
  gapCapabilities: 3,
  byStatus: { surfaced: 149, partial: 46, absent: 34, intentional: 9 },
  duplicateOperations: 0,
  unmatchedFrontendCalls: 0,
  unownedOperations: 0
});
assert.equal(new Set(matrix.operations.map(operation => operation.id)).size, matrix.summary.operations);
assert.equal(Object.values(matrix.summary.byStatus).reduce((sum, count) => sum + count, 0), matrix.summary.operations);

for (const operation of matrix.operations) {
  assert.ok(operation.method && operation.path && operation.capability && operation.permission);
  assert.ok(operation.test && operation.documentation.length && operation.source);
  assert.ok(['surfaced', 'partial', 'absent', 'intentional'].includes(operation.status));
  if (operation.status === 'absent') assert.ok(operation.targetSurface, `${operation.id} is unowned.`);
  if (operation.status === 'intentional') {
    assert.ok(operation.intentionalReason, `${operation.id} is missing its non-UI reason.`);
    assert.ok(operation.consumers.integration.length || operation.consumers.background.length, `${operation.id} is missing its non-UI consumer.`);
  }
}

const refresh = operation('POST /api/browser-auth/refresh');
assert.equal(refresh.status, 'surfaced');
assert.equal(refresh.consumers.desktop[0].source, 'Frontend/shared/api-client.js:308');
assert.equal(refresh.consumers.mobile[0].source, 'Frontend/shared/api-client.js:308');
assert.equal(operation('GET /').status, 'intentional');
assert.equal(operation('POST /api/auth/login').consumers.integration.length, 1);
assert.equal(operation('* /hubs/work-items').consumers.background.length, 1);

assert.equal(matrix.backgroundCapabilities.length, 8);
assert.deepEqual(matrix.informationArchitecture.desktop.routes, ['/board', '/projects', '/teams', '/reports', '/audit', '/archive', '/settings']);
assert.deepEqual(matrix.informationArchitecture.desktop.projectViews, [
  'overview', 'board', 'list', 'backlog', 'sprint', 'calendar', 'timeline', 'roadmap', 'catalog', 'automation', 'jobs', 'workload', 'reports'
]);
assert.equal(matrix.informationArchitecture.mobile.routes.length, 15);
assert.ok(matrix.informationArchitecture.mobile.routes.includes('/projects/:projectId/catalog'));
assert.ok(matrix.informationArchitecture.mobile.routes.includes('/projects/:projectId/automation'));
assert.ok(matrix.informationArchitecture.mobile.routes.includes('/projects/:projectId/jobs'));
assert.ok(matrix.informationArchitecture.mobile.routes.includes('/profile/integrations'));
assert.deepEqual(matrix.informationArchitecture.planned.project, []);
assert.deepEqual(matrix.capabilityGaps.map(gap => gap.id), [
  'privacy-jobs',
  'recurring-work',
  'search-operations'
]);
assert.equal(matrix.capabilityGaps[0].score.total, 28);
assert.ok(matrix.capabilityGaps.every(gap => gap.operationIds.length > 0));

const evidence = json('artifacts/v3/V3-PRODUCT-001.json');
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-PRODUCT-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
for (const [name, value] of Object.entries(evidence.sourceHashes)) {
  assert.match(value, /^[a-f0-9]{64}$/, `${name} is not a historical SHA-256 digest.`);
}
assert.equal(evidence.validation.summary.frontendCalls, 183);
assert.equal(evidence.validation.summary.desktopCalls, 132);
assert.equal(evidence.validation.summary.unmatchedFrontendCalls, 0);
assert.equal(evidence.validation.summary.unownedOperations, 0);
assert.equal(evidence.validation.scoredCapabilityGaps.length, 8);

console.log('V3 product matrix passed: 238 operations, 372 frontend calls, 8 background consumers, 3 scored gaps and zero unowned operations.');

function json(path) {
  return JSON.parse(readFileSync(resolve(applicationRoot, path), 'utf8'));
}

function operation(id) {
  const value = matrix.operations.find(candidate => candidate.id === id);
  assert.ok(value, `Operation is missing: ${id}`);
  return value;
}
