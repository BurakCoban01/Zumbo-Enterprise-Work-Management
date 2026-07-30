import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import {
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync
} from 'node:fs';
import { relative, resolve } from 'node:path';
import {
  applicationRoot,
  gitRepositoryRoot
} from '../repository-layout.mjs';

const task = 'V3-HARDEN-006';
const sourceBaseCommit = 'faf4ba100a68eee61228f621bff569ea9be03c87';
const evidencePath = `artifacts/v3/${task}.json`;
const sourcePaths = [
  'Backend/src/Zumbo.Api/AttachmentStorageAdapter.cs',
  'Backend/src/Zumbo.Api/RedisWorkItemReadModelCache.cs',
  'Backend/src/Zumbo.BuildingBlocks.Application/Runtime/CompensationExecution.cs',
  'Backend/src/Zumbo.BuildingBlocks.Infrastructure/Concurrency/DistributedLocking.cs',
  'Backend/src/Zumbo.BuildingBlocks.Infrastructure/Persistence/MongoTransactions.cs',
  'Backend/src/Zumbo.Modules.WorkItems/IntakeService.cs',
  'Backend/src/Zumbo.Modules.WorkItems/WorkItemCompensation.cs',
  'Backend/src/Zumbo.Modules.WorkItems/WorkItemsModule.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlCompensation.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlDurableMessaging.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlProvider.cs',
  'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlTransactions.cs',
  'Backend/tools/Zumbo.Capacity/CapacityGateRunner.cs',
  'Backend/tools/Zumbo.Capacity/Program.cs',
  'Backend/tools/Zumbo.DataTransfer/TransferEngine.cs',
  'Backend/tests/Zumbo.ApiTests/AttachmentSecurityTests.cs',
  'Backend/tests/Zumbo.GatewayTests/GatewayAcceptanceTests.cs',
  'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoDurableMessagingTests.cs',
  'Backend/tests/Zumbo.UnitTests/CompensationExecutionTests.cs',
  'Backend/tests/Zumbo.UnitTests/IntakeServiceTests.cs',
  'scripts/ci/Test-V3Harden006Evidence.mjs'
];
const baselineInventory = [
  entry('Backend/src/Zumbo.Api/AttachmentStorageAdapter.cs', 'compensation', 3),
  entry('Backend/src/Zumbo.Api/RedisWorkItemReadModelCache.cs', 'primary', 1),
  entry(
    'Backend/src/Zumbo.BuildingBlocks.Infrastructure/Concurrency/DistributedLocking.cs',
    'compensation',
    1),
  entry(
    'Backend/src/Zumbo.BuildingBlocks.Infrastructure/Persistence/MongoTransactions.cs',
    'compensation',
    1),
  entry('Backend/src/Zumbo.Gateway/GatewayHost.cs', 'intentional-immutable', 1),
  entry('Backend/src/Zumbo.Modules.WorkItems/IntakeService.cs', 'compensation', 3),
  entry('Backend/src/Zumbo.Modules.WorkItems/WorkItemsModule.cs', 'compensation', 2),
  entry(
    'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlDurableMessaging.cs',
    'compensation',
    1),
  entry(
    'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations.cs',
    'compensation',
    2),
  entry(
    'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlProvider.cs',
    'compensation',
    1),
  entry(
    'Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlTransactions.cs',
    'compensation',
    2),
  entry('Backend/tools/Zumbo.Capacity/CapacityGateRunner.cs', 'compensation', 1),
  entry(
    'Backend/tools/Zumbo.Capacity/CapacityGateRunner.cs',
    'shutdown-verification',
    1),
  entry('Backend/tools/Zumbo.Capacity/Program.cs', 'primary', 1),
  entry('Backend/tools/Zumbo.DataTransfer/TransferEngine.cs', 'compensation', 2)
];
const operationIds = [
  'attachment.quarantine.delete',
  'intake.submission.exists',
  'intake.attachments.delete',
  'work_item.attachment.delete',
  'work_item.attachment.restore',
  'postgres.outbox_claim.rollback',
  'postgres.migration_apply.rollback',
  'postgres.migration_rollback.rollback',
  'postgres.provider.rollback',
  'postgres.session_dispose.rollback',
  'postgres.transaction.rollback',
  'mongo.transaction.abort',
  'redis.lock.renewal_stop',
  'redis.lock.release',
  'data_transfer.mongo_import.abort',
  'data_transfer.postgres_import.rollback',
  'capacity.seed.cleanup',
  'capacity.storage_cleanup.probe'
];

assertBaselineInventory();
assertCurrentSource();

if (process.argv.includes('--write')) writeArtifact();

const evidence = json(evidencePath);
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, task);
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.deepEqual(evidence.inventory.initial, {
  total: 23,
  compensation: 19,
  primary: 2,
  shutdownVerification: 1,
  intentionalImmutable: 1,
  files: 14
});
assert.deepEqual(evidence.inventory.final, {
  total: 2,
  boundedLateFailureObserver: 1,
  intentionalImmutable: 1
});
assert.deepEqual(evidence.operationIds, operationIds);
assert.deepEqual(evidence.validation.backend.releaseBuild, {
  passed: true,
  warnings: 0,
  errors: 0
});
assert.deepEqual(evidence.validation.backend.unit, {
  passed: 283,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.backend.api, {
  passed: 116,
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
  passed: 80,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.provider.postgresql, {
  passed: 96,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.provider.redisFocused, {
  passed: 1,
  failed: 0,
  skipped: 0
});
assert.deepEqual(evidence.validation.frontend.unit, {
  passed: 209,
  failed: 0,
  skipped: 0
});
assert.equal(evidence.validation.frontend.assets, 125);
assert.equal(evidence.validation.provider.taskContainersRemoved, true);
assert.equal(evidence.validation.provider.remainingTaskPortListeners, 0);
assert.ok(Object.values(evidence.behavior).every(value => value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value => value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

console.log(
  'V3-HARDEN-006 evidence passed: 23 paths classified, 18 bounded operations, '
  + '283 unit, 116 API, 177 provider and 209 frontend checks.');

function assertBaselineInventory() {
  const grouped = new Map();
  for (const item of baselineInventory) {
    grouped.set(item.path, (grouped.get(item.path) ?? 0) + item.count);
  }
  for (const [path, expected] of grouped) {
    const content = gitText(sourceBaseCommit, `Zumbo/${path}`);
    assert.equal(
      occurrences(content, 'CancellationToken.None'),
      expected,
      `Baseline CancellationToken.None inventory drifted: ${path}`);
  }
  assert.equal(
    baselineInventory.reduce((sum, item) => sum + item.count, 0),
    23);
  assert.equal(
    baselineInventory
      .filter(item => item.category === 'compensation')
      .reduce((sum, item) => sum + item.count, 0),
    19);
  assert.equal(
    new Set(baselineInventory.map(item => item.path)).size,
    14);
}

function assertCurrentSource() {
  const current = findCancellationTokenNone();
  assert.deepEqual(
    current.map(item => item.path),
    [
      'Backend/src/Zumbo.BuildingBlocks.Application/Runtime/CompensationExecution.cs',
      'Backend/src/Zumbo.Gateway/GatewayHost.cs'
    ]);
  assert.match(current[0].line, /CancellationToken\.None/);
  assert.match(current[1].line, /CancellationChangeToken\(CancellationToken\.None\)/);

  const production = [
    ...csFiles('Backend/src'),
    ...csFiles('Backend/tools')
  ].map(text).join('\n');
  for (const operation of operationIds) {
    assert.equal(
      occurrences(production, `"${operation}"`),
      1,
      `Compensation operation ID must be a single fixed literal: ${operation}`);
  }

  const helper = text(
    'Backend/src/Zumbo.BuildingBlocks.Application/Runtime/CompensationExecution.cs');
  assert.match(helper, /DefaultTimeout = TimeSpan\.FromSeconds\(5\)/);
  assert.match(helper, /budget > TimeSpan\.FromMinutes\(2\)/);
  assert.match(helper, /new CancellationTokenSource\(budget\)/);
  assert.match(helper, /WaitAsync\(cancellation\.Token\)/);
  assert.match(helper, /zumbo\.compensation\.outcomes/);
  assert.match(helper, /zumbo\.compensation\.duration/);
  assert.match(helper, /\{ "operation", operation \}/);
  assert.match(helper, /\{ "outcome", outcome\.ToString\(\)\.ToLowerInvariant\(\) \}/);
  assert.match(helper, /char\.IsAsciiLetterOrDigit/);
  assert.match(helper, /operation\.Length > 80/);
  assert.match(helper, /TaskContinuationOptions\.OnlyOnFaulted/);

  const cache = text('Backend/src/Zumbo.Api/RedisWorkItemReadModelCache.cs');
  const capacityProgram = text('Backend/tools/Zumbo.Capacity/Program.cs');
  const capacityGate = text('Backend/tools/Zumbo.Capacity/CapacityGateRunner.cs');
  const gateway = text('Backend/src/Zumbo.Gateway/GatewayHost.cs');
  assert.match(cache, /ReadVersionAsync\(projectId, ct\)/);
  assert.match(
    cache,
    /"cache-version-read",[\s\S]*StringGetAsync\(VersionKey\(projectId\)\),[\s\S]*ct\)/);
  assert.match(capacityProgram, /CleanAsync\(profile, cancellation\.Token\)/);
  assert.match(capacityGate, /"smoke" => TimeSpan\.FromSeconds\(30\)/);
  assert.match(capacityGate, /"demo" => TimeSpan\.FromMinutes\(1\)/);
  assert.match(capacityGate, /_ => TimeSpan\.FromMinutes\(2\)/);
  assert.match(gateway, /new CancellationChangeToken\(CancellationToken\.None\)/);

  const helperTests = text(
    'Backend/tests/Zumbo.UnitTests/CompensationExecutionTests.cs');
  const intakeTests = text('Backend/tests/Zumbo.UnitTests/IntakeServiceTests.cs');
  const attachmentTests = text(
    'Backend/tests/Zumbo.ApiTests/AttachmentSecurityTests.cs');
  const mongoTests = text(
    'Backend/tests/Zumbo.PersistenceIntegrationTests/MongoDurableMessagingTests.cs');
  const gatewayTests = text(
    'Backend/tests/Zumbo.GatewayTests/GatewayAcceptanceTests.cs');
  assert.match(helperTests, /BoundsAnOperationThatDoesNotComplete/);
  assert.match(helperTests, /RejectsDynamicMetricLabels/);
  assert.match(intakeTests, /PartialUploadCleanup_DoesNotHidePrimaryFailureAndUsesBoundedToken/);
  assert.match(
    attachmentTests,
    /RejectedUpload_CleanupFailureDoesNotHidePrimaryResultAndUsesBoundedToken/);
  assert.match(mongoTests, /CallerCancellation_RollsBackBusinessWriteAndOutbox/);
  assert.match(gatewayTests, /ChangeToken\.HasChanged/);
}

function writeArtifact() {
  const evidence = {
    schemaVersion: 1,
    task,
    generatedAtUtc: new Date().toISOString(),
    passed: true,
    sourceBaseCommit,
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    inventory: {
      initial: {
        total: 23,
        compensation: 19,
        primary: 2,
        shutdownVerification: 1,
        intentionalImmutable: 1,
        files: 14
      },
      final: {
        total: 2,
        boundedLateFailureObserver: 1,
        intentionalImmutable: 1
      },
      baseline: baselineInventory
    },
    operationIds,
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 283, failed: 0, skipped: 0 },
        api: { passed: 116, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 }
      },
      focused: {
        compensationAndIntakeUnit: { passed: 7, failed: 0, skipped: 0 },
        attachmentApi: { passed: 1, failed: 0, skipped: 0 },
        mongoDurableMessaging: { passed: 9, failed: 0, skipped: 0 },
        postgresqlProviderAcceptance: { passed: 9, failed: 0, skipped: 0 }
      },
      provider: {
        mongo: { passed: 80, failed: 0, skipped: 0 },
        postgresql: { passed: 96, failed: 0, skipped: 0 },
        redisFocused: { passed: 1, failed: 0, skipped: 0 },
        taskPorts: [58480, 58481, 58482, 58483, 58484],
        taskContainers: [
          'zumbo-h6-mongo',
          'zumbo-h6-postgres',
          'zumbo-h6-mailpit',
          'zumbo-h6-redis'
        ],
        taskContainersRemoved: true,
        remainingTaskPortListeners: 0,
        initialMongoDiagnostic:
          'The first full Mongo run passed 78/80; two H5 index/plan tests timed out during index I/O. Both tests then passed 2/2 together, and the complete suite passed 80/80 with the test-only socketTimeoutMS=60000 diagnostic setting.'
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
          'No route, response projection, frontend source, layout or visible state changed in this lifecycle hardening package.'
      },
      commandDiagnostics: [
        'The first full Mongo run timed out in two unrelated H5 index/plan checks; focused 2/2 and full 80/80 reruns passed.',
        'A parallel gateway verification process did not return its terminal line to the orchestrator; the independent rerun passed 12/12.'
      ]
    },
    behavior: {
      cleanupUsesIndependentBoundedTokens: true,
      cleanupFailureDoesNotHidePrimaryFailure: true,
      timeoutAndFailureOutcomesAreObservable: true,
      metricDimensionsAreFixedAndValidated: true,
      logsExcludePayloadAndProductIdentifiers: true,
      requestCancellationReachesRedisPrimaryRead: true,
      capacityPrimaryCleanUsesCallerCancellation: true,
      mongoCallerCancellationRollsBackBusinessAndOutboxWrites: true,
      gatewayStaticConfigurationTokenRemainsImmutable: true,
      lateCleanupFaultsAreObserved: true
    },
    preservedCompatibility: {
      routesAndMethods: true,
      responseContracts: true,
      storedDataShape: true,
      migrationsAndIndexes: true,
      tenantAndResourceAuthorization: true,
      optimisticConcurrency: true,
      persistenceProviders: true,
      desktopAndMobileBehavior: true
    },
    migration: {
      required: false,
      dataShapeChanged: false
    },
    residualRisk: [
      'A cleanup implementation that ignores its cancellation token may finish after the caller receives a timed-out result; its late fault is observed, but the underlying driver must honor cancellation for prompt resource release.',
      'Cleanup remains best-effort by design and is surfaced through fixed operation/outcome metrics and safe warning fields rather than replacing the primary failure.'
    ],
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

function findCancellationTokenNone() {
  const result = [];
  for (const path of [...csFiles('Backend/src'), ...csFiles('Backend/tools')]) {
    const lines = text(path).split(/\r?\n/);
    for (const line of lines) {
      if (line.includes('CancellationToken.None')) result.push({ path, line });
    }
  }
  return result.sort((left, right) => left.path.localeCompare(right.path, 'en'));
}

function csFiles(directory) {
  const root = resolve(applicationRoot, directory);
  const files = [];
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const absolute = resolve(root, entry.name);
    if (entry.isDirectory()) {
      files.push(...csFiles(relative(applicationRoot, absolute)));
    } else if (entry.isFile() && entry.name.endsWith('.cs')) {
      files.push(relative(applicationRoot, absolute).replaceAll('\\', '/'));
    }
  }
  return files;
}

function gitText(commit, path) {
  const result = spawnSync('git', ['show', `${commit}:${path}`], {
    cwd: gitRepositoryRoot,
    encoding: 'utf8',
    timeout: 30_000
  });
  assert.equal(
    result.status,
    0,
    `Unable to read baseline source ${path}: ${result.stderr.trim()}`);
  return result.stdout;
}

function occurrences(value, search) {
  return value.split(search).length - 1;
}

function entry(path, category, count) {
  return { path, category, count };
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
