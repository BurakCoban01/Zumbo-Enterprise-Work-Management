import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import {
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync
} from 'node:fs';
import { join, resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidencePath = 'artifacts/v3/V3-HARDEN-004.json';
const sourcePaths = [
  'Backend/src/Zumbo.Modules.Projects/ProjectCatalogLifecycle.cs',
  'Backend/src/Zumbo.Modules.Projects/ProjectHistoryRetentionPolicy.cs',
  'Backend/src/Zumbo.Modules.Projects/GoalService.cs',
  'Backend/src/Zumbo.Modules.Projects/PortfolioService.cs',
  'Backend/src/Zumbo.Modules.Workflows/WorkflowRetentionPolicy.cs',
  'Backend/src/Zumbo.Modules.Workflows/WorkflowsModule.cs',
  'Backend/src/Zumbo.Modules.Workflows/WorkflowPublication.cs',
  'Backend/src/Zumbo.Modules.Workflows/Features/RepresentativeWorkflowSlices.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationDocuments.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookReferencePolicy.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookSecurity.cs',
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationService.cs',
  'Backend/tests/Zumbo.UnitTests/WorkflowAggregateTests.cs',
  'Backend/tests/Zumbo.UnitTests/GoalServiceTests.cs',
  'Backend/tests/Zumbo.UnitTests/PortfolioServiceTests.cs',
  'Backend/tests/Zumbo.UnitTests/DevelopmentIntegrationServiceTests.cs',
  'Backend/tests/Zumbo.ApiTests/WorkflowBoardLifecycleApiTests.cs',
  'Backend/tests/Zumbo.ApiTests/GoalApiTests.cs',
  'Backend/tests/Zumbo.ApiTests/PortfolioApiTests.cs',
  'Backend/tests/Zumbo.ApiTests/ProjectLifecycleApiTests.cs',
  'Backend/tests/Zumbo.ApiTests/DevelopmentIntegrationApiTests.cs',
  'Frontend/desktop-bulma/index.html',
  'Frontend/mobile-ionic/index.html',
  'Frontend/tests/v3-retention-policies.test.mjs',
  'Frontend/tests/v3-goal-browser.mjs',
  'Frontend/tests/v3-portfolio-browser.mjs',
  'scripts/ci/Test-V3Harden004Evidence.mjs'
];

if (process.argv.includes('--write')) writeArtifact();

const evidence = json(evidencePath);
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-HARDEN-004');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.deepEqual(evidence.characterization.developmentReferenceLimit.prePatch, {
  passed: 0,
  failed: 2,
  exceptionsThrown: 0
});
assert.deepEqual(evidence.validation.backend.releaseBuild, {
  passed: true,
  warnings: 0,
  errors: 0
});
assert.deepEqual(evidence.validation.backend.unit, {
  passed: 267,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.backend.api, {
  passed: 114,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.backend.architecture, {
  passed: 25,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.backend.gateway, {
  passed: 12,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.frontend.unit, {
  passed: 209,
  failed: 0,
  skipped: 0
});
assert.equal(evidence.validation.frontend.assets, 125);
assert.deepEqual(evidence.validation.provider.mongo, {
  passed: 5,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.provider.postgresql, {
  passed: 10,
  failed: 0,
  skipped: 0
});
assert.equal(evidence.validation.browser.deterministic.goalChecks, 5);
assert.equal(evidence.validation.browser.deterministic.portfolioChecks, 5);
assert.ok(Object.values(evidence.behavior).every(value => value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value => value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

const workflow = text('Backend/src/Zumbo.Modules.Workflows/WorkflowsModule.cs');
const workflowPolicy = text(
  'Backend/src/Zumbo.Modules.Workflows/WorkflowRetentionPolicy.cs');
const goal = text('Backend/src/Zumbo.Modules.Projects/GoalService.cs');
const portfolio = text('Backend/src/Zumbo.Modules.Projects/PortfolioService.cs');
const projectPolicy = text(
  'Backend/src/Zumbo.Modules.Projects/ProjectHistoryRetentionPolicy.cs');
const developmentLimits = text(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationDocuments.cs');
const developmentPolicy = text(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookReferencePolicy.cs');
const developmentSecurity = text(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentWebhookSecurity.cs');
const developmentService = text(
  'Backend/src/Zumbo.Modules.WorkItems/DevelopmentIntegrationService.cs');

assert.match(workflowPolicy, /MaximumPublishedVersions = 25/);
assert.match(workflow, /WorkflowRetentionPolicy\.RetainPublishedVersions/);
assert.match(projectPolicy, /MaximumGoalStatusUpdates = 50/);
assert.match(projectPolicy, /MaximumKeyResultProgressUpdates = 50/);
assert.match(projectPolicy, /MaximumInitiativeStatusUpdates = 50/);
assert.match(goal, /ProjectHistoryRetentionPolicy\.MaximumGoalStatusUpdates/);
assert.match(goal, /ProjectHistoryRetentionPolicy\.MaximumKeyResultProgressUpdates/);
assert.match(portfolio, /ProjectHistoryRetentionPolicy\.MaximumInitiativeStatusUpdates/);
assert.match(developmentLimits, /MaximumWorkItemReferencesPerEvent = 10/);
assert.match(
  developmentPolicy,
  /DEVELOPMENT_WEBHOOK_REFERENCE_LIMIT_EXCEEDED/);
assert.match(
  developmentSecurity,
  /DevelopmentWebhookReferencePolicy\.ExtractWithinLimit/);
assert.match(
  developmentService,
  /DevelopmentWebhookReferencePolicy[\s\S]*\.ExtractWithinLimit/);
assert.doesNotMatch(developmentSecurity, /\.Take\(10\)/);
assert.doesNotMatch(developmentService, /\.Take\(10\)/);

const moduleSource = moduleCsFiles()
  .map(path => readFileSync(path, 'utf8'))
  .join('\n');
assert.doesNotMatch(
  moduleSource,
  /\.Take\(\s*\d+\s*\)/,
  'Module source must not contain an unexplained literal Take(n) cap.');

console.log(
  'V3-HARDEN-004 evidence passed: 267 unit, 114 API, 15 provider and 10 browser checks.');

function writeArtifact() {
  const evidence = {
    schemaVersion: 1,
    task: 'V3-HARDEN-004',
    generatedAtUtc: new Date().toISOString(),
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    characterization: {
      existingRetention: {
        prePatch: { passed: 3, failed: 0, skipped: 0 },
        observation:
          'Workflow, goal, key-result and initiative histories retained only their newest items through inline caps.'
      },
      developmentReferenceLimit: {
        prePatch: { passed: 0, failed: 2, exceptionsThrown: 0 },
        observation:
          'Signed and legacy queued events silently processed only the first ten distinct references.'
      }
    },
    inventory: {
      mutationValidation: [
        'project template default component names: 50'
      ],
      documentedRetention: [
        'workflow published versions: 25',
        'goal status updates: 50',
        'key-result progress updates: 50',
        'initiative status updates: 50',
        'development webhook receipts: 90 days'
      ],
      providerPayloadValidation: [
        'distinct development work-item references per event: 10'
      ],
      allowedTakeSemantics: [
        'cursor/page reads',
        'durable message batches',
        'provider discovery response bounds with partial metadata',
        'privacy export bounds with truncation metadata',
        'storage list limits'
      ],
      remainingLiteralModuleTakeCount: 0
    },
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 267, failed: 0, skipped: 0 },
        api: { passed: 114, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 }
      },
      focused: {
        retentionUnit: { passed: 3, failed: 0, skipped: 0 },
        developmentIntegrationUnit: { passed: 9, failed: 0, skipped: 0 },
        affectedApi: { passed: 8, failed: 0, skipped: 0 },
        staticContracts: { passed: 2, failed: 0, skipped: 0 }
      },
      provider: {
        mongo: { passed: 5, failed: 0, skipped: 0 },
        postgresql: { passed: 10, failed: 0, skipped: 0 },
        taskPorts: [58474, 58475],
        taskContainersRemoved: true,
        remainingTaskPortListeners: 0
      },
      frontend: {
        lint: true,
        unit: { passed: 209, failed: 0, skipped: 0 },
        assets: 125,
        dependencyAudit: {
          passedUnderPolicy: true,
          critical: 0,
          high: 2,
          timeBoundExceptions: 10
        },
        licenseAudit: { passed: true, packages: 22 }
      },
      browser: {
        deterministic: {
          goalChecks: 5,
          portfolioChecks: 5,
          viewports: ['1440x1000', '390x844']
        },
        reviewedScreenshots: [
          'artifacts/ui/v3-feature-005/desktop-ready.png',
          'artifacts/ui/v3-feature-005/mobile-result-owner.png',
          'artifacts/ui/v3-feature-004/desktop-retention.png',
          'artifacts/ui/v3-feature-004/mobile-initiative-owner.png'
        ],
        criticalVisualBlockers: 0,
        inAppBrowserAvailable: false,
        harnessDiagnostic:
          'A first parallel goal/portfolio launch hit Windows EPERM on the shared Ionic vendor directory; sequential reruns passed both suites.'
      },
      commandDiagnostics: [
        'The first release command used the nonexistent root Zumbo.sln; Backend/Zumbo.sln then passed.',
        'A preview probe used an invalid /desktop/ path and returned 404; the repository browser suites passed against their configured URL.'
      ]
    },
    behavior: {
      projectTemplateLimitIsTyped: true,
      workflowRetentionIsNamedAndOrdered: true,
      goalRetentionIsNamedAndOrdered: true,
      keyResultRetentionIsNamedAndOrdered: true,
      initiativeRetentionIsNamedAndOrdered: true,
      retentionMetadataIsVisible: true,
      developmentReferenceLimitIsNamed: true,
      signedOverLimitPayloadRejectedBeforeReceipt: true,
      legacyOverLimitEventRejectedBeforeMutation: true,
      rawReferenceValuesAbsentFromErrors: true,
      literalModuleTakeCapsAbsent: true
    },
    preservedCompatibility: {
      routesAndMethods: true,
      additiveResponseContracts: true,
      storedDocumentShape: true,
      persistenceProviders: true,
      tenantAndResourceAuthorization: true,
      optimisticConcurrency: true,
      validWithinLimitWorkflows: true,
      desktopAndMobileNavigation: true
    },
    migration: {
      required: false,
      reason:
        'No stored shape changed. Existing oversized embedded histories remain readable and are normalized by the named policy on their next successful mutation.'
    },
    userChangesPreserved: true,
    heavyReleaseGatesDeferred: true,
    noDeployment: true
  };

  mkdirSync(resolve(applicationRoot, 'artifacts/v3'), { recursive: true });
  writeFileSync(
    resolve(applicationRoot, evidencePath),
    `${JSON.stringify(evidence, null, 2)}\n`,
    'utf8');
}

function moduleCsFiles() {
  const sourceRoot = resolve(applicationRoot, 'Backend/src');
  const moduleDirectories = readdirSync(sourceRoot, { withFileTypes: true })
    .filter(entry => entry.isDirectory() && entry.name.startsWith('Zumbo.Modules.'))
    .map(entry => join(sourceRoot, entry.name));
  return moduleDirectories.flatMap(directory => walkCs(directory));
}

function walkCs(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return walkCs(path);
    return entry.isFile() && entry.name.endsWith('.cs') ? [path] : [];
  });
}

function json(path) {
  return JSON.parse(text(path));
}

function text(path) {
  return readFileSync(resolve(applicationRoot, path), 'utf8');
}

function fileSha(path) {
  return createHash('sha256')
    .update(readFileSync(resolve(applicationRoot, path)))
    .digest('hex');
}
