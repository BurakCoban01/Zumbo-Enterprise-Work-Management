import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import {
  applicationRoot,
  assertRootWorkflowLayout,
  rootWorkflowPath
} from '../repository-layout.mjs';
import {
  expectedServices,
  redact,
  repositoryRoot,
  requiredPassingSteps,
  sanitizedEvidenceFiles,
  sha256,
  validateQa002Evidence
} from '../operations/qa002-common.mjs';
import { verifyEvidenceDirectory } from '../operations/qa002-evidence.mjs';

assert.equal(repositoryRoot, applicationRoot, 'QA-002 scripts and workflow layout disagree about the application root.');
assertRootWorkflowLayout();
const workflowPath = rootWorkflowPath('qa-002-clean-linux.yml');
const workflow = readFileSync(workflowPath, 'utf8');
const checks = [];

check('git-root-workflow-layout', () => {
  assert.match(workflow, /^defaults:\s*\r?\n\s+run:\s*\r?\n\s+working-directory: Zumbo\s*$/m);
  assert.match(workflow, /QA002_ENV_FILE: \$\{\{ runner\.temp \}\}\//);
  assert.match(workflow, /QA002_EVIDENCE_DIR: \$\{\{ runner\.temp \}\}\//);
});

check('manual-trigger-only', () => {
  assert.match(workflow, /^on:\s*\r?\n\s+workflow_dispatch:/m);
  assert.doesNotMatch(workflow, /^\s+(push|pull_request|schedule|workflow_run):/m);
});
check('runner-timeout-concurrency', () => {
  assert.match(workflow, /runs-on: ubuntu-24\.04/);
  assert.doesNotMatch(workflow, /ubuntu-latest/);
  assert.match(workflow, /timeout-minutes: 60/);
  assert.match(workflow, /concurrency:[\s\S]*cancel-in-progress: false/);
});
check('least-privilege', () => {
  assert.match(workflow, /^permissions:\s*\r?\n\s+contents: read\s*$/m);
  assert.doesNotMatch(workflow, /(contents|actions|packages|deployments|id-token|security-events): write/);
});
check('deliberate-exact-sha-input', () => {
  assert.match(workflow, /target_sha:/);
  assert.match(workflow, /confirmation:/);
  assert.match(workflow, /RUN-QA-002/);
  assert.match(workflow, /\{40\}/);
  assert.match(workflow, /ref: \$\{\{ inputs\.target_sha \}\}/);
});
check('official-full-sha-actions', () => {
  const actions = [...workflow.matchAll(/uses:\s+([^\s#]+)/g)].map(match => match[1]);
  assert.deepEqual(actions, [
    'actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1',
    'actions/setup-node@820762786026740c76f36085b0efc47a31fe5020',
    'actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68',
    'actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a'
  ]);
  assert.ok(actions.every(value => /^actions\/[a-z0-9-]+@[0-9a-f]{40}$/.test(value)));
});
check('checkout-and-artifact-boundary', () => {
  assert.match(workflow, /persist-credentials: false/);
  assert.match(workflow, /fetch-depth: 1/);
  assert.match(workflow, /retention-days: 1/);
  assert.match(workflow, /include-hidden-files: false/);
  assert.doesNotMatch(workflow, /cache:/);
});
check('always-cleanup', () => {
  const cleanup = workflow.indexOf('name: Targeted always cleanup');
  assert.ok(cleanup > 0);
  assert.match(workflow.slice(cleanup, cleanup + 180), /if: \$\{\{ always\(\) \}\}/);
  assert.doesNotMatch(workflow, /docker\s+(system|volume|network|builder)\s+prune/i);
});
check('no-deploy-or-publish', () => {
  assert.doesNotMatch(workflow, /\b(deploy|docker\s+push|ghcr\.io|packages: write|npm publish|dotnet nuget push)\b/i);
});
check('secret-redaction', () => {
  const secret = 'synthetic-secret-value-123456';
  const output = redact(`password=${secret} Authorization: Bearer abc.def.ghi`, [secret]);
  assert.doesNotMatch(output, new RegExp(secret));
  assert.match(output, /\[REDACTED\]/);
});

const valid = validEvidence();
check('valid-evidence', () => assert.deepEqual(validateQa002Evidence(valid, valid.targetCommitSha), []));
negative('wrong-target-sha', evidence => { evidence.targetCommitSha = 'b'.repeat(40); }, 'targetCommitSha does not match');
negative('missing-service', evidence => { evidence.serviceInventoryObserved.pop(); }, 'exactly eight services');
negative('missing-lifecycle-step', evidence => { evidence.stepResults = evidence.stepResults.filter(step => step.name !== 'resume'); }, 'passed=true is inconsistent');
negative('missing-cleanup', evidence => { evidence.cleanupPassed = false; }, 'passed=true is inconsistent');
negative('persistence-false-positive', evidence => { evidence.persistentMarkerPreserved = false; }, 'passed=true is inconsistent');
negative('duplicate-bootstrap-false-positive', evidence => { evidence.duplicateBootstrapRejected = false; }, 'passed=true is inconsistent');
negative('production-secret-use', evidence => { evidence.productionSecretsUsed = true; }, 'Production data or secrets');
check('artifact-hash-tamper-rejection', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'zumbo-qa002-artifact-'));
  try {
    const contents = new Map([
      ['qa-002-remote-evidence.json', `${JSON.stringify(valid, null, 2)}\n`],
      ['preflight-summary.json', '{}\n'],
      ['service-health.json', '{}\n'],
      ['command-summary.json', '{}\n'],
      ['cleanup-summary.json', '{}\n'],
      ['qa-002-summary.txt', 'task=QA-002\ndecision=passed\n']
    ]);
    for (const [name, content] of contents) writeFileSync(resolve(directory, name), content, 'utf8');
    const hashes = [...contents].sort(([left], [right]) => left.localeCompare(right))
      .map(([name, content]) => `${sha256(content)}  ${name}`);
    writeFileSync(resolve(directory, 'qa-002.sha256'), `${hashes.join('\n')}\n`, 'utf8');
    assert.equal(verifyEvidenceDirectory(directory, valid.targetCommitSha).passed, true);
    writeFileSync(resolve(directory, 'qa-002-summary.txt'), 'tampered=true\n', 'utf8');
    assert.throws(() => verifyEvidenceDirectory(directory, valid.targetCommitSha), /hash mismatch/i);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
check('evidence-builder-pass-gates', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'zumbo-qa002-builder-'));
  const artifact = resolve(directory, 'artifact');
  try {
    const preflight = {
      passed: true,
      targetCommitSha: valid.targetCommitSha,
      repositoryClean: true,
      composeConfigPassed: true,
      runner: { kernel: 'Linux 6.x' },
      dockerVersion: '28.0.0',
      composeVersion: '2.0.0'
    };
    const lifecycleNames = requiredPassingSteps.filter(name => !['cleanCheckout', 'cleanup', 'schemaValidation'].includes(name));
    const lifecycle = {
      passed: true,
      allServicesReady: true,
      firstRunPassed: true,
      initialBootstrapPassed: true,
      persistentMarkerCreated: true,
      safeStopPassed: true,
      resumePassed: true,
      persistentMarkerPreserved: true,
      duplicateBootstrapAttempted: true,
      duplicateBootstrapRejected: true,
      firstServiceInventory: valid.serviceInventoryObserved,
      secondServiceInventory: valid.serviceInventoryObserved,
      steps: lifecycleNames.map(name => ({ name, passed: true, durationMs: 1 })),
      timings: {}
    };
    const cleanup = { passed: true, durationMs: 1, remaining: { containers: 0, networks: 0, volumes: 0 } };
    writeFileSync(resolve(directory, 'preflight.json'), `${JSON.stringify(preflight)}\n`);
    writeFileSync(resolve(directory, 'lifecycle.json'), `${JSON.stringify(lifecycle)}\n`);
    writeFileSync(resolve(directory, 'cleanup.json'), `${JSON.stringify(cleanup)}\n`);
    const result = spawnSync(process.execPath, [
      resolve(repositoryRoot, 'scripts/operations/qa002-evidence.mjs'),
      '--target-sha', valid.targetCommitSha,
      '--preflight', resolve(directory, 'preflight.json'),
      '--lifecycle', resolve(directory, 'lifecycle.json'),
      '--cleanup', resolve(directory, 'cleanup.json'),
      '--output-dir', artifact
    ], { cwd: repositoryRoot, encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr || result.stdout);
    assert.equal(verifyEvidenceDirectory(artifact, valid.targetCommitSha).passed, true);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

console.log(`QA-002 workflow contracts passed: ${checks.length}/${checks.length}.`);

function negative(name, mutate, expectedMessage) {
  check(name, () => {
    const evidence = structuredClone(valid);
    mutate(evidence);
    assert.ok(validateQa002Evidence(evidence, valid.targetCommitSha).some(message => message.includes(expectedMessage)));
  });
}

function check(name, operation) {
  operation();
  checks.push({ name, passed: true });
}

function validEvidence() {
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
    serviceInventoryObserved: expectedServices.map(service => ({ service, state: service === 'mongo-init-replica' ? 'exited' : 'running', health: service === 'mongo-init-replica' ? 'none' : 'healthy', exitCode: service === 'mongo-init-replica' ? 0 : -1, ready: true })),
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
    stepResults: requiredPassingSteps.map(name => ({ name, passed: true })),
    timings: Object.fromEntries(requiredPassingSteps.map(name => [name, 1])),
    blocker: null,
    sanitizedEvidenceFiles,
    sha256Manifest: 'qa-002.sha256'
  };
}
