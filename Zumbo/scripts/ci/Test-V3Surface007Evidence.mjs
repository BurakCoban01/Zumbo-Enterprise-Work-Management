import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-SURFACE-007.json');
const visual = json('artifacts/v3/V3-SURFACE-007-visual.json');
const deterministic = json('artifacts/ui/v3-surface-007/result.json');
const real = json('artifacts/ui/v3-surface-007-real/result.json');
const matrix = json('docs/product/api-ui-capability-matrix.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-SURFACE-007');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.temporaryApiListenerStopped, true);
assert.equal(evidence.temporaryGatewayListenerStopped, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 159, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 96);
assert.equal(evidence.validation.deterministicBrowser.checks, 7);
assert.equal(evidence.validation.realBackendBrowser.checks, 7);
assert.deepEqual(
  evidence.validation.realBackendBrowser.tenantCleanup,
  { attempted: 1, passed: 1, failed: 0 }
);
assert.deepEqual(evidence.validation.backend.focusedUnit, { passed: 24, failed: 0 });
assert.equal(evidence.validation.backend.unitPassed, 202);
assert.equal(evidence.validation.backend.architecturePassed, 25);
assert.deepEqual(evidence.validation.backend.focusedApi, { passed: 13, failed: 0 });
assert.equal(evidence.validation.backend.apiPassed, 96);
assert.equal(evidence.validation.backend.openApiBaselinePathsPreserved, 198);
assert.equal(evidence.validation.backend.providerProjects.runtimeStatus, 'NeedsRevalidation');
assert.equal(evidence.validation.inAppBrowser.available, false);
assertSourceHashes(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 7);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 7);
assert.deepEqual(real.failures, []);
assert.equal(real.cleanup.failed, 0);
assert.equal(real.cleanup.passed, 1);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-SURFACE-007');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 4);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.criticalBlockers, 0);
  assert.equal(capture.horizontalOverflow, false);
  assert.equal(capture.containsSecretOrRawIdentifier, false);
  assert.equal(capture.containsInternalPayload, false);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

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

for (const id of [
  'GET /api/operations/external-dependencies',
  'GET /api/operations/storage/security',
  'POST /api/operations/storage/security/maintenance',
  'GET /api/work-items/durable-messaging/metrics',
  'GET /api/work-items/durable-messaging/dead-letters',
  'POST /api/work-items/durable-messaging/dead-letter/{messageId}/replay',
  'GET /api/notifications/delivery/status',
  'GET /api/notifications/delivery/dead-letters',
  'POST /api/notifications/delivery/{notificationId}/replay',
  'POST /api/work-items/search/reconcile'
]) {
  const operation = matrix.operations.find(candidate => candidate.id === id);
  assert.ok(operation, `Operation is missing: ${id}`);
  assert.equal(operation.status, 'surfaced', `${id} is not surfaced.`);
  assert.ok(operation.consumers.desktop.length, `${id} has no desktop consumer.`);
  assert.ok(operation.consumers.mobile.length, `${id} has no mobile consumer.`);
}

assert.equal(matrix.capabilityGaps.some(gap => gap.id === 'durable-messaging-operations'), false);

console.log('V3-SURFACE-007 evidence passed: safe operations, 159 frontend tests, 202 unit tests, 25 architecture tests, 96 API tests and four reviewed captures.');

function json(path) {
  return JSON.parse(readFileSync(resolve(applicationRoot, path), 'utf8'));
}

function exists(path) {
  return existsSync(resolve(applicationRoot, path));
}

function fileBytes(path) {
  return readFileSync(resolve(applicationRoot, path)).byteLength;
}

function fileSha(path) {
  return createHash('sha256').update(readFileSync(resolve(applicationRoot, path))).digest('hex');
}

function assertSourceHashes(hashes) {
  for (const [path, hash] of Object.entries(hashes)) {
    assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
  }
}
