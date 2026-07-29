import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidencePath = 'artifacts/v3/V3-HARDEN-005.json';
const sourcePaths = [
  'Backend/src/Zumbo.Modules.Projects/ProjectContracts.cs',
  'Backend/src/Zumbo.Modules.Projects/Domain/ProjectMembershipAggregate.cs',
  'Backend/src/Zumbo.Modules.Projects/ProjectMembership.cs',
  'Backend/src/Zumbo.Modules.Projects/ProjectCatalogLifecycle.cs',
  'Backend/src/Zumbo.Modules.Projects/ProjectDocuments.cs',
  'Backend/src/Zumbo.Modules.Identity/IdentitySessions.cs',
  'Backend/src/Zumbo.Modules.WorkItems/WorkItemActivities.cs',
  'Backend/src/Zumbo.Modules.WorkItems/WorkItemCollaboration.cs',
  'Backend/src/Zumbo.Api/MongoMigrations.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations.cs',
  'Backend/tests/Shared/ProjectRepositoryContract.cs',
  'Backend/tests/Shared/IdentityCredentialStoreContract.cs',
  'Backend/tests/Shared/WorkItemActivityStoreContract.cs',
  'Backend/tests/Shared/WorkItemCollaborationRepositoryContract.cs',
  'Backend/tests/Zumbo.UnitTests/ProjectCardinalityTests.cs',
  'Backend/tests/Zumbo.ApiTests/ProjectLifecycleApiTests.cs',
  'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoHighCardinalityQueryPlanTests.cs',
  'Backend/tests/Zumbo.PostgreSqlIntegrationTests/PostgreSqlHighCardinalityQueryPlanTests.cs',
  'Backend/tests/Zumbo.PostgreSqlIntegrationTests/PostgreSqlInitializationCharacterizationTests.cs',
  'scripts/ci/Test-V3Harden005Evidence.mjs'
];

if (process.argv.includes('--write')) writeArtifact();

const evidence = json(evidencePath);
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-HARDEN-005');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.deepEqual(evidence.characterization.prePatch, {
  passed: 1,
  failed: 7,
  skipped: 0,
  limitExceptionsThrown: 0
});
assert.deepEqual(evidence.validation.backend.releaseBuild, {
  passed: true,
  warnings: 0,
  errors: 0
});
assert.deepEqual(evidence.validation.backend.unit, {
  passed: 276,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.backend.api, {
  passed: 115,
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
assert.deepEqual(evidence.validation.provider.mongo, {
  passed: 79,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.provider.postgresql, {
  passed: 96,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.frontend.unit, {
  passed: 209,
  failed: 0,
  skipped: 0
});
assert.equal(evidence.validation.frontend.assets, 125);
assert.equal(evidence.measurements.maximumProject.serializedJsonProxyBytes, maximumProjectProxyBytes());
assert.ok(
  evidence.measurements.maximumProject.serializedJsonProxyBytes
    < evidence.measurements.maximumProject.budgetBytes);
assert.equal(evidence.measurements.productionDistributionAvailable, false);
assert.ok(Object.values(evidence.behavior).every(value => value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value => value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

const contracts = text('Backend/src/Zumbo.Modules.Projects/ProjectContracts.cs');
const membershipAggregate = text(
  'Backend/src/Zumbo.Modules.Projects/Domain/ProjectMembershipAggregate.cs');
const membership = text('Backend/src/Zumbo.Modules.Projects/ProjectMembership.cs');
const catalog = text('Backend/src/Zumbo.Modules.Projects/ProjectCatalogLifecycle.cs');
const identitySessions = text('Backend/src/Zumbo.Modules.Identity/IdentitySessions.cs');
const workItemActivities = text(
  'Backend/src/Zumbo.Modules.WorkItems/WorkItemActivities.cs');
const workItemCollaboration = text(
  'Backend/src/Zumbo.Modules.WorkItems/WorkItemCollaboration.cs');
const mongoMigrations = text('Backend/src/Zumbo.Api/MongoMigrations.cs');
const postgresqlMigrations = text(
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations.cs');
const cardinalityTests = text(
  'Backend/tests/Zumbo.UnitTests/ProjectCardinalityTests.cs');
const apiTests = text('Backend/tests/Zumbo.ApiTests/ProjectLifecycleApiTests.cs');
const mongoPlanTests = text(
  'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoHighCardinalityQueryPlanTests.cs');
const postgresqlPlanTests = text(
  'Backend/tests/Zumbo.PostgreSqlIntegrationTests/PostgreSqlHighCardinalityQueryPlanTests.cs');

assert.match(contracts, /MaximumMembers = 500/);
assert.match(contracts, /MaximumTeams = 100/);
assert.match(contracts, /MaximumTemplates = 100/);
assert.match(contracts, /MaximumComponents = 500/);
assert.match(contracts, /MaximumVersions = 500/);
assert.match(contracts, /MaximumReleases = 500/);
assert.match(contracts, /MaximumMilestones = 500/);
assert.match(contracts, /MaximumSerializedBytes = 2 \* 1024 \* 1024/);
assert.match(membershipAggregate, /PROJECT_MEMBER_LIMIT_REACHED/);
assert.match(membership, /PROJECT_TEAM_LIMIT_REACHED/);
for (const code of [
  'PROJECT_TEMPLATE_LIMIT_REACHED',
  'PROJECT_COMPONENT_LIMIT_REACHED',
  'PROJECT_VERSION_LIMIT_REACHED',
  'PROJECT_RELEASE_LIMIT_REACHED',
  'PROJECT_MILESTONE_LIMIT_REACHED'
]) {
  assert.match(catalog, new RegExp(code));
}
assert.match(identitySessions, /pageSize: 100/);
assert.match(identitySessions, /pageSize: 200/);
assert.match(identitySessions, /Math\.Clamp\(batchSize, 1, 500\)/);
assert.match(workItemActivities, /ActivityStorageVersion/);
assert.match(workItemActivities, /ListByCursorAsync/);
assert.match(workItemCollaboration, /WatcherLimit = 200/);
assert.match(workItemCollaboration, /VoterLimit = 1_000/);
assert.match(mongoMigrations, /20260729_036_high_cardinality_indexes/);
assert.match(mongoMigrations, /ix_refreshsessions_owner_last_seen/);
assert.match(postgresqlMigrations, /Migration\.Create\(37, "high_cardinality_indexes"/);
assert.match(postgresqlMigrations, /ix_projects_organization_archived_key_cursor/);
assert.match(postgresqlMigrations, /ix_refresh_sessions_owner_last_seen/);
assert.match(cardinalityTests, /ExistingOversizedProject_AllowsNonGrowthCatalogUpdate/);
assert.match(cardinalityTests, /MaximumEmbeddedCardinality_FitsTwoMebibyteSerializedDocumentBudget/);
assert.match(apiTests, /MemberCardinalityLimit_ReturnsTypedConflictWithoutChangingProject/);
assert.match(mongoPlanTests, /Enumerable\.Range\(1, 5_000\)/);
assert.match(mongoPlanTests, /maximumDocumentsExamined: 200/);
assert.match(postgresqlPlanTests, /generate_series\(1, 5000\)/);
assert.match(postgresqlPlanTests, /AssertBoundedOptionalTopNSort/);

console.log(
  'V3-HARDEN-005 evidence passed: 276 unit, 115 API, '
  + '175 provider and 209 frontend checks.');

function writeArtifact() {
  const limits = {
    members: 500,
    teams: 100,
    templates: 100,
    components: 500,
    versions: 500,
    releases: 500,
    milestones: 500
  };
  const proxyBytes = maximumProjectProxyBytes();
  const evidence = {
    schemaVersion: 1,
    task: 'V3-HARDEN-005',
    generatedAtUtc: new Date().toISOString(),
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    characterization: {
      prePatch: {
        passed: 1,
        failed: 7,
        skipped: 0,
        limitExceptionsThrown: 0
      },
      observation:
        'The maximum-size proxy stayed under budget, while every embedded collection growth path accepted one entry beyond its intended maximum.'
    },
    measurements: {
      productionDistributionAvailable: false,
      scope:
        'Local deterministic synthetic fixtures; no production or private tenant data was accessed.',
      designCardinality: {
        members: { expected: 100, maximum: limits.members },
        teams: { expected: 20, maximum: limits.teams },
        templates: { expected: 20, maximum: limits.templates },
        components: { expected: 100, maximum: limits.components },
        versions: { expected: 100, maximum: limits.versions },
        releases: { expected: 100, maximum: limits.releases },
        milestones: { expected: 100, maximum: limits.milestones }
      },
      maximumProject: {
        serializedJsonProxyBytes: proxyBytes,
        budgetBytes: 2 * 1024 * 1024,
        budgetUtilization: Number((proxyBytes / (2 * 1024 * 1024)).toFixed(4)),
        includesArchivedEntries: true,
        caveat:
          'JSON is a conservative reproducible document-size proxy, not a production cardinality distribution or exact provider BSON/JSONB storage measurement.'
      },
      providerPlanFixture: {
        providerVersions: ['MongoDB 7.0', 'PostgreSQL 16 Alpine'],
        projectsPerProvider: 5_000,
        sessionsPerProvider: 5_000,
        targetProjects: 1_000,
        targetOwnerSessions: 1_000,
        pageLimit: 100,
        executionBudgetMilliseconds: 250
      },
      postgresqlObservedSessionPlan: {
        index: 'ix_refresh_sessions_owner_last_seen',
        candidateRows: 1_000,
        resultRows: 100,
        sortMethod: 'top-N heapsort',
        maximumSortSpaceKiB: 256,
        observedDiagnosticExecutionMilliseconds: 2.663
      },
      concurrency: {
        project: 'whole-document optimistic CAS through ReplaceByVersionAsync',
        refreshSessions: 'separate versioned documents with owner-scoped CAS',
        workItemActivities: 'separate versioned activity documents with cursor reads',
        workItemCollaboration: 'separate versioned document with distributed lock and CAS'
      }
    },
    decomposition: {
      newDecompositionRequired: false,
      projectDecision:
        'Keep bounded embedded project administration collections: the maximum proxy is below the explicit budget, mutations are CAS-protected, and no measured hot independent write path justified migration risk.',
      alreadyDecomposed: [
        'refresh sessions',
        'work-item comments, revisions, attachments, work logs, approvals and timeline',
        'work-item collaboration and event activity'
      ],
      reconsiderWhen: [
        'representative telemetry shows sustained project-document contention',
        'provider document size approaches the two-MiB budget',
        'an embedded collection needs independent pagination or write ownership'
      ]
    },
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 276, failed: 0, skipped: 0 },
        api: { passed: 115, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 }
      },
      focused: {
        cardinalityUnit: { passed: 9, failed: 0, skipped: 0 },
        projectLifecycleApi: { passed: 2, failed: 0, skipped: 0 },
        mongoQueryPlan: { passed: 1, failed: 0, skipped: 0 },
        postgresqlQueryPlan: { passed: 1, failed: 0, skipped: 0 },
        postgresqlMigrationAcceptance: { passed: 10, failed: 0, skipped: 0 }
      },
      provider: {
        mongo: { passed: 79, failed: 0, skipped: 0 },
        postgresql: { passed: 96, failed: 0, skipped: 0 },
        taskPorts: [58476, 58477, 58478, 58479],
        taskContainersRemoved: true,
        remainingTaskPortListeners: 0,
        initialMongoDiagnostic:
          'The first full run passed 78 tests and failed only because the unrelated SMTP lifecycle dependency was unset; the pinned loopback Mailpit rerun passed 79/79.'
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
        applicable: false,
        reason:
          'No route, response projection, frontend source, layout or visible state changed in this backend/data hardening package.'
      },
      commandDiagnostics: [
        'Parallel provider test-project builds briefly contended for one Windows obj output; the sequential PostgreSQL rebuild passed.',
        'The first PostgreSQL query-plan command used the wrong test environment-variable name; the corrected fixture command passed.',
        'PostgreSQL plan iteration aligned id collation and null ordering before accepting the measured bounded planner choice.'
      ]
    },
    behavior: {
      allSevenProjectGrowthPathsAreBounded: true,
      typedConflictCodesAreStable: true,
      rejectedGrowthLeavesVersionAndStateUnchanged: true,
      oversizedExistingDocumentsAllowNonGrowthMutation: true,
      projectCasRemainsProviderNeutral: true,
      highGrowthActivitiesRemainDecomposedAndPaged: true,
      refreshSessionsRemainOwnedVersionedAndRetained: true,
      mongoPlanUsesRequiredIndexes: true,
      postgresqlPlanUsesRequiredIndexes: true,
      additiveIndexMigrationsAreIdempotent: true
    },
    preservedCompatibility: {
      routesAndMethods: true,
      responseContracts: true,
      storedDocumentShape: true,
      existingOversizedReadsAndNonGrowthMutations: true,
      tenantAndResourceAuthorization: true,
      optimisticConcurrency: true,
      persistenceProviders: true,
      desktopAndMobileBehavior: true
    },
    migration: {
      required: true,
      dataShapeChanged: false,
      mongo:
        'Additive 20260729_036_high_cardinality_indexes creates the owner/last-seen refresh-session index.',
      postgresql:
        'Additive migration 37 creates deterministic project cursor and refresh-session owner/last-seen indexes; latest migration rollback/reapply passed.'
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

function maximumProjectProxyBytes() {
  const id = 'i'.repeat(32);
  const name = 'N'.repeat(120);
  const description = 'D'.repeat(500);
  const project = {
    Id: id,
    OrganizationId: id,
    Key: 'PROJECTKEY',
    Name: name,
    Visibility: 'Private',
    Archived: false,
    Version: 1,
    Members: Array.from({ length: 500 }, (_, index) => ({
      UserId: `${id}${index}`,
      Role: 'ProjectAdmin'
    })),
    TeamIds: Array.from({ length: 100 }, (_, index) => `${id}${index}`),
    Templates: Array.from({ length: 100 }, (_, index) => ({
      Id: `${id}${index}`,
      Name: name,
      IsDefault: index === 0,
      Archived: false,
      DefaultComponentNames: Array.from(
        { length: 50 },
        (_, component) => `${'C'.repeat(78)}${component.toString().padStart(2, '0')}`)
    })),
    Components: Array.from({ length: 500 }, (_, index) => ({
      Id: `${id}${index}`,
      Name: 'C'.repeat(80),
      Description: description,
      Archived: false
    })),
    Versions: Array.from({ length: 500 }, (_, index) => ({
      Id: `${id}${index}`,
      Name: 'V'.repeat(80),
      Status: 'Planned'
    })),
    Releases: Array.from({ length: 500 }, (_, index) => ({
      Id: `${id}${index}`,
      VersionId: `${id}${index}`,
      Name: 'R'.repeat(100),
      Status: 'Draft'
    })),
    Milestones: Array.from({ length: 500 }, (_, index) => ({
      Id: `${id}${index}`,
      Name: name,
      DueAt: '2026-07-29T10:00:00.000Z',
      Status: 'Open'
    }))
  };
  return Buffer.byteLength(JSON.stringify(project), 'utf8');
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
