import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-SURFACE-003.json');
const visual = json('artifacts/v3/V3-SURFACE-003-visual.json');
const deterministic = json('artifacts/ui/v3-surface-003/result.json');
const real = json('artifacts/ui/v3-surface-003-real/result.json');
const matrix = json('docs/product/api-ui-capability-matrix.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-SURFACE-003');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 133, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 75);
assert.equal(evidence.validation.deterministicBrowser.checks, 5);
assert.equal(evidence.validation.realBackendBrowser.checks, 6);
assert.deepEqual(evidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(evidence.validation.backend.focusedUnitPassed, 2);
assert.equal(evidence.validation.backend.focusedApiPassed, 3);
assert.equal(evidence.validation.backend.openApiBaselinePathsPreserved, 193);
assert.equal(evidence.validation.visualReview.criticalBlockers, 0);
assert.equal(evidence.validation.visualReview.horizontalOverflow390, false);
assert.equal(evidence.validation.visualReview.oneTimeSecretValuesCaptured, false);
assertShaMap(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 5);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 6);
assert.deepEqual(real.failures, []);
assert.equal(real.cleanup.failed, 0);
assert.equal(real.cleanup.passed, 1);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-SURFACE-003');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 4);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.containsOneTimeSecretValues, false);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.ok(matrix.summary.operations >= 233);
assert.ok(matrix.summary.frontendCalls >= 302);
assert.equal(matrix.summary.unmatchedFrontendCalls, 0);
assert.equal(matrix.summary.unownedOperations, 0);
assert.equal(matrix.capabilityGaps.some(gap => gap.id === 'session-security'), false);
assert.equal(operation('GET /api/auth/sessions').status, 'surfaced');
assert.equal(operation('DELETE /api/auth/sessions/{sessionId}').status, 'surfaced');
assert.ok(operation('GET /api/auth/sessions').consumers.desktop.length > 0);
assert.ok(operation('GET /api/auth/sessions').consumers.mobile.length > 0);

console.log('V3-SURFACE-003 evidence passed: 5 deterministic and 6 real checks, 4 reviewed secret-safe captures, targeted revoke and closed session-security gap.');

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

function assertShaMap(hashes) {
  assert.ok(Object.keys(hashes).length > 0, 'Hash manifest must not be empty.');
  for (const [name, value] of Object.entries(hashes)) {
    assert.match(value, /^[a-f0-9]{64}$/, `${name} is not a SHA-256 digest.`);
  }
}

function operation(id) {
  const value = matrix.operations.find(candidate => candidate.id === id);
  assert.ok(value, `Operation is missing: ${id}`);
  return value;
}
