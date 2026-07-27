import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-MOBILE-002.json');
const visual = json('artifacts/v3/V3-MOBILE-002-visual.json');
const real = json('artifacts/ui/v3-mobile-002-real/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-MOBILE-002');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryApiListenerStopped, true);
assert.equal(evidence.temporaryGatewayListenerStopped, true);
assert.equal(evidence.existingFrontendListenerPreserved, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 176, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.focusedSource, { passed: 15, failed: 0 });
assert.equal(evidence.validation.frontend.build.assets, 97);
assert.equal(evidence.validation.frontend.runtimeAssetsChromium, true);
assert.equal(evidence.validation.realBackendBrowser.checks, 16);
assert.deepEqual(evidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(evidence.validation.realBackendBrowser.separatedApproval, true);
assert.equal(evidence.validation.accessibility.minimumTouchTargetPixels, 44);
assert.equal(evidence.validation.security.approvalSeparationOfDuties, true);
assert.equal(evidence.validation.backendBaseline.sourceChangedByTask, false);
assert.equal(evidence.validation.inAppBrowser.available, false);
assertSourceHashes(evidence.hashes);

assert.equal(real.passed, true);
assert.equal(real.checks.length, 16);
assert.deepEqual(real.viewports, ['390x844']);
assert.deepEqual(real.failures, []);
assert.equal(real.cleanup.passed, true);
for (const check of [
  'real-touch-safe-board-move',
  'real-backlog-plan',
  'real-sprint-start',
  'real-edit-move',
  'real-checklist-relation',
  'real-attachment-upload',
  'real-watch-vote',
  'real-comment-worklog',
  'real-self-approval-denied',
  'real-approve-transition',
  'real-search',
  'real-inbox-read',
  'real-offline-mutation-block',
  'real-viewer-read-only-comment'
]) {
  assert.ok(real.checks.includes(check), `Real check is missing: ${check}`);
}

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-MOBILE-002');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 6);
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

console.log('V3-MOBILE-002 evidence passed: 176 frontend tests, 16 real checks, role cleanup and six reviewed captures.');

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
