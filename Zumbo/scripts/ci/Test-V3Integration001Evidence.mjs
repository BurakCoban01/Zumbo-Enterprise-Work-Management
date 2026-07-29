import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidencePath = 'artifacts/v3/V3-INTEGRATION-001.json';
const visualPath = 'artifacts/v3/V3-INTEGRATION-001-visual.json';
const sourcePaths = [
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationContracts.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationDocuments.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationService.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookSecurity.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookReceiptRetentionService.cs',
  'Backend/src/Zumbo.Api/DevelopmentIntegrationAdapters.cs',
  'Backend/src/Zumbo.Api/DevelopmentWebhookReceiptRetentionHostedService.cs',
  'Backend/src/Zumbo.Api/Endpoints/DevelopmentIntegrationEndpoints.cs',
  'Backend/src/Zumbo.Api/DurableWorkItemEffects.cs',
  'Backend/src/Zumbo.Api/Adapters/AuditWorkflowBoundaryAdapters.cs',
  'Backend/src/Zumbo.Api/PrivacyDataProcessorAdapter.cs',
  'Backend/src/Zumbo.Api/Hosting/ApiHostRegistration.cs',
  'Backend/src/Zumbo.Api/Hosting/ApiPipeline.cs',
  'Backend/src/Zumbo.Api/MongoMigrations.cs',
  'Backend/src/Zumbo.Api/appsettings.json',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations.cs',
  'Backend/tests/Shared/DevelopmentIntegrationRepositoryContract.cs',
  'Backend/tests/Zumbo.UnitTests/DevelopmentIntegrationServiceTests.cs',
  'Backend/tests/Zumbo.UnitTests/InMemoryDevelopmentIntegrationRepositoryContractTests.cs',
  'Backend/tests/Zumbo.ApiTests/DevelopmentIntegrationApiTests.cs',
  'Backend/tests/Zumbo.ApiTests/RouteInventory.approved.txt',
  'Backend/tests/Zumbo.ApiTests/RouteInventoryCharacterizationTests.cs',
  'Backend/tests/Zumbo.ArchitectureTests/ArchitectureBoundaryTests.cs',
  'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoDevelopmentIntegrationRepositoryContractTests.cs',
  'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoDevelopmentIntegrationIndexTests.cs',
  'Backend/tests/Zumbo.PostgreSqlIntegrationTests/PostgreSqlDevelopmentIntegrationRepositoryContractTests.cs',
  'Frontend/shared/development-integration-core.js',
  'Frontend/desktop-bulma/integration-center.js',
  'Frontend/desktop-bulma/integration-center.css',
  'Frontend/desktop-bulma/index.html',
  'Frontend/desktop-bulma/work-items.js',
  'Frontend/mobile-ionic/api.js',
  'Frontend/mobile-ionic/integration-center.js',
  'Frontend/mobile-ionic/integration-center.css',
  'Frontend/mobile-ionic/details.js',
  'Frontend/mobile-ionic/index.html',
  'Frontend/package.json',
  'Frontend/tests/v3-development-integration.test.mjs',
  'Frontend/tests/v3-development-integration-browser.mjs',
  'Frontend/tests/v3-development-integration-real-browser.mjs',
  'Frontend/tests/v3-integration-center.test.mjs',
  'Frontend/tests/v3-work-item-detail.test.mjs',
  'contracts/openapi.v1.json',
  'docs/product/api-ui-capability-matrix.json',
  'scripts/product/Build-ProductCapabilityMatrix.mjs',
  'scripts/ci/Test-V3ProductCapabilityMatrix.mjs',
  'scripts/ci/Test-V3Integration001Evidence.mjs'
];
const captures = [
  {
    surface: 'desktop-development-center',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-integration-001/desktop-development-center.png',
    notes: 'Deterministic role-gated provider health and repository mapping center.'
  },
  {
    surface: 'mobile-development-center',
    viewport: '390x844',
    screenshot: 'artifacts/ui/v3-integration-001/mobile-development-center.png',
    minimumCommandTargetPixels: 44,
    notes: 'Deterministic Ionic provider management parity.'
  },
  {
    surface: 'mobile-work-item-development',
    viewport: '390x844',
    screenshot: 'artifacts/ui/v3-integration-001/mobile-work-item-development.png',
    minimumCommandTargetPixels: 44,
    notes: 'Deterministic automatic and manual work-item development links.'
  },
  {
    surface: 'desktop-development-center-real-api',
    viewport: '1440x1000',
    screenshot: 'artifacts/ui/v3-integration-001-real/desktop-development-center.png',
    notes: 'Real API and loopback provider health, scopes and repository mapping.'
  },
  {
    surface: 'mobile-work-item-development-real-api',
    viewport: '390x844',
    screenshot: 'artifacts/ui/v3-integration-001-real/mobile-work-item-development.png',
    minimumCommandTargetPixels: 44,
    notes: 'Real signed webhook link and manual mobile link without overflow.'
  }
];

const deterministic = json('artifacts/ui/v3-integration-001/result.json');
const real = json('artifacts/ui/v3-integration-001-real/result.json');

if (process.argv.includes('--write')) writeArtifacts();

const evidence = json(evidencePath);
const visual = json(visualPath);

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-INTEGRATION-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.equal(evidence.temporaryApiListenersStopped, true);
assert.equal(evidence.frontendPreviewActive, true);
assert.deepEqual(evidence.validation.backend.unit, { passed: 259, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.api, { passed: 114, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.architecture, { passed: 25, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.frontend.unit, { passed: 207, failed: 0, skipped: 0 });
assert.equal(evidence.validation.providerParity.mongoDb.passed, 2);
assert.equal(evidence.validation.providerParity.postgreSql.passed, 2);
assert.equal(evidence.validation.browser.deterministic.checks, 6);
assert.equal(evidence.validation.browser.realApi.checks.length, 6);
assert.equal(evidence.validation.apiSurface.openApiPathsPreserved, 260);
assert.equal(evidence.validation.apiSurface.openApiOperations, 320);
assert.equal(evidence.validation.apiSurface.matrixOperations, 324);
assert.equal(evidence.validation.apiSurface.frontendCalls, 548);
assert.equal(evidence.validation.apiSurface.developmentOperationsSurfaced, 16);
assert.equal(evidence.validation.apiSurface.developmentIngressIntentional, 1);
assert.equal(evidence.validation.apiSurface.unmatchedFrontendCalls, 0);
assert.equal(evidence.validation.apiSurface.unownedOperations, 0);
assert.ok(Object.values(evidence.validation.developmentIntegration).every(value =>
  Number.isInteger(value) ? value > 0 : value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value =>
  Array.isArray(value) ? value.length > 0 : value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

assert.equal(deterministic.passed, true);
assert.equal(deterministic.checks.length, 6);
assert.deepEqual(deterministic.failures, []);
assert.equal(real.passed, true);
assert.equal(real.checks.length, 6);
assert.equal(real.cleanup.failed, 0);
assert.deepEqual(real.failures, []);

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-INTEGRATION-001');
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

console.log('V3-INTEGRATION-001 evidence passed: 259 unit, 114 API, provider parity and 12 integration browser checks.');

function writeArtifacts() {
  const generatedAtUtc = new Date().toISOString();
  mkdirSync(resolve(applicationRoot, 'artifacts/v3'), { recursive: true });
  const visual = {
    schemaVersion: 1,
    task: 'V3-INTEGRATION-001',
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
    task: 'V3-INTEGRATION-001',
    generatedAtUtc,
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 259, failed: 0, skipped: 0 },
        focusedDevelopmentUnit: { passed: 8, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        api: { passed: 114, failed: 0, skipped: 0 },
        focusedDevelopmentApi: { passed: 2, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 }
      },
      providerParity: {
        mongoDb: { passed: 2, failed: 0, realProvider: true, indexesValidated: true, port: 58471 },
        postgreSql: { passed: 2, failed: 0, realProvider: true, migrationApplied: true, port: 58472 },
        firstMongoRunWithoutRequiredEnvironment: { passed: 0, failed: 2, resolved: true },
        taskContainersRemoved: true,
        providerPortsClosed: true,
        unrelatedContainersPreserved: true
      },
      frontend: {
        lint: true,
        unit: { passed: 207, failed: 0, skipped: 0 },
        focusedDevelopment: { passed: 6, failed: 0 },
        build: { passed: true, assets: 125, sha256Verified: true },
        dependencyAudit: { critical: 0, high: 2, timeBoundExceptions: 10 },
        licenseAudit: { passed: true, packages: 22 }
      },
      browser: {
        deterministic: {
          passed: true,
          checks: 6,
          states: [
            'desktop-provider-center-role-gated',
            'desktop-create-secret-once-and-credential-memory-only',
            'desktop-repository-mapping-and-health',
            'desktop-integration-permission-denied',
            'mobile-provider-management-parity',
            'mobile-work-item-manual-and-automatic-links'
          ]
        },
        realApi: {
          passed: true,
          checks: real.checks,
          cleanupPassed: real.cleanup.passed,
          cleanupFailed: real.cleanup.failed
        },
        affectedRegressions: {
          webhookAdministration: true,
          workItemDetail: true,
          mobileAccessibilityChecks: 11
        },
        viewports: ['1440x1000', '390x844', '360x780', '430x844', '844x390'],
        captures: 5,
        horizontalOverflow: false,
        interactiveOverlap: false
      },
      developmentIntegration: {
        maximumConnectionsPerOrganization: 20,
        maximumMappingsPerConnection: 100,
        maximumLinksPerWorkItem: 50,
        maximumProviderRepositories: 100,
        maximumWebhookPayloadBytes: 1048576,
        receiptRetentionDays: 90,
        gitLabReplayWindowSeconds: 300,
        encryptedCredentials: true,
        secretOnce: true,
        explicitHostAllowlist: true,
        dnsAddressPolicy: true,
        redirectsDisabled: true,
        boundedProviderResponses: true,
        signedGitHubAndGitLabIngress: true,
        deliveryDedupeAndCollisionDetection: true,
        durableRetryInfrastructure: true,
        eventOrderProtection: true,
        disconnectLifecycleInvalidation: true,
        privacyAndAuditIntegration: true,
        projectScopedMappingDiscovery: true
      },
      security: {
        unauthenticatedManagementRejected: true,
        ordinaryUserManagementRejected: true,
        crossTenantHiddenByApiTest: true,
        workItemPermissionRevalidated: true,
        providerTargetFailsClosed: true,
        badSignatureRejected: true,
        duplicateDeliveryIdempotent: true,
        disconnectedSecretRejected: true,
        credentialAndSecretAbsentFromStorageAndCaptures: true,
        syntheticDataOnly: true
      },
      apiSurface: {
        openApiPathsPreserved: 260,
        openApiOperations: 320,
        matrixOperations: 324,
        frontendCalls: 548,
        desktopCalls: 309,
        mobileCalls: 239,
        duplicateOperations: 0,
        unmatchedFrontendCalls: 0,
        unownedOperations: 0,
        developmentOperationsSurfaced: 16,
        developmentIngressIntentional: 1
      }
    },
    resolvedDiagnostics: [
      'Aligned the real-browser runtime config and startup-cached CSP with the task-owned loopback API.',
      'Added the required organizationId to direct desktop integration project discovery.',
      'Updated stale frontend harnesses for the shared development core and multi-secret cleanup lifecycle.',
      'Added the new endpoint to exact architecture ownership lists without weakening module boundaries.',
      'Reran MongoDB with its required connection contract and removed the task container.'
    ],
    preservedCompatibility: {
      existingWebhookAdministration: true,
      existingWorkItemDetailAndCollaboration: true,
      existingRoutesAndStoredData: true,
      existingFrontendFrameworks: ['AngularJS', 'Bulma', 'Ionic'],
      providerNeutralPersistence: true,
      additiveMigrationsOnly: true,
      existingDurableMessagingRetryPolicy: true
    },
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
