import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

function readJson(relativePath) {
  const path = resolve(applicationRoot, relativePath);
  assert.ok(existsSync(path), `${relativePath} is missing.`);
  return JSON.parse(readFileSync(path, 'utf8'));
}

const success = readJson('artifacts/final/M02-board/result.json');
const failure = readJson('artifacts/final/M02-board/failure-result.json');
const acceptance = readJson('artifacts/final/M02-board/M02-acceptance.json');

assert.equal(success.passed, true);
assert.equal(success.task, 'FINAL-BOARD-001');
assert.equal(success.exactRestore, true);
assert.deepEqual(success.checks, [
  'refresh-free-cross-column-success',
  'refresh-free-exact-restore',
  'cross-column-pointer-before-placement',
  'cross-column-placement-persists',
  'cross-column-exactly-restored',
  'empty-visible-column-drop',
  'empty-column-exactly-restored',
  'same-column-authoritative-rank',
  'rank-persists-after-refresh',
  'rank-exactly-restored',
  'same-column-first-placement',
  'same-column-end-placement-restored',
  'wip-preflight-no-request',
  'light-dark-1440-1024'
]);
assert.equal(success.unexpectedConsoleErrors, 0);

assert.equal(failure.passed, true);
assert.equal(failure.task, 'FINAL-BOARD-003');
assert.equal(failure.exactRestore, true);
assert.deepEqual(failure.checks, [
  'network-interruption-rollback-recovery',
  'workflow-rejection-local-rollback',
  'rank-cas-conflict-reconcile',
  'remote-realtime-move-and-restore'
]);
assert.deepEqual(failure.unexpectedErrors, []);

assert.equal(acceptance.passed, true);
assert.deepEqual(acceptance.tasks, ['FINAL-BOARD-001', 'FINAL-BOARD-002', 'FINAL-BOARD-003']);
assert.equal(acceptance.realBackend, true);
assert.equal(acceptance.browser, 'chromium');
assert.equal(acceptance.exactRestore, true);
assert.equal(acceptance.focusedUnitTests, 8);
assert.equal(acceptance.focusedExistingTests, 31);
assert.equal(acceptance.frontendAssets, 125);
assert.equal(acceptance.noBackendContractChange, true);
assert.equal(acceptance.noMobileSourceChange, true);
assert.equal(acceptance.screenshots.length, 5);
for (const screenshot of acceptance.screenshots) {
  const path = resolve(applicationRoot, screenshot.path);
  assert.ok(existsSync(path), `${screenshot.path} is missing.`);
  const actual = createHash('sha256').update(readFileSync(path)).digest('hex');
  assert.equal(actual, screenshot.sha256, `${screenshot.path} hash drifted.`);
}

const serialized = JSON.stringify({ success, failure, acceptance });
for (const forbiddenKey of ['password', 'accessToken', 'refreshToken', 'cookie']) {
  assert.equal(new RegExp(`"${forbiddenKey}"`, 'i').test(serialized), false, `Evidence contains forbidden key ${forbiddenKey}.`);
}
for (const evidence of [success, failure, acceptance]) {
  assert.equal(evidence.secretsRecorded, false);
  assert.equal(evidence.noDeployment, true);
  assert.equal(evidence.noPublicExposure, true);
}

console.log('M02 acceptance passed: refresh-free exact placement, failure recovery, realtime, responsive captures and hashes verified.');
