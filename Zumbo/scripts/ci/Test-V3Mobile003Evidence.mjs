import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-MOBILE-003.json');
const visual = json('artifacts/v3/V3-MOBILE-003-visual.json');
const real = json('artifacts/ui/v3-mobile-003-real/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-MOBILE-003');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryStaticListenersStopped, true);
assert.equal(evidence.existingFrontendListenerPreserved, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 176, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.focusedSource, { passed: 13, failed: 0 });
assert.equal(evidence.validation.frontend.build.assets, 97);
assert.equal(evidence.validation.frontend.runtimeAssetsChromium, true);
assert.equal(evidence.validation.existingPwaBrowser.checks, 14);
assert.equal(evidence.validation.realBrowser.checks, 9);
assert.equal(evidence.validation.realBrowser.authenticatedApiNotCached, true);
assert.equal(evidence.validation.realBrowser.corruptUpdateRetainsActiveShell, true);
assert.equal(evidence.validation.realBrowser.corruptFirstInstallRejected, true);
assert.equal(evidence.validation.accessibility.updateTouchTargetPixels, 44);
assert.equal(evidence.validation.security.authenticatedApiCached, false);
assert.equal(evidence.validation.backendBaseline.sourceChangedByTask, false);
assert.equal(evidence.validation.backendBaseline.rerunForTask, false);
assertSourceHashes(evidence.hashes);

assert.equal(real.passed, true);
assert.equal(real.browser, 'chromium');
assert.equal(real.viewport, '390x844');
assert.equal(real.checks.length, 9);
assert.deepEqual(real.failures, []);
for (const check of [
  'verified-first-install',
  'repeat-install-cache-stable',
  'authenticated-api-response-not-cached',
  'degraded-api-has-no-stale-cache-fallback',
  'offline-deep-link-navigation-shell',
  'user-controlled-update-prompt',
  'scoped-update-cache-cleanup',
  'corrupt-update-retains-active-shell',
  'corrupt-first-install-visible-and-rejected'
]) {
  assert.ok(real.checks.includes(check), `Real check is missing: ${check}`);
}

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-MOBILE-003');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 3);
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

console.log('V3-MOBILE-003 evidence passed: 176 frontend tests, 14 existing PWA checks, 9 task checks and three reviewed captures.');

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
