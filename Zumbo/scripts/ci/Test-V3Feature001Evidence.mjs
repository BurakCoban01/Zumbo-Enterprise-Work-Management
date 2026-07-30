import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-FEATURE-001.json');
const visual = json('artifacts/v3/V3-FEATURE-001-visual.json');
const deterministic = json('artifacts/ui/v3-feature-001/result.json');
const real = json('artifacts/ui/v3-feature-001-real/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-FEATURE-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryApiListenersStopped, true);
assert.equal(evidence.existingFrontendListenerPreserved, true);
assert.deepEqual(evidence.validation.backend.unit, { passed: 221, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.architecture, { passed: 25, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.api, { passed: 101, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.gateway, { passed: 12, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.unit, { passed: 185, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 102);
assert.equal(evidence.validation.providerParity.mongoDb.passed, 1);
assert.equal(evidence.validation.providerParity.postgreSql.passed, 2);
assert.equal(evidence.validation.browser.deterministic.checks, 9);
assert.equal(evidence.validation.browser.realApi.checks, 11);
assert.equal(evidence.validation.apiSurface.openApiPaths, 216);
assert.equal(evidence.validation.apiSurface.openApiOperations, 257);
assert.equal(evidence.validation.apiSurface.unmatchedFrontendCalls, 0);
assert.equal(evidence.validation.apiSurface.unownedOperations, 0);
assertSourceHashes(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 9);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 11);
assert.equal(real.cleanup.failed, 0);
assert.equal(real.cleanup.passed, 1);
assert.deepEqual(real.failures, []);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-FEATURE-001');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 7);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.criticalBlockers, 0);
  assert.equal(capture.horizontalOverflow, false);
  assert.equal(capture.interactiveOverlap, false);
  assert.equal(capture.containsSecretOrRealUserData, false);
  assert.equal(capture.containsInternalOpaqueIdentifier, false);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

console.log('V3-FEATURE-001 evidence passed on the current tree: backend/provider parity, 185 frontend tests, 20 browser checks and seven reviewed captures.');

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
