import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-MOBILE-004.json');
const visual = json('artifacts/v3/V3-MOBILE-004-visual.json');
const browser = json('artifacts/ui/v3-mobile-004/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-MOBILE-004');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryStaticListenerStopped, true);
assert.equal(evidence.existingFrontendListenerPreserved, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 176, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.focusedSource, { passed: 18, failed: 0 });
assert.equal(evidence.validation.frontend.build.assets, 97);
assert.equal(evidence.validation.browser.checks, 11);
assert.equal(evidence.validation.accessibility.minimumRenderedTouchTargetPixels, 44);
assert.equal(evidence.validation.accessibility.missingAccessibleNames, 0);
assert.equal(evidence.validation.backendBaseline.sourceChangedByTask, false);
assert.equal(evidence.validation.backendBaseline.rerunForTask, false);
assertSourceHashes(evidence.hashes);

assert.equal(browser.passed, true);
assert.equal(browser.browser, 'chromium');
assert.deepEqual(browser.viewports, ['360x780', '390x844', '430x844', '844x390']);
assert.equal(browser.checks.length, 11);
assert.deepEqual(browser.failures, []);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-MOBILE-004');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 4);
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

console.log('V3-MOBILE-004 evidence passed: 176 frontend tests, 11 device checks and four reviewed captures.');

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
