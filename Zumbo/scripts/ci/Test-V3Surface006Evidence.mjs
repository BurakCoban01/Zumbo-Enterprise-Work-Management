import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-SURFACE-006.json');
const visual = json('artifacts/v3/V3-SURFACE-006-visual.json');
const deterministic = json('artifacts/ui/v3-surface-006/result.json');
const real = json('artifacts/ui/v3-surface-006-real/result.json');
const matrix = json('docs/product/api-ui-capability-matrix.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-SURFACE-006');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.temporaryApiListenerStopped, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 152, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 91);
assert.equal(evidence.validation.deterministicBrowser.checks, 7);
assert.equal(evidence.validation.realBackendBrowser.checks, 7);
assert.equal(evidence.validation.realBackendBrowser.receiverRequests, 4);
assert.deepEqual(evidence.validation.realBackendBrowser.tenantCleanup, { attempted: 2, passed: 2, failed: 0 });
assert.deepEqual(evidence.validation.backend.focusedUnit, { passed: 6, failed: 0 });
assert.equal(evidence.validation.backend.unitPassed, 201);
assert.equal(evidence.validation.backend.architecturePassed, 25);
assert.deepEqual(evidence.validation.backend.fullApiFirstRun, {
  passed: 93,
  failed: 1,
  diagnostic: 'Existing shared realtime meter listener observed [1,-1,1] instead of [1,-1] at ObservabilityContractTests.cs:70.'
});
assert.deepEqual(evidence.validation.backend.focusedFlakeRerun, { passed: 1, failed: 0 });
assert.deepEqual(evidence.validation.backend.fullApiRerun, { passed: 94, failed: 0 });
assert.equal(evidence.validation.backend.openApiBaselinePathsPreserved, 194);
assert.equal(evidence.validation.backend.testDeliveryContainsWorkItemPayload, false);
assert.equal(evidence.validation.backend.auditTenantResolvedWithoutRequestContext, true);
assert.equal(evidence.validation.visualReview.criticalBlockers, 0);
assert.equal(evidence.validation.visualReview.horizontalOverflow390, false);
assert.equal(evidence.validation.visualReview.targetQueryCaptured, false);
assert.equal(evidence.validation.visualReview.secretOrDeliveryPayloadCaptured, false);
assertSourceHashes(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 7);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 7);
assert.deepEqual(real.failures, []);
assert.equal(real.receiverRequests, 4);
assert.equal(real.cleanup.failed, 0);
assert.equal(real.cleanup.passed, 2);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-SURFACE-006');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 4);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.containsTargetQuery, false);
  assert.equal(capture.containsSecretOrDeliveryPayload, false);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.deepEqual(matrix.summary, {
  operations: 234,
  openApiOperations: 230,
  frontendCalls: 352,
  desktopCalls: 210,
  mobileCalls: 142,
  backgroundCapabilities: 8,
  gapCapabilities: 4,
  byStatus: { surfaced: 139, partial: 46, absent: 40, intentional: 9 },
  duplicateOperations: 0,
  unmatchedFrontendCalls: 0,
  unownedOperations: 0
});
assert.ok(matrix.informationArchitecture.mobile.routes.includes('/profile/integrations'));

for (const id of [
  'GET /api/integrations/webhooks/deliveries/{deliveryId}',
  'GET /api/integrations/webhooks/metrics',
  'GET /api/integrations/webhooks/{id}/deliveries',
  'GET /api/integrations/webhooks/{id}',
  'GET /api/integrations/webhooks',
  'POST /api/integrations/webhooks/deliveries/{deliveryId}/replay',
  'POST /api/integrations/webhooks/{id}/disable',
  'POST /api/integrations/webhooks/{id}/enable',
  'POST /api/integrations/webhooks/{id}/rotate-secret',
  'POST /api/integrations/webhooks/{id}/test-delivery',
  'POST /api/integrations/webhooks',
  'PUT /api/integrations/webhooks/{id}'
]) {
  const value = operation(id);
  assert.equal(value.status, 'surfaced', `${id} must be surfaced.`);
  assert.ok(value.consumers.desktop.length > 0, `${id} is missing its desktop consumer.`);
  assert.ok(value.consumers.mobile.length > 0, `${id} is missing its mobile consumer.`);
}

console.log('V3-SURFACE-006 evidence passed: 7 deterministic and 7 real checks, 4 reviewed secret-safe captures, 94/94 API rerun and 12 webhook operations on both clients.');

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
  assert.ok(Object.keys(hashes).length > 0, 'Hash manifest must not be empty.');
  for (const [path, value] of Object.entries(hashes)) {
    assert.ok(exists(path), `Hashed source is missing: ${path}`);
    assert.match(value, /^[a-f0-9]{64}$/, `${path} is not a SHA-256 digest.`);
    assert.equal(value, fileSha(path), `Hashed source changed: ${path}`);
  }
}

function operation(id) {
  const value = matrix.operations.find(candidate => candidate.id === id);
  assert.ok(value, `Operation is missing: ${id}`);
  return value;
}
