import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-UX-001.json');
const visual = json('artifacts/v3/V3-UX-001-visual.json');
const realResult = json('artifacts/ui/v3-ux-real/result.json');
const personalEvidence = json('artifacts/v3/V3-UX-002.json');
const personalVisual = json('artifacts/v3/V3-UX-002-visual.json');
const personalRealResult = json('artifacts/ui/v3-ux-002-real/result.json');
const projectEvidence = json('artifacts/v3/V3-UX-003.json');
const projectVisual = json('artifacts/v3/V3-UX-003-visual.json');
const projectRealResult = json('artifacts/ui/v3-ux-003-real/result.json');
const boardEvidence = json('artifacts/v3/V3-UX-004.json');
const boardVisual = json('artifacts/v3/V3-UX-004-visual.json');
const boardRealResult = json('artifacts/ui/v3-ux-004-real/result.json');
const planningEvidence = json('artifacts/v3/V3-UX-005.json');
const planningVisual = json('artifacts/v3/V3-UX-005-visual.json');
const planningRealResult = json('artifacts/ui/v3-ux-005-real/result.json');
const detailEvidence = json('artifacts/v3/V3-UX-006.json');
const detailVisual = json('artifacts/v3/V3-UX-006-visual.json');
const detailRealResult = json('artifacts/ui/v3-ux-006-real/result.json');
const advancedPlanningEvidence = json('artifacts/v3/V3-UX-007.json');
const advancedPlanningVisual = json('artifacts/v3/V3-UX-007-visual.json');
const advancedPlanningRealResult = json('artifacts/ui/v3-ux-007-real/result.json');
const reportingEvidence = json('artifacts/v3/V3-UX-008.json');
const reportingVisual = json('artifacts/v3/V3-UX-008-visual.json');
const reportingRealResult = json('artifacts/ui/v3-ux-008-real/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-UX-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.deepEqual(evidence.validation.frontend.unit, { passed: 83, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 50);
assert.equal(evidence.validation.deterministicBrowser.passed, true);
assert.equal(evidence.validation.deterministicBrowser.loadingState, true);
assert.equal(evidence.validation.deterministicBrowser.controlled503State, true);
assert.equal(evidence.validation.realBackendBrowser.passed, true);
assert.deepEqual(evidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(evidence.validation.realBackendBrowser.targetedComposeResidue, 0);
assert.equal(evidence.validation.visualReview.criticalBlockers, 0);
assert.equal(evidence.broadSuiteAttempt.acceptanceAuthority, false);

assert.equal(realResult.passed, true);
assert.equal(realResult.checks.length, 10);
assert.deepEqual(realResult.failures, []);
assert.equal(realResult.cleanup.failed, 0);
assert.equal(realResult.cleanup.passed, 1);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-UX-001');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 4);
for (const capture of visual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(personalEvidence.schemaVersion, 1);
assert.equal(personalEvidence.task, 'V3-UX-002');
assert.equal(personalEvidence.passed, true);
assert.equal(personalEvidence.noDeployment, true);
assert.deepEqual(personalEvidence.validation.frontend.unit, { passed: 87, failed: 0, skipped: 0 });
assert.equal(personalEvidence.validation.frontend.build.assets, 51);
assert.equal(personalEvidence.validation.deterministicBrowser.passed, true);
assert.equal(personalEvidence.validation.realBackendBrowser.passed, true);
assert.equal(personalEvidence.validation.realBackendBrowser.checks, 11);
assert.deepEqual(personalEvidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(personalEvidence.validation.realBackendBrowser.targetedComposeResidue, 0);
assert.equal(personalEvidence.validation.visualReview.criticalBlockers, 0);
assert.equal(personalEvidence.diagnosticAttempts.acceptanceAuthority, false);

assert.equal(personalRealResult.passed, true);
assert.equal(personalRealResult.checks.length, 11);
assert.deepEqual(personalRealResult.failures, []);
assert.equal(personalRealResult.cleanup.failed, 0);
assert.equal(personalRealResult.cleanup.passed, 1);

assert.equal(personalVisual.schemaVersion, 1);
assert.equal(personalVisual.task, 'V3-UX-002');
assert.equal(personalVisual.browser, 'chromium');
assert.equal(personalVisual.captures.length, 6);
for (const capture of personalVisual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(projectEvidence.schemaVersion, 1);
assert.equal(projectEvidence.task, 'V3-UX-003');
assert.equal(projectEvidence.passed, true);
assert.equal(projectEvidence.noDeployment, true);
assertShaMap(projectEvidence.hashes);
assert.equal(projectEvidence.hashes.realResultSha256, fileSha('artifacts/ui/v3-ux-003-real/result.json'));
assert.deepEqual(projectEvidence.validation.frontend.unit, { passed: 91, failed: 0, skipped: 0 });
assert.equal(projectEvidence.validation.frontend.build.assets, 52);
assert.equal(projectEvidence.validation.deterministicBrowser.passed, true);
assert.equal(projectEvidence.validation.deterministicBrowser.noBoardFallback, true);
assert.equal(projectEvidence.validation.realBackendBrowser.passed, true);
assert.equal(projectEvidence.validation.realBackendBrowser.checks, 8);
assert.deepEqual(projectEvidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(projectEvidence.validation.realBackendBrowser.targetedComposeResidue, 0);
assert.equal(projectEvidence.validation.visualReview.criticalBlockers, 0);
assert.equal(projectEvidence.diagnosticAttempts.acceptanceAuthority, false);

assert.equal(projectRealResult.passed, true);
assert.equal(projectRealResult.checks.length, 8);
assert.deepEqual(projectRealResult.failures, []);
assert.equal(projectRealResult.cleanup.failed, 0);
assert.equal(projectRealResult.cleanup.passed, 1);

assert.equal(projectVisual.schemaVersion, 1);
assert.equal(projectVisual.task, 'V3-UX-003');
assert.equal(projectVisual.browser, 'chromium');
assert.equal(projectVisual.captures.length, 6);
for (const capture of projectVisual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(boardEvidence.schemaVersion, 1);
assert.equal(boardEvidence.task, 'V3-UX-004');
assert.equal(boardEvidence.passed, true);
assert.equal(boardEvidence.noDeployment, true);
assertShaMap(boardEvidence.hashes);
assert.equal(boardEvidence.hashes.boardExcellenceSha256, fileSha('Frontend/desktop-bulma/board-excellence.js'));
assert.equal(boardEvidence.hashes.unitSha256, fileSha('Frontend/tests/v3-board-excellence.test.mjs'));
assert.equal(boardEvidence.hashes.browserSha256, fileSha('Frontend/tests/v3-board-excellence-browser.mjs'));
assert.equal(boardEvidence.hashes.realBrowserSha256, fileSha('Frontend/tests/v3-board-excellence-real-browser.mjs'));
assert.equal(boardEvidence.hashes.realResultSha256, fileSha('artifacts/ui/v3-ux-004-real/result.json'));
assert.deepEqual(boardEvidence.validation.frontend.unit, { passed: 97, failed: 0, skipped: 0 });
assert.equal(boardEvidence.validation.frontend.build.assets, 53);
assert.equal(boardEvidence.validation.deterministicBrowser.passed, true);
assert.equal(boardEvidence.validation.deterministicBrowser.tasks, 48);
assert.equal(boardEvidence.validation.deterministicBrowser.viewerReadOnly, true);
assert.equal(boardEvidence.validation.realBackendBrowser.passed, true);
assert.equal(boardEvidence.validation.realBackendBrowser.checks, 8);
assert.deepEqual(boardEvidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(boardEvidence.validation.realBackendBrowser.targetedComposeResidue, 0);
assert.equal(boardEvidence.validation.visualReview.criticalBlockers, 0);
assert.equal(boardEvidence.diagnosticAttempts.acceptanceAuthority, false);

assert.equal(boardRealResult.passed, true);
assert.equal(boardRealResult.checks.length, 8);
assert.deepEqual(boardRealResult.failures, []);
assert.equal(boardRealResult.cleanup.failed, 0);
assert.equal(boardRealResult.cleanup.passed, 1);

assert.equal(boardVisual.schemaVersion, 1);
assert.equal(boardVisual.task, 'V3-UX-004');
assert.equal(boardVisual.browser, 'chromium');
assert.equal(boardVisual.captures.length, 5);
for (const capture of boardVisual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(planningEvidence.schemaVersion, 1);
assert.equal(planningEvidence.task, 'V3-UX-005');
assert.equal(planningEvidence.passed, true);
assert.equal(planningEvidence.noDeployment, true);
assertShaMap(planningEvidence.hashes);
assert.deepEqual(planningEvidence.validation.frontend.unit, { passed: 103, failed: 0, skipped: 0 });
assert.equal(planningEvidence.validation.frontend.build.assets, 53);
assert.equal(planningEvidence.validation.deterministicBrowser.passed, true);
assert.equal(planningEvidence.validation.deterministicBrowser.backlogItems, 110);
assert.equal(planningEvidence.validation.deterministicBrowser.boundedFirstPage, 100);
assert.equal(planningEvidence.validation.deterministicBrowser.ifMatchPropagation, true);
assert.equal(planningEvidence.validation.deterministicBrowser.concurrencyRollback, true);
assert.equal(planningEvidence.validation.deterministicBrowser.createStartCompleteCarryover, true);
assert.equal(planningEvidence.validation.deterministicBrowser.keyboardAndTouchPlanning, true);
assert.equal(planningEvidence.validation.deterministicBrowser.viewerReadOnly, true);
assert.equal(planningEvidence.validation.realBackendBrowser.passed, true);
assert.equal(planningEvidence.validation.realBackendBrowser.checks, 8);
assert.deepEqual(planningEvidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(planningEvidence.validation.realBackendBrowser.targetedComposeResidue, 0);
assert.equal(planningEvidence.validation.visualReview.criticalBlockers, 0);
assert.equal(planningEvidence.diagnosticAttempts.acceptanceAuthority, false);

assert.equal(planningRealResult.passed, true);
assert.equal(planningRealResult.checks.length, 8);
assert.deepEqual(planningRealResult.failures, []);
assert.equal(planningRealResult.cleanup.failed, 0);
assert.equal(planningRealResult.cleanup.passed, 1);

assert.equal(planningVisual.schemaVersion, 1);
assert.equal(planningVisual.task, 'V3-UX-005');
assert.equal(planningVisual.browser, 'chromium');
assert.equal(planningVisual.captures.length, 8);
for (const capture of planningVisual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(detailEvidence.schemaVersion, 1);
assert.equal(detailEvidence.task, 'V3-UX-006');
assert.equal(detailEvidence.passed, true);
assert.equal(detailEvidence.noDeployment, true);
assertShaMap(detailEvidence.hashes);
assert.equal(detailEvidence.hashes.workItemsSha256, fileSha('Frontend/desktop-bulma/work-items.js'));
assert.equal(detailEvidence.hashes.unitSha256, fileSha('Frontend/tests/v3-work-item-detail.test.mjs'));
assert.equal(detailEvidence.hashes.browserSha256, fileSha('Frontend/tests/v3-work-item-detail-browser.mjs'));
assert.equal(detailEvidence.hashes.realBrowserSha256, fileSha('Frontend/tests/v3-work-item-detail-real-browser.mjs'));
assert.equal(detailEvidence.hashes.realResultSha256, fileSha('artifacts/ui/v3-ux-006-real/result.json'));
assert.deepEqual(detailEvidence.validation.frontend.unit, { passed: 110, failed: 0, skipped: 0 });
assert.equal(detailEvidence.validation.frontend.build.assets, 53);
assert.equal(detailEvidence.validation.deterministicBrowser.passed, true);
assert.equal(detailEvidence.validation.deterministicBrowser.safeRichContent, true);
assert.equal(detailEvidence.validation.deterministicBrowser.conflictDraftPreserved, true);
assert.equal(detailEvidence.validation.deterministicBrowser.boundedActivity, true);
assert.equal(detailEvidence.validation.realBackendBrowser.passed, true);
assert.equal(detailEvidence.validation.realBackendBrowser.checks, 8);
assert.deepEqual(detailEvidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(detailEvidence.validation.providers.mongo.passed, 1);
assert.equal(detailEvidence.validation.providers.postgresql.passed, 2);
assert.equal(detailEvidence.validation.providers.targetedDockerResidue, 0);
assert.equal(detailEvidence.validation.visualReview.criticalBlockers, 0);
assert.equal(detailEvidence.diagnosticAttempts.acceptanceAuthority, false);

assert.equal(detailRealResult.passed, true);
assert.equal(detailRealResult.checks.length, 8);
assert.deepEqual(detailRealResult.failures, []);
assert.equal(detailRealResult.cleanup.failed, 0);
assert.equal(detailRealResult.cleanup.passed, 1);

assert.equal(detailVisual.schemaVersion, 1);
assert.equal(detailVisual.task, 'V3-UX-006');
assert.equal(detailVisual.browser, 'chromium');
assert.equal(detailVisual.captures.length, 7);
for (const capture of detailVisual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(advancedPlanningEvidence.schemaVersion, 1);
assert.equal(advancedPlanningEvidence.task, 'V3-UX-007');
assert.equal(advancedPlanningEvidence.passed, true);
assert.equal(advancedPlanningEvidence.noDeployment, true);
assert.deepEqual(advancedPlanningEvidence.hashes, {
  planningCoreSha256: fileSha('Frontend/shared/planning-core.js'),
  desktopAppSha256: fileSha('Frontend/desktop-bulma/app.js'),
  projectOverviewSha256: fileSha('Frontend/desktop-bulma/project-overview.js'),
  desktopPlanningSha256: fileSha('Frontend/desktop-bulma/planning-views.js'),
  desktopPlanningStylesSha256: fileSha('Frontend/desktop-bulma/planning-views.css'),
  desktopTemplateSha256: fileSha('Frontend/desktop-bulma/index.html'),
  mobileAppSha256: fileSha('Frontend/mobile-ionic/app.js'),
  mobileDetailsSha256: fileSha('Frontend/mobile-ionic/details.js'),
  mobilePlanningSha256: fileSha('Frontend/mobile-ionic/planning-views.js'),
  mobilePlanningStylesSha256: fileSha('Frontend/mobile-ionic/planning-views.css'),
  mobileTemplateSha256: fileSha('Frontend/mobile-ionic/index.html'),
  unitSha256: fileSha('Frontend/tests/v3-planning-views.test.mjs'),
  browserSha256: fileSha('Frontend/tests/v3-planning-views-browser.mjs'),
  realBrowserSha256: fileSha('Frontend/tests/v3-planning-views-real-browser.mjs'),
  assetManifestSha256: fileSha('Frontend/dist/asset-manifest.json'),
  realResultSha256: fileSha('artifacts/ui/v3-ux-007-real/result.json')
});
assert.deepEqual(advancedPlanningEvidence.validation.frontend.unit, { passed: 115, failed: 0, skipped: 0 });
assert.equal(advancedPlanningEvidence.validation.frontend.build.assets, 58);
assert.equal(advancedPlanningEvidence.validation.deterministicBrowser.passed, true);
assert.equal(advancedPlanningEvidence.validation.deterministicBrowser.workItems, 205);
assert.equal(advancedPlanningEvidence.validation.deterministicBrowser.sprints, 55);
assert.equal(advancedPlanningEvidence.validation.deterministicBrowser.completePagination, true);
assert.equal(advancedPlanningEvidence.validation.deterministicBrowser.ganttAndAccessibleTable, true);
assert.equal(advancedPlanningEvidence.validation.deterministicBrowser.concurrencyRollback, true);
assert.equal(advancedPlanningEvidence.validation.deterministicBrowser.viewerReadOnly, true);
assert.equal(advancedPlanningEvidence.validation.largeProjection.viewModelCap, false);
assert.equal(advancedPlanningEvidence.validation.largeProjection.silentTruncation, false);
assert.equal(advancedPlanningEvidence.validation.realBackendBrowser.passed, true);
assert.equal(advancedPlanningEvidence.validation.realBackendBrowser.checks, 8);
assert.deepEqual(advancedPlanningEvidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(advancedPlanningEvidence.validation.realBackendBrowser.targetedRuntimeResidue, 0);
assert.equal(advancedPlanningEvidence.validation.visualReview.criticalBlockers, 0);

assert.equal(advancedPlanningRealResult.passed, true);
assert.equal(advancedPlanningRealResult.checks.length, 8);
assert.deepEqual(advancedPlanningRealResult.failures, []);
assert.equal(advancedPlanningRealResult.cleanup.failed, 0);
assert.equal(advancedPlanningRealResult.cleanup.passed, 1);

assert.equal(advancedPlanningVisual.schemaVersion, 1);
assert.equal(advancedPlanningVisual.task, 'V3-UX-007');
assert.equal(advancedPlanningVisual.browser, 'chromium');
assert.equal(advancedPlanningVisual.captures.length, 9);
for (const capture of advancedPlanningVisual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

assert.equal(reportingEvidence.schemaVersion, 1);
assert.equal(reportingEvidence.task, 'V3-UX-008');
assert.equal(reportingEvidence.passed, true);
assert.equal(reportingEvidence.noDeployment, true);
assert.deepEqual(reportingEvidence.hashes, {
  reportingCoreSha256: fileSha('Frontend/shared/reporting-core.js'),
  desktopReportingSha256: fileSha('Frontend/desktop-bulma/reporting-views.js'),
  desktopReportingStylesSha256: fileSha('Frontend/desktop-bulma/reporting-views.css'),
  desktopTemplateSha256: fileSha('Frontend/desktop-bulma/index.html'),
  mobileReportingSha256: fileSha('Frontend/mobile-ionic/reporting-views.js'),
  mobileReportingStylesSha256: fileSha('Frontend/mobile-ionic/reporting-views.css'),
  mobileTemplateSha256: fileSha('Frontend/mobile-ionic/index.html'),
  unitSha256: fileSha('Frontend/tests/v3-reporting-views.test.mjs'),
  browserSha256: fileSha('Frontend/tests/v3-reporting-views-browser.mjs'),
  realBrowserSha256: fileSha('Frontend/tests/v3-reporting-views-real-browser.mjs'),
  assetManifestSha256: fileSha('Frontend/dist/asset-manifest.json'),
  realResultSha256: fileSha('artifacts/ui/v3-ux-008-real/result.json')
});
assert.deepEqual(reportingEvidence.validation.frontend.unit, { passed: 119, failed: 0, skipped: 0 });
assert.equal(reportingEvidence.validation.frontend.build.assets, 63);
assert.equal(reportingEvidence.validation.deterministicBrowser.passed, true);
assert.equal(reportingEvidence.validation.deterministicBrowser.workItems, 205);
assert.equal(reportingEvidence.validation.deterministicBrowser.completePagination, true);
assert.equal(reportingEvidence.validation.deterministicBrowser.capacityInvented, false);
assert.equal(reportingEvidence.validation.deterministicBrowser.productivityRanking, false);
assert.equal(reportingEvidence.validation.realBackendBrowser.passed, true);
assert.equal(reportingEvidence.validation.realBackendBrowser.checks, 6);
assert.equal(reportingEvidence.validation.realBackendBrowser.permissionIsolation, true);
assert.deepEqual(reportingEvidence.validation.realBackendBrowser.tenantCleanup, { attempted: 1, passed: 1, failed: 0 });
assert.equal(reportingEvidence.validation.realBackendBrowser.targetedRuntimeResidue, 0);
assert.equal(reportingEvidence.validation.backend.focusedUnitPassed, 3);
assert.equal(reportingEvidence.validation.backend.focusedApiPassed, 1);
assert.equal(reportingEvidence.validation.largeProjection.workItems, 1205);
assert.equal(reportingEvidence.validation.largeProjection.viewModelCap, false);
assert.equal(reportingEvidence.validation.largeProjection.repositoryPageTruncation, false);
assert.equal(reportingEvidence.validation.visualReview.criticalBlockers, 0);
assert.deepEqual(reportingEvidence.validation.capabilityMatrix, { operations: 232, frontendCalls: 240, unmatchedFrontendCalls: 0, unownedOperations: 0 });

assert.equal(reportingRealResult.passed, true);
assert.equal(reportingRealResult.checks.length, 6);
assert.deepEqual(reportingRealResult.failures, []);
assert.equal(reportingRealResult.cleanup.failed, 0);
assert.equal(reportingRealResult.cleanup.passed, 1);

assert.equal(reportingVisual.schemaVersion, 1);
assert.equal(reportingVisual.task, 'V3-UX-008');
assert.equal(reportingVisual.browser, 'chromium');
assert.equal(reportingVisual.captures.length, 7);
for (const capture of reportingVisual.captures) {
  assert.equal(capture.reviewed, true);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.bytes, fileBytes(capture.screenshot));
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

console.log('V3 UX evidence passed: 67 real-backend checks, 52 immutable reviewed captures and zero targeted runtime residue.');

function json(path) { return JSON.parse(read(path)); }
function exists(path) { return existsSync(resolve(applicationRoot, path)); }
function read(path) { return readFileSync(resolve(applicationRoot, path), 'utf8'); }
function fileBytes(path) { return readFileSync(resolve(applicationRoot, path)).byteLength; }
function fileSha(path) { return createHash('sha256').update(readFileSync(resolve(applicationRoot, path))).digest('hex'); }
function assertShaMap(hashes) {
  assert.ok(Object.keys(hashes).length > 0, 'Hash manifest must not be empty.');
  for (const [name, value] of Object.entries(hashes)) {
    assert.match(value, /^[a-f0-9]{64}$/, `${name} is not a SHA-256 digest.`);
  }
}
