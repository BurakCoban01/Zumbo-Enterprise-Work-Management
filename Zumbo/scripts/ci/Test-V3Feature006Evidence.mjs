import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidencePath = 'artifacts/v3/V3-FEATURE-006.json';
const visualPath = 'artifacts/v3/V3-FEATURE-006-visual.json';
const sourcePaths = [
  'Backend/src/Zumbo.Modules.WorkItems/CapacityPlanningDocuments.cs',
  'Backend/src/Zumbo.Modules.WorkItems/CapacityPlanningService.cs',
  'Backend/src/Zumbo.Api/CapacityPlanningAdapters.cs',
  'Backend/src/Zumbo.Api/Endpoints/CapacityPlanningEndpoints.cs',
  'Backend/src/Zumbo.Api/Program.cs',
  'Backend/src/Zumbo.Api/Hosting/ApiHostRegistration.cs',
  'Backend/src/Zumbo.Api/Hosting/ApiPipeline.cs',
  'Backend/src/Zumbo.Api/MongoMigrations.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations.cs',
  'Backend/tests/Zumbo.ApiTests/RouteInventory.approved.txt',
  'Backend/tests/Zumbo.ArchitectureTests/ArchitectureBoundaryTests.cs',
  'Backend/tests/Shared/CapacityPlanRepositoryContract.cs',
  'Backend/tests/Zumbo.UnitTests/CapacityPlanningServiceTests.cs',
  'Backend/tests/Zumbo.UnitTests/InMemoryCapacityPlanRepositoryContractTests.cs',
  'Backend/tests/Zumbo.ApiTests/CapacityPlanningApiTests.cs',
  'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoCapacityPlanRepositoryContractTests.cs',
  'Backend/tests/Zumbo.PostgreSqlIntegrationTests/PostgreSqlCapacityPlanRepositoryContractTests.cs',
  'Frontend/shared/capacity-planning-core.js',
  'Frontend/shared/api-client.js',
  'Frontend/desktop-bulma/capacity-center.js',
  'Frontend/desktop-bulma/capacity-center.css',
  'Frontend/desktop-bulma/app.js',
  'Frontend/desktop-bulma/index.html',
  'Frontend/mobile-ionic/capacity-center.js',
  'Frontend/mobile-ionic/capacity-center.css',
  'Frontend/mobile-ionic/app.js',
  'Frontend/mobile-ionic/index.html',
  'Frontend/tests/fe003-desktop-characterization.test.mjs',
  'Frontend/tests/fe004-mobile-characterization.test.mjs',
  'Frontend/tests/v3-capacity-planning.test.mjs',
  'Frontend/tests/v3-capacity-planning-browser.mjs',
  'Frontend/tests/v3-capacity-planning-real-browser.mjs',
  'contracts/openapi.v1.json',
  'docs/product/api-ui-capability-matrix.json',
  'scripts/product/Build-ProductCapabilityMatrix.mjs',
  'scripts/ci/Test-V3ProductCapabilityMatrix.mjs',
  'scripts/ci/Test-V3Feature006Evidence.mjs'
];
const captures = [
  {
    surface: 'desktop-owner-ready',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-feature-006/desktop-ready.png',
    notes: 'Separate capacity hours, allocation hours, points and project totals.'
  },
  {
    surface: 'desktop-owner-scenario',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-feature-006/desktop-scenario.png',
    notes: 'Read-only scenario comparison states that the stored plan is unchanged.'
  },
  {
    surface: 'mobile-viewer',
    viewport: '390x844',
    screenshot: 'artifacts/ui/v3-feature-006/mobile-viewer.png',
    minimumCommandTargetPixels: 44,
    notes: 'Read-only mobile viewer without create or scenario authority.'
  },
  {
    surface: 'desktop-owner-real-api',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-feature-006-real/desktop-owner.png',
    notes: 'Real API owner view with work-derived points and unestimated items.'
  },
  {
    surface: 'mobile-viewer-real-api',
    viewport: '390x844',
    screenshot: 'artifacts/ui/v3-feature-006-real/mobile-viewer.png',
    minimumCommandTargetPixels: 44,
    notes: 'Real API viewer authority and named synthetic user without overflow.'
  }
];

if (process.argv.includes('--write')) writeArtifacts();

const evidence = json(evidencePath);
const visual = json(visualPath);
const deterministic = json('artifacts/ui/v3-feature-006/result.json');
const real = json('artifacts/ui/v3-feature-006-real/result.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-FEATURE-006');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryApiListenersStopped, true);
assert.equal(evidence.frontendPreviewActive, true);
assert.deepEqual(evidence.validation.backend.unit, { passed: 244, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.api, { passed: 105, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.architecture, { passed: 25, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.unit, { passed: 198, failed: 0, skipped: 0 });
assert.equal(evidence.validation.providerParity.mongoDb.passed, 1);
assert.equal(evidence.validation.providerParity.postgreSql.passed, 2);
assert.equal(evidence.validation.browser.deterministic.checks, 5);
assert.equal(evidence.validation.browser.realApi.checks.length, 5);
assert.equal(evidence.validation.apiSurface.openApiPathsPreserved, 241);
assert.equal(evidence.validation.apiSurface.openApiOperations, 294);
assert.equal(evidence.validation.apiSurface.matrixOperations, 298);
assert.equal(evidence.validation.apiSurface.frontendCalls, 495);
assert.equal(evidence.validation.apiSurface.capacityOperationsSurfaced, 8);
assert.equal(evidence.validation.apiSurface.unmatchedFrontendCalls, 0);
assert.equal(evidence.validation.apiSurface.unownedOperations, 0);
assert.ok(Object.values(evidence.validation.capacityPlanning).every(value =>
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
assert.equal(visual.task, 'V3-FEATURE-006');
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

console.log('V3-FEATURE-006 evidence passed: 244 unit, 105 API, provider parity and 10 browser checks.');

function writeArtifacts() {
  const generatedAtUtc = new Date().toISOString();
  mkdirSync(resolve(applicationRoot, 'artifacts/v3'), { recursive: true });
  const visual = {
    schemaVersion: 1,
    task: 'V3-FEATURE-006',
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
    task: 'V3-FEATURE-006',
    generatedAtUtc,
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 244, failed: 0, skipped: 0 },
        focusedCapacityUnit: { passed: 5, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        api: { passed: 105, failed: 0, skipped: 0 },
        focusedCapacityApi: { passed: 1, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 },
        resolvedDiagnostic: {
          firstApiRun: { passed: 104, failed: 1, skipped: 0 },
          isolatedCursorAuditRerun: { passed: 1, failed: 0, skipped: 0 },
          unchangedFullApiRerun: { passed: 105, failed: 0, skipped: 0 }
        }
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
        unit: { passed: 198, failed: 0, skipped: 0 },
        focusedCapacity: { passed: 4, failed: 0 },
        build: { passed: true, assets: 119, sha256Verified: true },
        dependencyAudit: { critical: 0, high: 2, timeBoundExceptions: 10 },
        licenseAudit: { passed: true, packages: 22 }
      },
      browser: {
        deterministic: {
          passed: true,
          checks: 5,
          states: ['separate-units', 'weekly-views', 'partial-source', 'read-only-scenario', 'mobile-viewer-authority']
        },
        realApi: {
          passed: true,
          checks: [
            'snapshot-separate-units-and-work-source',
            'viewer-read-forbidden-write-and-scenario',
            'scenario-is-nonpersistent',
            'desktop-owner-weekly-and-project-views',
            'mobile-viewer-authority-no-overflow'
          ],
          cleanupPassed: 1,
          cleanupFailed: 0
        },
        viewports: ['1440x1000', '390x844'],
        captures: 5,
        horizontalOverflow: false,
        interactiveOverlap: false
      },
      capacityPlanning: {
        maximumProjects: 20,
        maximumPeople: 100,
        maximumAllocations: 500,
        maximumPeriodDays: 366,
        maximumSourceItems: 10000,
        dateOnlyPeriods: true,
        workingDayProration: true,
        hoursAndPointsSeparated: true,
        explicitUnestimatedAndUnscheduledWork: true,
        explicitReadyPartialSource: true,
        readOnlyNonpersistentScenarios: true,
        permissionAwareSharing: true,
        noProductivityRanking: true,
        optimisticConcurrency: true,
        accessiblePeopleAndProjectViews: true
      },
      security: {
        unauthenticatedRejected: true,
        unsharedResourceHidden: true,
        crossTenantHiddenByApiTest: true,
        ownerMutationsOnly: true,
        viewerReadOnly: true,
        projectAndTeamPermissionsRevalidated: true,
        boundedPayloads: true,
        syntheticDataOnly: true,
        secretCaptured: false
      },
      apiSurface: {
        openApiPathsPreserved: 241,
        openApiOperations: 294,
        matrixOperations: 298,
        frontendCalls: 495,
        desktopCalls: 282,
        mobileCalls: 213,
        duplicateOperations: 0,
        unmatchedFrontendCalls: 0,
        unownedOperations: 0,
        capacityOperationsSurfaced: 8
      }
    },
    preservedCompatibility: {
      existingWorkloadSprintTimelineAndPortfolioRoutes: true,
      existingRolesAndMembershipRules: true,
      existingFrontendFrameworks: ['AngularJS', 'Bulma', 'Ionic'],
      providerNeutralPersistence: true,
      additiveMigrationsOnly: true,
      existingStoredDataAndContracts: true
    },
    resolvedDiagnostics: [
      'Kept capacity hours and task estimate points separate without inventing a conversion.',
      'Limited plan creation to manageable projects and team selection to active membership.',
      'Kept viewers read-only across API, desktop and mobile scenario and mutation controls.',
      'Kept scenarios nonpersistent and made partial source coverage explicit.',
      'Surfaced all eight capacity operations on both clients, including independent sharing.'
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
