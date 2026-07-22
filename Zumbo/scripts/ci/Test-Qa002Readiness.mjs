import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { rootWorkflowPath } from '../repository-layout.mjs';
import {
  expectedServices,
  requiredPassingSteps,
  validateQa002Evidence
} from '../operations/qa002-common.mjs';
import {
  createApiRequest,
  exactInventoryReady,
  Qa002ReadinessTimeoutError,
  validateQa002ApiUrl,
  verifyQa002Readiness
} from '../operations/qa002-readiness.mjs';

const checks = [];
const origin = 'http://127.0.0.1:58089';
const readyInventory = expectedServices.map(service => ({
  service,
  state: service === 'mongo-init-replica' ? 'exited' : 'running',
  health: service === 'mongo-init-replica' ? 'none' : 'healthy',
  exitCode: service === 'mongo-init-replica' ? 0 : -1,
  ready: true
}));

await check('ECONNREFUSED-then-200', async () => {
  const fetch = sequencedFetch([
    transportFailure('ECONNREFUSED'), response(200), response(200), response(200)
  ]);
  const result = await readiness({ fetch });
  assert.equal(exactInventoryReady(result, expectedServices), true);
  assert.equal(fetch.calls(), 4);
});

await check('ECONNRESET-retries-then-ready', async () => {
  const fetch = sequencedFetch([
    transportFailure('ECONNRESET'), transportFailure('ECONNRESET'),
    transportFailure('ECONNRESET'), response(200),
    response(200), response(200)
  ]);
  await readiness({ fetch });
  assert.equal(fetch.calls(), 6);
});

await check('HTTP-503-then-200', async () => {
  const fetch = sequencedFetch([response(503), response(200), response(200), response(200)]);
  await readiness({ fetch });
  assert.equal(fetch.calls(), 4);
});

await check('deadline-transport-diagnostic', async () => {
  const fetch = alwaysFetch(transportFailure('ECONNREFUSED', {
    errno: -111, syscall: 'connect', address: '127.0.0.1', port: 58089
  }));
  const error = await timeoutError(() => readiness({ fetch, timeoutMs: 10, pollIntervalMs: 5 }));
  assert.equal(error.stage, 'resume');
  assert.equal(error.code, 'ECONNREFUSED');
  assert.equal(error.path, '/health/ready');
  assert.match(error.message, /resume readiness timed out; last transport error ECONNREFUSED/);
  assert.match(error.message, /GET \/health\/ready/);
  assert.match(error.message, /address 127\.0\.0\.1, port 58089/);
});

await check('deadline-http-diagnostic', async () => {
  const error = await timeoutError(() => readiness({
    fetch: alwaysFetch(response(503)), timeoutMs: 10, pollIntervalMs: 5
  }));
  assert.equal(error.status, 503);
  assert.equal(error.path, '/health/ready');
  assert.match(error.message, /last HTTP status 503/);
  assert.match(error.message, new RegExp(origin.replaceAll('.', '\\.')));
});

await check('failure-retains-last-inventory', async () => {
  const retained = [];
  const error = await timeoutError(() => readiness({
    fetch: alwaysFetch(transportFailure('ECONNREFUSED')),
    timeoutMs: 10,
    pollIntervalMs: 5,
    onInventory: inventory => retained.push(inventory)
  }));
  assert.equal(retained.length, 2);
  assert.deepEqual(error.inventory, readyInventory);
});

await check('request-timeout-is-bounded-by-readiness-deadline', async () => {
  const scheduled = [];
  const request = createApiRequest({
    origin,
    fetchImpl: response(503),
    requestTimeoutMs: 30_000,
    setTimeoutImpl: (_callback, milliseconds) => { scheduled.push(milliseconds); return scheduled.length; },
    clearTimeoutImpl: () => {}
  });
  let time = 0;
  await timeoutError(() => verifyQa002Readiness({
    stage: 'resume',
    expectedServices,
    getInventory: async ({ timeoutMs }) => {
      assert.ok(timeoutMs <= 10 - time);
      return structuredClone(readyInventory);
    },
    apiRequest: request,
    now: () => time,
    sleep: async milliseconds => { time += milliseconds; },
    timeoutMs: 10,
    pollIntervalMs: 5
  }));
  assert.deepEqual(scheduled, [10, 10, 5, 5]);
  assert.equal(time, 10);
});

await check('bootstrap-post-no-retry', () => assertSingleSideEffect('/api/auth/register', {
  username: 'qa002admin', password: 'synthetic-password', bootstrapToken: 'synthetic-token'
}));

await check('marker-post-no-retry', () => assertSingleSideEffect('/api/organizations', {
  name: 'QA-002 marker', tenantKey: 'qa002-tenant'
}, 'Bearer synthetic-access-token'));

await check('duplicate-bootstrap-post-once', () => assertSingleSideEffect('/api/auth/register', {
  username: 'qa002adminsecond', password: 'synthetic-password', bootstrapToken: 'synthetic-token'
}));

await check('transport-diagnostic-excludes-secrets', async () => {
  const password = 'do-not-leak-password';
  const token = 'do-not-leak-token';
  const fetch = alwaysFetch(() => {
    const cause = Object.assign(new Error(`socket closed ${password} ${token}`), {
      code: 'ECONNRESET', syscall: 'read', address: '127.0.0.1', port: 58089
    });
    return Promise.reject(new TypeError(`fetch failed ${password}`, { cause }));
  });
  const request = createApiRequest({ origin, fetchImpl: fetch });
  await assert.rejects(
    request('/api/organizations', {
      method: 'POST', token, body: { password, bootstrapToken: token }
    }),
    error => {
      assert.doesNotMatch(error.message, new RegExp(`${password}|${token}|Bearer|bootstrapToken`));
      assert.match(error.message, /ECONNRESET/);
      assert.equal(error.cause.cause.code, 'ECONNRESET');
      return true;
    }
  );
});

await check('final-blocker-is-not-generic-fetch-failed', async () => {
  const error = await timeoutError(() => readiness({
    fetch: alwaysFetch(transportFailure('ECONNREFUSED')), timeoutMs: 10, pollIntervalMs: 5
  }));
  assert.doesNotMatch(error.message, /(^|[;:])\s*fetch failed\s*$/i);
  assert.doesNotMatch(error.message, /^fetch failed$/i);
  assert.match(error.message, /ECONNREFUSED/);
});

await check('two-inventories-two-readiness-tours', async () => {
  let inventoryCalls = 0;
  const fetch = alwaysFetch(response(200));
  for (const stage of ['first', 'resume']) {
    const result = await verifyQa002Readiness({
      stage,
      expectedServices,
      getInventory: async () => { inventoryCalls += 1; return structuredClone(readyInventory); },
      apiRequest: createApiRequest({ origin, fetchImpl: fetch })
    });
    assert.equal(exactInventoryReady(result, expectedServices), true);
  }
  assert.equal(inventoryCalls, 2);
  assert.equal(fetch.calls(), 4);
});

await check('workflow-cleanup-always-runs', () => {
  const workflow = readFileSync(rootWorkflowPath('qa-002-clean-linux.yml'), 'utf8');
  const cleanup = workflow.indexOf('name: Targeted always cleanup');
  assert.ok(cleanup > 0);
  assert.match(workflow.slice(cleanup, cleanup + 180), /if: \$\{\{ always\(\) \}\}/);
});

await check('passed-requires-all-semantic-gates', () => {
  const evidence = validEvidence();
  assert.deepEqual(validateQa002Evidence(evidence, evidence.targetCommitSha), []);
  for (const field of [
    'allServicesReady', 'firstRunPassed', 'initialBootstrapPassed',
    'persistentMarkerCreated', 'safeStopPassed', 'resumePassed',
    'persistentMarkerPreserved', 'duplicateBootstrapAttempted',
    'duplicateBootstrapRejected', 'cleanupPassed'
  ]) {
    const invalid = structuredClone(evidence);
    invalid[field] = false;
    assert.ok(validateQa002Evidence(invalid, invalid.targetCommitSha)
      .some(message => message.includes('passed=true is inconsistent')));
  }
});

await check('api-url-compose-contract', () => {
  const contract = validateQa002ApiUrl(`${origin}/`, {
    gatewayBindHost: '127.0.0.1',
    gatewayPort: '58089',
    composeGateway: { host_ip: '127.0.0.1', published: '58089' }
  });
  assert.equal(contract.origin, origin);
  assert.throws(() => validateQa002ApiUrl('https://127.0.0.1:58089', {
    gatewayBindHost: '127.0.0.1', gatewayPort: '58089'
  }), /HTTP scheme/);
  assert.throws(() => validateQa002ApiUrl('http://127.0.0.1:58088', {
    gatewayBindHost: '127.0.0.1', gatewayPort: '58089'
  }), /published port/);
  assert.throws(() => validateQa002ApiUrl('http://example.test:58089', {
    gatewayBindHost: '127.0.0.1', gatewayPort: '58089'
  }), /loopback/);
});

console.log(`QA-002 readiness contracts passed: ${checks.length}/${checks.length}.`);

async function readiness({ fetch, timeoutMs = 20, pollIntervalMs = 5, onInventory = () => {} }) {
  let time = 0;
  return verifyQa002Readiness({
    stage: 'resume',
    expectedServices,
    getInventory: async () => structuredClone(readyInventory),
    apiRequest: createApiRequest({ origin, fetchImpl: fetch }),
    onInventory,
    now: () => time,
    sleep: async milliseconds => { time += milliseconds; },
    timeoutMs,
    pollIntervalMs
  });
}

async function assertSingleSideEffect(path, body, authorization) {
  const fetch = alwaysFetch(transportFailure('ECONNREFUSED'));
  const request = createApiRequest({ origin, fetchImpl: async (url, options) => {
    assert.equal(url, `${origin}${path}`);
    assert.equal(options.method, 'POST');
    if (authorization) assert.equal(options.headers.Authorization, authorization);
    return fetch(url, options);
  }});
  await assert.rejects(request(path, {
    method: 'POST',
    token: authorization?.replace(/^Bearer /, ''),
    body
  }), /ECONNREFUSED/);
  assert.equal(fetch.calls(), 1);
}

async function timeoutError(operation) {
  try {
    await operation();
  } catch (error) {
    assert.ok(error instanceof Qa002ReadinessTimeoutError);
    return error;
  }
  assert.fail('Expected readiness timeout.');
}

function response(status, text = status === 200 ? 'ok' : 'unavailable') {
  return async () => ({ status, text: async () => text });
}

function transportFailure(code, fields = {}) {
  return async () => {
    const cause = Object.assign(new Error('socket transport detail'), { code, ...fields });
    throw new TypeError('fetch failed', { cause });
  };
}

function sequencedFetch(sequence) {
  let calls = 0;
  const fetch = async (...args) => {
    const operation = sequence[Math.min(calls, sequence.length - 1)];
    calls += 1;
    return operation(...args);
  };
  fetch.calls = () => calls;
  return fetch;
}

function alwaysFetch(operation) {
  let calls = 0;
  const fetch = async (...args) => {
    calls += 1;
    return operation(...args);
  };
  fetch.calls = () => calls;
  return fetch;
}

async function check(name, operation) {
  await operation();
  checks.push({ name, passed: true });
}

function validEvidence() {
  const stepResults = requiredPassingSteps.map(name => ({ name, passed: true }));
  return {
    schemaVersion: 2,
    task: 'QA-002',
    generatedAtUtc: '2026-07-22T00:00:00.000Z',
    repository: 'bcedu1/ZmboTaskTmMng',
    targetCommitSha: 'a'.repeat(40),
    workflowName: 'QA-002 Clean Linux Lifecycle',
    workflowRunId: '123',
    workflowRunAttempt: '1',
    runnerImage: 'ubuntu-24.04',
    runnerOs: 'Linux',
    kernel: 'Linux 6.x',
    dockerVersion: '28.0.0',
    composeVersion: '2.0.0',
    passed: true,
    decision: 'passed',
    serviceInventoryExpected: expectedServices,
    serviceInventoryObserved: readyInventory,
    allServicesReady: true,
    firstRunPassed: true,
    initialBootstrapPassed: true,
    persistentMarkerCreated: true,
    safeStopPassed: true,
    resumePassed: true,
    persistentMarkerPreserved: true,
    duplicateBootstrapAttempted: true,
    duplicateBootstrapRejected: true,
    cleanupPassed: true,
    noDeployment: true,
    noPublicExposure: true,
    noImagePush: true,
    productionDataUsed: false,
    productionSecretsUsed: false,
    stepResults,
    timings: Object.fromEntries(stepResults.map(step => [step.name, 1])),
    blocker: null,
    sanitizedEvidenceFiles: [
      'qa-002-remote-evidence.json', 'preflight-summary.json', 'service-health.json',
      'command-summary.json', 'cleanup-summary.json', 'qa-002-summary.txt', 'qa-002.sha256'
    ],
    sha256Manifest: 'qa-002.sha256'
  };
}
