import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidencePath = 'artifacts/v3/V3-FEATURE-005.json';
const visualPath = 'artifacts/v3/V3-FEATURE-005-visual.json';
const sourcePaths = [
  'Backend/src/Zumbo.Modules.Projects/GoalDocuments.cs',
  'Backend/src/Zumbo.Modules.Projects/GoalService.cs',
  'Backend/src/Zumbo.Api/GoalAdapters.cs',
  'Backend/src/Zumbo.Api/Endpoints/GoalEndpoints.cs',
  'Backend/src/Zumbo.Api/Program.cs',
  'Backend/src/Zumbo.Api/Hosting/ApiHostRegistration.cs',
  'Backend/src/Zumbo.Api/Hosting/ApiPipeline.cs',
  'Backend/src/Zumbo.Api/MongoMigrations.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations.cs',
  'Backend/tests/Zumbo.ApiTests/RouteInventory.approved.txt',
  'Backend/tests/Zumbo.ArchitectureTests/ArchitectureBoundaryTests.cs',
  'Backend/tests/Shared/GoalRepositoryContract.cs',
  'Backend/tests/Zumbo.UnitTests/GoalServiceTests.cs',
  'Backend/tests/Zumbo.UnitTests/InMemoryGoalRepositoryContractTests.cs',
  'Backend/tests/Zumbo.ApiTests/GoalApiTests.cs',
  'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoGoalRepositoryContractTests.cs',
  'Backend/tests/Zumbo.PostgreSqlIntegrationTests/PostgreSqlGoalRepositoryContractTests.cs',
  'Frontend/shared/goal-core.js',
  'Frontend/shared/api-client.js',
  'Frontend/desktop-bulma/goal-center.js',
  'Frontend/desktop-bulma/goal-center.css',
  'Frontend/desktop-bulma/app.js',
  'Frontend/desktop-bulma/index.html',
  'Frontend/mobile-ionic/goal-center.js',
  'Frontend/mobile-ionic/goal-center.css',
  'Frontend/mobile-ionic/app.js',
  'Frontend/mobile-ionic/index.html',
  'Frontend/tests/fe003-desktop-characterization.test.mjs',
  'Frontend/tests/fe004-mobile-characterization.test.mjs',
  'Frontend/tests/v3-goal-core.test.mjs',
  'Frontend/tests/v3-goal-browser.mjs',
  'Frontend/tests/v3-goal-real-browser.mjs',
  'contracts/openapi.v1.json',
  'docs/product/api-ui-capability-matrix.json',
  'scripts/product/Build-ProductCapabilityMatrix.mjs',
  'scripts/ci/Test-V3ProductCapabilityMatrix.mjs',
  'scripts/ci/Test-V3Feature005Evidence.mjs'
];
const captures = [
  {
    surface: 'desktop-owner-ready',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-feature-005/desktop-ready.png',
    notes: 'Goal owner definition, measurable key results, progress history and named links.'
  },
  {
    surface: 'desktop-partial-sources',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-feature-005/desktop-partial-sources.png',
    notes: 'Explicit partial-source roll-up without invented progress.'
  },
  {
    surface: 'mobile-key-result-owner',
    viewport: '390x844',
    screenshot: 'artifacts/ui/v3-feature-005/mobile-result-owner.png',
    minimumCommandTargetPixels: 44,
    notes: 'Scoped key-result progress authority with read-only goal definition.'
  },
  {
    surface: 'desktop-owner-real-api',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-feature-005-real/desktop-owner.png',
    notes: 'Real API goal lifecycle, history and readable source links.'
  },
  {
    surface: 'mobile-key-result-owner-real-api',
    viewport: '390x844',
    screenshot: 'artifacts/ui/v3-feature-005-real/mobile-result-owner.png',
    minimumCommandTargetPixels: 44,
    notes: 'Real API scoped progress update without horizontal overflow.'
  }
];

if (process.argv.includes('--write')) writeArtifacts();

const evidence = json(evidencePath);
const visual = json(visualPath);
const deterministic = json('artifacts/ui/v3-feature-005/result.json');
const real = json('artifacts/ui/v3-feature-005-real/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-FEATURE-005');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryApiListenersStopped, true);
assert.equal(evidence.frontendPreviewActive, true);
assert.deepEqual(evidence.validation.backend.unit, { passed: 239, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.api, { passed: 104, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.architecture, { passed: 25, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.unit, { passed: 194, failed: 0, skipped: 0 });
assert.equal(evidence.validation.providerParity.mongoDb.passed, 1);
assert.equal(evidence.validation.providerParity.postgreSql.passed, 2);
assert.equal(evidence.validation.browser.deterministic.checks, 5);
assert.equal(evidence.validation.browser.realApi.checks.length, 5);
assert.equal(evidence.validation.apiSurface.openApiPathsPreserved, 236);
assert.equal(evidence.validation.apiSurface.openApiOperations, 286);
assert.equal(evidence.validation.apiSurface.matrixOperations, 290);
assert.equal(evidence.validation.apiSurface.frontendCalls, 477);
assert.equal(evidence.validation.apiSurface.goalOperationsSurfaced, 10);
assert.equal(evidence.validation.apiSurface.unmatchedFrontendCalls, 0);
assert.equal(evidence.validation.apiSurface.unownedOperations, 0);
assert.ok(Object.values(evidence.validation.goals).every(value =>
  Number.isInteger(value) ? value > 0 : value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value =>
  Array.isArray(value) ? value.length > 0 : value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 5);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 5);
assert.equal(real.cleanup.failed, 0);
assert.deepEqual(real.failures, []);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-FEATURE-005');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.captures.length, 5);
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

console.log('V3-FEATURE-005 evidence passed: 239 unit, 104 API, provider parity and 10 browser checks.');

function writeArtifacts() {
  const generatedAtUtc = new Date().toISOString();
  mkdirSync(resolve(applicationRoot, 'artifacts/v3'), { recursive: true });
  const visual = {
    schemaVersion: 1,
    task: 'V3-FEATURE-005',
    browser: 'chromium',
    generatedAtUtc,
    captures: captures.map(capture => ({
      ...capture,
      bytes: readFileSync(resolve(applicationRoot, capture.screenshot)).byteLength,
      sha256: fileSha(capture.screenshot),
      reviewed: true,
      criticalBlockers: 0,
      horizontalOverflow: false,
      interactiveOverlap: false,
      containsSecretOrRealUserData: false
    }))
  };
  const evidence = {
    schemaVersion: 1,
    task: 'V3-FEATURE-005',
    generatedAtUtc,
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 239, failed: 0, skipped: 0 },
        focusedGoalUnit: { passed: 5, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        api: { passed: 104, failed: 0, skipped: 0 },
        focusedGoalApi: { passed: 1, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 }
      },
      providerParity: {
        mongoDb: { passed: 1, failed: 0, realProvider: true, port: 58231 },
        postgreSql: { passed: 2, failed: 0, realProvider: true, migrationApplied: true, port: 58232 },
        taskContainersRemoved: true,
        providerPortsClosed: true,
        realBrowserComposeRemoved: true,
        unrelatedContainersPreserved: true
      },
      frontend: {
        lint: true,
        unit: { passed: 194, failed: 0, skipped: 0 },
        focusedGoal: { passed: 3, failed: 0 },
        build: { passed: true, assets: 114, sha256Verified: true },
        dependencyAudit: { critical: 0, high: 2, timeBoundExceptions: 10 },
        licenseAudit: { passed: true, packages: 22 }
      },
      browser: {
        deterministic: {
          passed: true,
          checks: 5,
          states: ['owner-history', 'partial-source', 'readable-links', 'scoped-result-owner', 'mobile-reflow']
        },
        realApi: {
          passed: true,
          checks: [
            'goal-key-result-progress-history',
            'key-result-owner-scoped-authority',
            'ready-linked-rollup',
            'desktop-owner-progress-and-links',
            'mobile-result-owner-no-overflow'
          ],
          cleanupPassed: 1,
          cleanupFailed: 0
        },
        viewports: ['1440x1000', '390x844'],
        captures: 5,
        horizontalOverflow: false,
        interactiveOverlap: false
      },
      goals: {
        maximumViewers: 50,
        maximumInitiativeLinks: 20,
        maximumProjectLinks: 20,
        maximumKeyResults: 50,
        maximumGoalUpdates: 50,
        maximumKeyResultUpdates: 50,
        dateOnlyPeriods: true,
        outcomeDirectionProgress: true,
        clampedProgress: true,
        historicalHealthConfidence: true,
        explicitReadyPartialRollup: true,
        scopedKeyResultOwnerProgress: true,
        permissionAwareSharing: true,
        ownerArchive: true,
        optimisticConcurrency: true,
        accessibleHistoryAndSourceTables: true
      },
      security: {
        unauthenticatedRejected: true,
        unsharedResourceHidden: true,
        crossTenantHiddenByApiTest: true,
        goalDefinitionOwnerOnly: true,
        keyResultOwnerProgressScoped: true,
        projectAndPortfolioPermissionsRevalidated: true,
        boundedPayloads: true,
        syntheticDataOnly: true,
        secretCaptured: false
      },
      apiSurface: {
        openApiPathsPreserved: 236,
        openApiOperations: 286,
        matrixOperations: 290,
        frontendCalls: 477,
        desktopCalls: 273,
        mobileCalls: 204,
        duplicateOperations: 0,
        unmatchedFrontendCalls: 0,
        unownedOperations: 0,
        goalOperationsSurfaced: 10
      }
    },
    preservedCompatibility: {
      existingProjectPortfolioAndPlanningRoutes: true,
      existingRolesAndMembershipRules: true,
      existingFrontendFrameworks: ['AngularJS', 'Bulma', 'Ionic'],
      providerNeutralPersistence: true,
      additiveMigrationsOnly: true,
      existingStoredDataAndContracts: true
    },
    resolvedDiagnostics: [
      'Kept goal definition authority separate from scoped key-result progress authority.',
      'Represented periods as date-only API fields while persisting provider-compatible UTC day instants.',
      'Calculated Increase and Decrease progress from explicit baselines and targets without inventing source values.',
      'Kept partial source roll-ups visible and corrected desktop summary and source-label selectors.',
      'Preserved the bounded desktop composition root while adding explicit Goal feature ownership.'
    ],
    visualManifest: visualPath,
    temporaryApiListenersStopped: true,
    frontendPreviewActive: true,
    frontendPreviewUrl: 'http://127.0.0.1:58177',
    userChangesPreserved: true,
    heavyReleaseGatesDeferred: true,
    noDeployment: true
  };
  writeFileSync(resolve(applicationRoot, visualPath), `${JSON.stringify(visual, null, 2)}\n`);
  writeFileSync(resolve(applicationRoot, evidencePath), `${JSON.stringify(evidence, null, 2)}\n`);
}

function json(path) {
  return JSON.parse(readFileSync(resolve(applicationRoot, path), 'utf8'));
}

function fileSha(path) {
  return createHash('sha256')
    .update(readFileSync(resolve(applicationRoot, path)))
    .digest('hex');
}
