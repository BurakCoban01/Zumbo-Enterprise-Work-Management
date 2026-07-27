import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-SURFACE-001.json');
const visual = json('artifacts/v3/V3-SURFACE-001-visual.json');
const deterministic = json('artifacts/ui/v3-surface-001/result.json');
const real = json('artifacts/ui/v3-surface-001-real/result.json');
const matrix = json('docs/product/api-ui-capability-matrix.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-SURFACE-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 123, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 68);
assert.equal(evidence.validation.deterministicBrowser.checks, 9);
assert.equal(evidence.validation.realBackendBrowser.checks, 10);
assert.deepEqual(evidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(evidence.validation.backend.defaultComponentLimit, 50);
assert.equal(evidence.validation.backend.rejectedUniqueNames, 51);
assert.equal(evidence.validation.backend.silentTruncation, false);
assert.equal(evidence.validation.visualReview.criticalBlockers, 0);
assert.equal(evidence.validation.visualReview.horizontalOverflow390, false);
assert.equal(evidence.validation.capabilityMatrix.projectCatalogGapClosed, true);
assertShaMap(evidence.hashes);

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 9);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 10);
assert.deepEqual(real.failures, []);
assert.equal(real.cleanup.failed, 0);
assert.equal(real.cleanup.passed, 1);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-SURFACE-001');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 6);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.ok(matrix.summary.operations >= 232);
assert.ok(matrix.summary.frontendCalls >= 269);
assert.equal(matrix.summary.unmatchedFrontendCalls, 0);
assert.equal(matrix.summary.unownedOperations, 0);
assert.equal(matrix.capabilityGaps.some(gap => gap.id === 'project-catalogs'), false);
assert.ok(matrix.informationArchitecture.desktop.projectViews.includes('catalog'));
assert.ok(matrix.informationArchitecture.mobile.routes.includes('/projects/:projectId/catalog'));

console.log('V3-SURFACE-001 evidence passed: 9 deterministic and 10 real checks, 6 reviewed captures, explicit limit and closed catalog gap.');

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
