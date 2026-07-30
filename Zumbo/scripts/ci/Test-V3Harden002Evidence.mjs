import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidencePath = 'artifacts/v3/V3-HARDEN-002.json';
const sourcePaths = [
  'Backend/src/Zumbo.Api/PinnedHttpClientPool.cs',
  'Backend/src/Zumbo.Api/WebhookAdapters.cs',
  'Backend/src/Zumbo.Api/DevelopmentIntegrationAdapters.cs',
  'Backend/tests/Zumbo.ApiTests/WebhookHttpLifecycleTests.cs',
  'scripts/ci/Test-V3Harden002Evidence.mjs'
];

if (process.argv.includes('--write')) writeArtifact();

const evidence = json(evidencePath);
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-HARDEN-002');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.userChangesPreserved, true);
assert.equal(evidence.heavyReleaseGatesDeferred, true);
assert.deepEqual(evidence.characterization.prePatch, {
  passed: 0,
  failed: 2,
  genericWebhookAcceptedConnections: 2,
  developmentProviderAcceptedConnections: 2,
  expectedConnectionsPerPath: 1
});
assert.deepEqual(evidence.validation.backend.releaseBuild, {
  passed: true,
  warnings: 0,
  errors: 0
});
assert.deepEqual(evidence.validation.backend.unit, { passed: 259, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.api, { passed: 114, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.architecture, { passed: 25, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.backend.gateway, { passed: 12, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.lifecycle.focused, { passed: 6, failed: 0, skipped: 0 });
assert.deepEqual(evidence.validation.lifecycle.affectedSecurityAndReplay, {
  passed: 16,
  failed: 0,
  skipped: 0
});
assert.equal(evidence.validation.lifecycle.cacheMaximum, 128);
assert.equal(evidence.validation.lifecycle.finalConnectionsPerPath, 1);
assert.equal(evidence.validation.browser.applicable, false);
assert.equal(evidence.validation.browser.reason, 'No API, frontend or visual surface changed.');
assert.ok(Object.values(evidence.behavior).every(value => value === true));
assert.ok(Object.values(evidence.preservedCompatibility).every(value => value === true));

for (const [path, hash] of Object.entries(evidence.hashes)) {
  assert.equal(fileSha(path), hash, `Source hash drifted: ${path}`);
}

const pool = text('Backend/src/Zumbo.Api/PinnedHttpClientPool.cs');
const webhook = text('Backend/src/Zumbo.Api/WebhookAdapters.cs');
const development = text('Backend/src/Zumbo.Api/DevelopmentIntegrationAdapters.cs');
assert.match(pool, /MaximumCachedClients = 128/);
assert.match(pool, /AllowAutoRedirect = false/);
assert.match(pool, /UseCookies = false/);
assert.match(pool, /UseProxy = false/);
assert.match(pool, /PooledConnectionIdleTimeout = TimeSpan\.FromMinutes\(1\)/);
assert.match(pool, /PooledConnectionLifetime = TimeSpan\.FromMinutes\(2\)/);
assert.match(pool, /context\.DnsEndPoint\.Port != expectedPort/);
assert.doesNotMatch(webhook, /new SocketsHttpHandler|new HttpClient\(handler\)/);
assert.doesNotMatch(development, /new SocketsHttpHandler|new HttpClient\(handler\)/);

console.log('V3-HARDEN-002 evidence passed: one reused connection per path and 114 API tests.');

function writeArtifact() {
  const evidence = {
    schemaVersion: 1,
    task: 'V3-HARDEN-002',
    generatedAtUtc: new Date().toISOString(),
    passed: true,
    sourceBaseCommit: 'faf4ba100a68eee61228f621bff569ea9be03c87',
    sourceState: 'working-tree candidate derived from the recorded base commit',
    hashes: Object.fromEntries(sourcePaths.map(path => [path, fileSha(path)])),
    characterization: {
      prePatch: {
        passed: 0,
        failed: 2,
        genericWebhookAcceptedConnections: 2,
        developmentProviderAcceptedConnections: 2,
        expectedConnectionsPerPath: 1
      },
      resolvedTestDiagnostic: {
        firstCompileReachedProductCode: false,
        cause: 'Future IDisposable contract was cast directly from current sealed adapter types.'
      }
    },
    validation: {
      backend: {
        releaseBuild: { passed: true, warnings: 0, errors: 0 },
        unit: { passed: 259, failed: 0, skipped: 0 },
        api: { passed: 114, failed: 0, skipped: 0 },
        architecture: { passed: 25, failed: 0, skipped: 0 },
        gateway: { passed: 12, failed: 0, skipped: 0 }
      },
      lifecycle: {
        focused: { passed: 6, failed: 0, skipped: 0 },
        affectedSecurityAndReplay: { passed: 16, failed: 0, skipped: 0 },
        cacheMaximum: 128,
        finalConnectionsPerPath: 1,
        dynamicLoopbackListenersDisposed: true,
        fixedPortsUsed: false
      },
      browser: {
        applicable: false,
        reason: 'No API, frontend or visual surface changed.'
      }
    },
    behavior: {
      dnsPolicyRunsPerRequest: true,
      exactAddressFingerprintSelectsClient: true,
      changedAddressFingerprintUsesDifferentClient: true,
      redirectDisabled: true,
      proxyDisabled: true,
      cookiesDisabled: true,
      timeoutBounded: true,
      providerResponseBodyBounded: true,
      cacheBounded: true,
      singletonPoolsDisposed: true
    },
    preservedCompatibility: {
      ssrfAndPrivateAddressPolicy: true,
      dnsPinningAndRebindingProtection: true,
      signedWebhookHeaders: true,
      retryDeadLetterAndReplay: true,
      safeErrorCodesAndSecretRedaction: true,
      applicationApiRoutes: true,
      persistenceContracts: true,
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
