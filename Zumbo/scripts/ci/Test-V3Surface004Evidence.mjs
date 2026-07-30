import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-SURFACE-004.json');
const visual = json('artifacts/v3/V3-SURFACE-004-visual.json');
const deterministic = json('artifacts/ui/v3-surface-004/result.json');
const real = json('artifacts/ui/v3-surface-004-real/result.json');
const matrix = json('docs/product/api-ui-capability-matrix.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-SURFACE-004');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.temporaryApiListenerStopped, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 138, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 80);
assert.equal(evidence.validation.deterministicBrowser.checks, 7);
assert.equal(evidence.validation.realBackendBrowser.checks, 8);
assert.deepEqual(evidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(evidence.validation.realBackendBrowser.dryRunCreatedWorkItems, 0);
assert.equal(evidence.validation.backend.focusedApiPassed, 1);
assert.equal(evidence.validation.backend.fullApiPassed, 93);
assert.equal(evidence.validation.backend.openApiBaselinePathsPreserved, 193);
assert.equal(evidence.validation.backend.artifactRetentionDays, 7);
assert.equal(evidence.validation.backend.ownerTenantCheckedBeforeExpiry, true);
assert.equal(evidence.validation.backend.expiredArtifactCleanupIdempotent, true);
assert.equal(evidence.validation.backend.unrelatedArtifactPreserved, true);
assert.equal(evidence.validation.visualReview.criticalBlockers, 0);
assert.equal(evidence.validation.visualReview.horizontalOverflow390, false);
assert.equal(evidence.validation.visualReview.artifactPayloadValuesCaptured, false);
assertSourceHashes(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 7);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 8);
assert.deepEqual(real.failures, []);
assert.equal(real.cleanup.failed, 0);
assert.equal(real.cleanup.passed, 1);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-SURFACE-004');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 6);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.containsArtifactPayloadValues, false);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(matrix.summary.operations, 233);
assert.equal(matrix.summary.frontendCalls, 314);
assert.equal(matrix.summary.desktopCalls, 191);
assert.equal(matrix.summary.mobileCalls, 123);
assert.equal(matrix.summary.unmatchedFrontendCalls, 0);
assert.equal(matrix.summary.unownedOperations, 0);
assert.ok(matrix.informationArchitecture.desktop.projectViews.includes('jobs'));
assert.ok(matrix.informationArchitecture.mobile.routes.includes('/projects/:projectId/jobs'));

for (const id of [
  'GET /api/work-items/bulk/jobs',
  'GET /api/work-items/bulk/jobs/{jobId}/errors',
  'GET /api/work-items/bulk/jobs/{jobId}/result',
  'POST /api/work-items/bulk/jobs/export',
  'POST /api/work-items/bulk/jobs/import',
  'POST /api/work-items/bulk/jobs/{jobId}/cancel',
  'POST /api/work-items/bulk/jobs/{jobId}/retry'
]) {
  const value = operation(id);
  assert.equal(value.status, 'surfaced', `${id} must be surfaced.`);
  assert.ok(value.consumers.desktop.length > 0, `${id} is missing its desktop consumer.`);
  assert.ok(value.consumers.mobile.length > 0, `${id} is missing its mobile consumer.`);
}

console.log('V3-SURFACE-004 evidence passed: 7 deterministic and 8 real checks, 6 reviewed artifact-safe captures, 93 API tests and 7 durable job operations surfaced on both clients.');

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
