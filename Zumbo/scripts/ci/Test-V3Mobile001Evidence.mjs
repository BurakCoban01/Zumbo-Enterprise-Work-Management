import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-MOBILE-001.json');
const visual = json('artifacts/v3/V3-MOBILE-001-visual.json');
const deterministic = json('artifacts/ui/v3-mobile-001/result.json');
const real = json('artifacts/ui/v3-mobile-001-real/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-MOBILE-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryApiListenerStopped, true);
assert.equal(evidence.temporaryGatewayListenerStopped, true);
assert.equal(evidence.existingFrontendListenerPreserved, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 176, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.focusedSource, { passed: 17, failed: 0 });
assert.equal(evidence.validation.frontend.build.assets, 97);
assert.equal(evidence.validation.frontend.runtimeAssetsChromium, true);
assert.equal(evidence.validation.deterministicBrowser.checks, 9);
assert.equal(evidence.validation.realBackendBrowser.checks, 9);
assert.deepEqual(
  evidence.validation.realBackendBrowser.tenantCleanup,
  { attempted: 2, passed: 2, failed: 0 }
);
assert.equal(evidence.validation.realBackendBrowser.crossTenantSearchDenied, true);
assert.equal(evidence.validation.accessibility.minimumTouchTargetPixels, 44);
assert.equal(evidence.validation.backendBaseline.sourceChangedByTask, false);
assert.equal(evidence.validation.inAppBrowser.available, false);
assertSourceHashes(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 9);
assert.deepEqual(deterministic.viewports, ['360x780', '390x844', '430x844']);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 9);
assert.deepEqual(real.viewports, ['360x780', '390x844', '430x844']);
assert.deepEqual(real.failures, []);
assert.equal(real.cleanup.length, 2);
assert.equal(real.cleanup.every(item => item.passed), true);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-MOBILE-001');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 10);
assert.deepEqual(
  [...new Set(visual.captures.map(capture => capture.viewport.width))].sort((left, right) => left - right),
  [360, 390, 430]
);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.equal(capture.criticalBlockers, 0);
  assert.equal(capture.horizontalOverflow, false);
  assert.equal(capture.interactiveOverlap, false);
  assert.equal(capture.containsSecretOrRealUserData, false);
  assert.equal(capture.containsOpaqueIdentifier, false);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

console.log('V3-MOBILE-001 evidence passed: 176 frontend tests, 97 assets, 18 browser checks, two-tenant cleanup and ten reviewed captures.');

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
