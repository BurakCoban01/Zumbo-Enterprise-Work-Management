import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-SURFACE-005.json');
const visual = json('artifacts/v3/V3-SURFACE-005-visual.json');
const deterministic = json('artifacts/ui/v3-surface-005/result.json');
const real = json('artifacts/ui/v3-surface-005-real/result.json');
const matrix = json('docs/product/api-ui-capability-matrix.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-SURFACE-005');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.temporaryApiListenerStopped, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 145, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 86);
assert.equal(evidence.validation.deterministicBrowser.checks, 7);
assert.equal(evidence.validation.realBackendBrowser.checks, 6);
assert.deepEqual(evidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(evidence.validation.deterministicBrowser.statusTokenInDomOrUrl, false);
assert.equal(evidence.validation.backend.unitPassed, 200);
assert.deepEqual(evidence.validation.backend.fullApiFirstRun, {
  passed: 92,
  failed: 1,
  diagnostic: 'Existing cursor timing scenario returned an empty page at ApiFlowTests.cs:319.'
});
assert.deepEqual(evidence.validation.backend.fullApiRerun, { passed: 93, failed: 0 });
assert.equal(evidence.validation.backend.openApiBaselinePathsPreserved, 193);
assert.equal(evidence.validation.backend.ndjsonUtf8WithoutBom, true);
assert.equal(evidence.validation.backend.exportHeadersNoStoreNosniffAndCount, true);
assert.equal(evidence.validation.backend.integrityUsesApiEnvelope, true);
assert.equal(evidence.validation.visualReview.criticalBlockers, 0);
assert.equal(evidence.validation.visualReview.horizontalOverflow390, false);
assert.equal(evidence.validation.visualReview.statusTokenCaptured, false);
assert.equal(evidence.validation.visualReview.secretOrRedactedPayloadCaptured, false);
assertSourceHashes(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 7);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 6);
assert.deepEqual(real.failures, []);
assert.equal(real.cleanup.failed, 0);
assert.equal(real.cleanup.passed, 1);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-SURFACE-005');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 5);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.containsStatusToken, false);
  assert.equal(capture.containsSecretOrRedactedPayload, false);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.deepEqual(matrix.summary, {
  operations: 233,
  openApiOperations: 229,
  frontendCalls: 327,
  desktopCalls: 198,
  mobileCalls: 129,
  backgroundCapabilities: 8,
  gapCapabilities: 5,
  byStatus: { surfaced: 126, partial: 47, absent: 51, intentional: 9 },
  duplicateOperations: 0,
  unmatchedFrontendCalls: 0,
  unownedOperations: 0
});
assert.ok(matrix.informationArchitecture.desktop.routes.includes('/audit'));

for (const id of [
  'GET /api/audit',
  'GET /api/audit/export',
  'GET /api/audit/integrity/{organizationId}'
]) {
  assert.ok(operation(id).consumers.desktop.length > 0, `${id} is missing its desktop consumer.`);
}

for (const id of [
  'GET /api/auth/privacy/export.ndjson',
  'GET /api/auth/privacy/jobs/{jobId}/status',
  'GET /api/auth/privacy/jobs/{jobId}',
  'POST /api/auth/privacy/anonymization-jobs',
  'POST /api/auth/privacy/jobs/{jobId}/reconcile',
  'POST /api/auth/privacy/jobs/{jobId}/retry'
]) {
  const value = operation(id);
  assert.equal(value.status, 'surfaced', `${id} must be surfaced.`);
  assert.ok(value.consumers.desktop.length > 0, `${id} is missing its desktop consumer.`);
  assert.ok(value.consumers.mobile.length > 0, `${id} is missing its mobile consumer.`);
}

console.log('V3-SURFACE-005 evidence passed: 7 deterministic and 6 real checks, 5 reviewed token-safe captures, 93/93 API rerun and audit/privacy ownership on both clients.');

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
