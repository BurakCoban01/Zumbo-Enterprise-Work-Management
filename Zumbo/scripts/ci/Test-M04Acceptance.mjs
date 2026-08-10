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

const browser = readJson('artifacts/final/M04-planning-roadmap/result.json');
const acceptance = readJson('artifacts/final/M04-planning-roadmap/M04-acceptance.json');

assert.equal(browser.passed, true);
assert.deepEqual(browser.checks, [
  'custom-workflow-published',
  'backlog-sprint-board-persisted',
  'quick-transition-follows-workflow',
  'roadmap-segments-total-100',
  'exact-status-distribution',
  'light-dark-readable',
  'narrow-responsive-no-overflow'
]);
assert.deepEqual(browser.failures, []);
assert.equal(browser.cleanup.attempted, true);
assert.equal(browser.cleanup.passed, true);

assert.equal(acceptance.passed, true);
assert.equal(acceptance.realBackend, true);
assert.equal(acceptance.browser, 'chromium');
assert.equal(acceptance.browserChecks, browser.checks.length);
assert.deepEqual(acceptance.focusedTests, {
  planningAndSprint: 15,
  mobileCharacterizationIaAndSprint: 25,
  workDetailAndBoard: 17
});
assert.equal(acceptance.frontendAssets, 126);
assert.equal(acceptance.productionHardcodedStatusDecisionMatches, 0);
assert.equal(acceptance.temporaryProjectArchived, true);
assert.equal(acceptance.statusMetadataPortionAccepted, true);
assert.equal(acceptance.rolePermissionMetadataDeferredToM05, true);
assert.equal(acceptance.screenshots.length, 3);

for (const screenshot of acceptance.screenshots) {
  const path = resolve(applicationRoot, screenshot.path);
  assert.ok(existsSync(path), `${screenshot.path} is missing.`);
  const actual = createHash('sha256').update(readFileSync(path)).digest('hex');
  assert.equal(actual, screenshot.sha256, `${screenshot.path} hash drifted.`);
}

const serialized = JSON.stringify({ browser, acceptance });
for (const forbiddenKey of ['password', 'accessToken', 'refreshToken', 'cookie']) {
  assert.equal(new RegExp(`"${forbiddenKey}"`, 'i').test(serialized), false, `Evidence contains forbidden key ${forbiddenKey}.`);
}
assert.equal(acceptance.secretsRecorded, false);
assert.equal(acceptance.noDeployment, true);
assert.equal(acceptance.noPublicExposure, true);

console.log('M04 acceptance passed: workflow-driven planning, exact roadmap distribution, responsive captures and cleanup verified.');
