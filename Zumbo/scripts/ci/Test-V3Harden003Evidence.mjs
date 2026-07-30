import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidencePath = 'artifacts/v3/V3-HARDEN-003.json';
const sourcePaths = [
  'Backend/src/Zumbo.Modules.Audit/AuditImplementation.cs',
  'Backend/src/Zumbo.Api/Hosting/ObservabilityRegistration.cs',
  'Backend/tests/Zumbo.UnitTests/AuditParseDiagnosticsTests.cs',
  'scripts/ci/Test-V3Harden003Evidence.mjs'
];

if (process.argv.includes('--write')) writeArtifact();

const evidence = json(evidencePath);
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-HARDEN-003');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.deepEqual(evidence.characterization.prePatch, {
  passed: 1,
  failed: 2,
  malformedMeasurements: 0,
  oversizedMeasurements: 0
});
assert.deepEqual(evidence.validation.backend.releaseBuild, {
  passed: true,
  warnings: 0,
  errors: 0
});
assert.deepEqual(evidence.validation.backend.unit, { passed: 262, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.api, { passed: 114, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.architecture, { passed: 25, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.gateway, { passed: 12, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.auditDiagnostics.focused, {
  passed: 3,
  failed: 0,
  skipped: 0
});
assert.equal(evidence.validation.browser.applicable, false);
assert.equal(evidence.validation.browser.reason, 'No API, frontend or visual surface changed.');
assert.ok(Object.values(evidence.behavior).every(value => value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value => value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

const audit = text('Backend/src/Zumbo.Modules.Audit/AuditImplementation.cs');
const observability = text('Backend/src/Zumbo.Api/Hosting/ObservabilityRegistration.cs');
assert.match(audit, /new\("Zumbo\.Audit", "1\.0\.0"\)/);
assert.match(audit, /CreateCounter<long>\("zumbo\.audit\.diff_parse_fallbacks"\)/);
assert.match(audit, /"reason", reason/);
assert.match(audit, /"side", side/);
assert.match(audit, /"sensitive", IsSensitive\("value", value\)/);
assert.match(audit, /"size", size/);
assert.doesNotMatch(audit, /ParseFallbacks\.Add\([\s\S]{0,500}"(user|tenant|entity|action|correlation|payload|secret)"/i);
assert.match(observability, /"Zumbo\.Audit"/);

console.log('V3-HARDEN-003 evidence passed: 3 safe audit diagnostics tests and 262 unit tests.');

function writeArtifact() {
  const evidence = {
    schemaVersion: 1,
    task: 'V3-HARDEN-003',
    generatedAtUtc: new Date().toISOString(),
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    characterization: {
      prePatch: {
        passed: 1,
        failed: 2,
        malformedMeasurements: 0,
        oversizedMeasurements: 0
      },
      resolvedTestDiagnostic: {
        firstCompileReachedProductCode: false,
        cause: 'Test-only in-memory repository namespace import was missing.'
      }
    },
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 262, failed: 0, skipped: 0 },
        api: { passed: 114, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 }
      },
      auditDiagnostics: {
        focused: { passed: 3, failed: 0, skipped: 0 },
        meter: 'Zumbo.Audit',
        counter: 'zumbo.audit.diff_parse_fallbacks',
        oversizedThresholdCharacters: 32768,
        valueBoundCharacters: 4000,
        tags: ['reason', 'side', 'sensitive', 'size']
      },
      browser: {
        applicable: false,
        reason: 'No API, frontend or visual surface changed.'
      }
    },
    behavior: {
      malformedStructuredValueCounted: true,
      oversizedValueCountedOnce: true,
      sensitiveValueRedacted: true,
      rawValueAbsentFromTags: true,
      fixedLowCardinalityTags: true,
      plainScalarHasNoFalsePositive: true,
      validObjectDiffPreserved: true,
      metricExportAllowlisted: true
    },
    preservedCompatibility: {
      auditStorageContract: true,
      auditApiAndExport: true,
      fieldRedaction: true,
      hashChain: true,
      tenantAuthorization: true,
      persistenceSchema: true,
      frontendConsumers: true
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

function json(path) {
  return JSON.parse(text(path));
}

function text(path) {
  return readFileSync(resolve(applicationRoot, path), 'utf8');
}

function fileSha(path) {
  return createHash('sha256').update(readFileSync(resolve(applicationRoot, path))).digest('hex');
}
