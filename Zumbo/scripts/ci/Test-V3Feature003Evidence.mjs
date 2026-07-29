import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-FEATURE-003.json');
const visual = json('artifacts/v3/V3-FEATURE-003-visual.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-FEATURE-003');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryApiListenersStopped, true);
assert.equal(evidence.existingFrontendListenerPreserved, true);
assert.deepEqual(evidence.validation.backend.unit, { passed: 228, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.api, { passed: 102, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.architecture, { passed: 25, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.unit, { passed: 188, failed: 0, skipped: 0 });
assert.equal(evidence.validation.providerParity.mongoDb.passed, 1);
assert.equal(evidence.validation.providerParity.postgreSql.passed, 2);
assert.equal(evidence.validation.browser.deterministic.checks, 4);
assert.equal(evidence.validation.browser.realApi.checks.length, 5);
assert.equal(evidence.validation.apiSurface.openApiPathsPreserved, 221);
assert.equal(evidence.validation.apiSurface.openApiOperations, 265);
assert.equal(evidence.validation.apiSurface.unmatchedFrontendCalls, 0);
assert.equal(evidence.validation.apiSurface.unownedOperations, 0);
assert.ok(Object.values(evidence.validation.dashboards).every(value =>
  Array.isArray(value) ? value.length > 0 : Number.isInteger(value) ? value > 0 : value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value =>
  Array.isArray(value) ? value.length > 0 : value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-FEATURE-003');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 4);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.criticalBlockers, 0);
  assert.equal(capture.horizontalOverflow, false);
  assert.equal(capture.interactiveOverlap, false);
  assert.equal(capture.containsSecretOrRealUserData, false);
  assert.ok(existsSync(resolve(applicationRoot, capture.screenshot)));
  assert.equal(readFileSync(resolve(applicationRoot, capture.screenshot)).byteLength, capture.bytes);
  assert.equal(fileSha(capture.screenshot), capture.sha256);
}

console.log('V3-FEATURE-003 evidence passed: dashboards, provider parity, 188 frontend tests and 9 browser checks.');

function json(path) {
  return JSON.parse(readFileSync(resolve(applicationRoot, path), 'utf8'));
}

function fileSha(path) {
  return createHash('sha256')
    .update(readFileSync(resolve(applicationRoot, path)))
    .digest('hex');
}
