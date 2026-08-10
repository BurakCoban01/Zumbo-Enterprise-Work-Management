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

const browser = readJson('artifacts/final/M03-daily-work/result.json');
const acceptance = readJson('artifacts/final/M03-daily-work/M03-acceptance.json');

assert.equal(browser.passed, true);
assert.deepEqual(browser.checks, [
  'normal-parameterless-url',
  'structured-notification-metadata',
  'my-work-semantic-filters-and-sort',
  'inbox-action-triage-distinct',
  'notification-popover-awareness-only',
  'projects-directory-filter-search-sort-page',
  'project-more-deeplink-back-forward-refresh',
  'desktop-1024-no-overflow',
  'mobile-adaptive-grouped-navigation'
]);
assert.deepEqual(browser.errors, []);
assert.ok(browser.notificationCount > 0);

assert.equal(acceptance.passed, true);
assert.equal(acceptance.realBackend, true);
assert.equal(acceptance.browser, 'chromium');
assert.equal(acceptance.browserChecks, browser.checks.length);
assert.equal(acceptance.notificationCount, browser.notificationCount);
assert.equal(acceptance.focusedFrontendTests, 21);
assert.equal(acceptance.focusedApiTests, 1);
assert.equal(acceptance.frontendAssets, 126);
assert.equal(acceptance.additiveApiContract, true);
assert.equal(acceptance.routesPreserved, true);
assert.equal(acceptance.screenshots.length, 6);

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

console.log('M03 acceptance passed: daily-work semantics, structured notifications, project directory, route parity and responsive captures verified.');
