import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  argumentValue,
  assertSafeArtifactContent,
  assertSha,
  evidenceFileName,
  expectedServices,
  hashManifestFileName,
  readJson,
  requiredPassingSteps,
  sanitizedEvidenceFiles,
  sha256,
  validateQa002Evidence
} from './qa002-common.mjs';

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const validatePath = argumentValue('--validate');
  if (validatePath) {
    const evidence = readJson(validatePath);
    const expectedSha = argumentValue('--expected-sha') ? assertSha(argumentValue('--expected-sha')) : undefined;
    const failures = validateQa002Evidence(evidence, expectedSha);
    if (failures.length) throw new Error(failures.join('\n'));
    console.log(`QA-002 evidence schema is valid (${evidence.passed ? 'passed' : 'blocked'}).`);
  } else {
    build();
  }
}

function build() {
  const preflightPath = resolve(required('--preflight'));
  const lifecyclePath = resolve(required('--lifecycle'));
  const cleanupPath = resolve(required('--cleanup'));
  const outputDirectory = resolve(required('--output-dir'));
  const targetSha = assertSha(required('--target-sha'));
  const preflight = optionalJson(preflightPath, { passed: false, checks: [], runner: {} });
  const lifecycle = optionalJson(lifecyclePath, { passed: false, steps: [], blocker: 'Lifecycle result is missing.' });
  const cleanup = optionalJson(cleanupPath, { passed: false, remaining: { containers: -1, networks: -1, volumes: -1 } });
  const observed = lifecycle.secondServiceInventory?.length
    ? lifecycle.secondServiceInventory
    : (lifecycle.firstServiceInventory || []);
  const stepResults = [
    { name: 'cleanCheckout', passed: preflight.repositoryClean === true },
    ...(lifecycle.steps || []).map(item => ({ name: item.name, passed: item.passed === true, durationMs: item.durationMs, commands: item.commands })),
    { name: 'cleanup', passed: cleanup.passed === true, durationMs: cleanup.durationMs },
    { name: 'schemaValidation', passed: true }
  ];
  const uniqueSteps = [...new Map(stepResults.map(item => [item.name, item])).values()];
  const stepMap = new Map(uniqueSteps.map(item => [item.name, item]));
  const gates = requiredPassingSteps.map(name => stepMap.get(name)?.passed === true);
  const passed = preflight.passed === true
    && lifecycle.passed === true
    && cleanup.passed === true
    && lifecycle.allServicesReady === true
    && gates.every(Boolean);
  const blocker = passed ? null : firstBlocker(preflight, lifecycle, cleanup, stepMap);
  const workflowName = process.env.GITHUB_WORKFLOW || 'QA-002 Clean Linux Operations Lifecycle';
  const evidence = {
    schemaVersion: 2,
    task: 'QA-002',
    generatedAtUtc: new Date().toISOString(),
    repository: process.env.GITHUB_REPOSITORY || 'local/unknown',
    targetCommitSha: targetSha,
    workflowName,
    workflowRunId: String(process.env.GITHUB_RUN_ID || 'local-static'),
    workflowRunAttempt: String(process.env.GITHUB_RUN_ATTEMPT || '1'),
    runnerImage: 'ubuntu-24.04',
    runnerOs: 'Linux',
    kernel: preflight.runner?.kernel || 'unavailable',
    dockerVersion: preflight.dockerVersion || 'unavailable',
    composeVersion: preflight.composeVersion || 'unavailable',
    passed,
    decision: passed ? 'passed' : 'blocked',
    serviceInventoryExpected: expectedServices,
    serviceInventoryObserved: observed,
    allServicesReady: lifecycle.allServicesReady === true,
    firstRunPassed: lifecycle.firstRunPassed === true,
    initialBootstrapPassed: lifecycle.initialBootstrapPassed === true,
    persistentMarkerCreated: lifecycle.persistentMarkerCreated === true,
    safeStopPassed: lifecycle.safeStopPassed === true,
    resumePassed: lifecycle.resumePassed === true,
    persistentMarkerPreserved: lifecycle.persistentMarkerPreserved === true,
    duplicateBootstrapAttempted: lifecycle.duplicateBootstrapAttempted === true,
    duplicateBootstrapRejected: lifecycle.duplicateBootstrapRejected === true,
    cleanupPassed: cleanup.passed === true,
    noDeployment: true,
    noPublicExposure: true,
    noImagePush: true,
    productionDataUsed: false,
    productionSecretsUsed: false,
    stepResults: uniqueSteps,
    timings: { ...(lifecycle.timings || {}), cleanup: cleanup.durationMs || 0 },
    blocker,
    sanitizedEvidenceFiles,
    sha256Manifest: hashManifestFileName
  };

  const failures = validateQa002Evidence(evidence, targetSha);
  if (failures.length) throw new Error(`Evidence builder produced an invalid manifest:\n${failures.join('\n')}`);
  mkdirSync(outputDirectory, { recursive: true });
  writeJson(resolve(outputDirectory, evidenceFileName), evidence);
  writeJson(resolve(outputDirectory, 'preflight-summary.json'), {
    targetCommitSha: preflight.targetCommitSha || targetSha,
    runner: preflight.runner || {},
    dockerVersion: preflight.dockerVersion || 'unavailable',
    composeVersion: preflight.composeVersion || 'unavailable',
    initialResources: preflight.initialResources || {},
    repositoryClean: preflight.repositoryClean === true,
    composeConfigPassed: preflight.composeConfigPassed === true,
    serviceInventoryConfigured: preflight.serviceInventoryConfigured || []
  });
  writeJson(resolve(outputDirectory, 'service-health.json'), {
    expected: expectedServices,
    first: lifecycle.firstServiceInventory || [],
    resumed: lifecycle.secondServiceInventory || [],
    allServicesReady: lifecycle.allServicesReady === true
  });
  writeJson(resolve(outputDirectory, 'command-summary.json'), {
    commands: uniqueSteps.map(item => ({ name: item.name, passed: item.passed, durationMs: item.durationMs || 0, commandCount: item.commands || 0 }))
  });
  writeJson(resolve(outputDirectory, 'cleanup-summary.json'), cleanup);
  writeFileSync(resolve(outputDirectory, 'qa-002-summary.txt'), [
    `task=QA-002`,
    `targetCommitSha=${targetSha}`,
    `decision=${evidence.decision}`,
    `allServicesReady=${evidence.allServicesReady}`,
    `persistentMarkerPreserved=${evidence.persistentMarkerPreserved}`,
    `duplicateBootstrapRejected=${evidence.duplicateBootstrapRejected}`,
    `cleanupPassed=${evidence.cleanupPassed}`,
    `noDeployment=true`,
    `noPublicExposure=true`,
    `noImagePush=true`
  ].join('\n') + '\n', 'utf8');

  const hashedFiles = sanitizedEvidenceFiles.filter(name => name !== hashManifestFileName).sort();
  const hashLines = hashedFiles.map(name => `${sha256(readFileSync(resolve(outputDirectory, name)))}  ${name}`);
  writeFileSync(resolve(outputDirectory, hashManifestFileName), `${hashLines.join('\n')}\n`, 'utf8');
  for (const name of sanitizedEvidenceFiles) {
    const content = readFileSync(resolve(outputDirectory, name), 'utf8');
    assertSafeArtifactContent(name, content);
  }
  console.log(`QA-002 sanitized evidence built: ${evidence.decision}; ${sanitizedEvidenceFiles.length} files.`);
}

export function verifyEvidenceDirectory(directory, expectedSha) {
  const absolute = resolve(directory);
  const files = readdirSync(absolute, { withFileTypes: true });
  if (files.some(entry => !entry.isFile())
      || JSON.stringify(files.map(entry => entry.name).sort()) !== JSON.stringify([...sanitizedEvidenceFiles].sort())) {
    throw new Error('Artifact directory does not match the exact sanitized file allow-list.');
  }
  const evidence = readJson(resolve(absolute, evidenceFileName));
  const failures = validateQa002Evidence(evidence, expectedSha);
  if (failures.length) throw new Error(failures.join('\n'));
  const hashText = readFileSync(resolve(absolute, hashManifestFileName), 'utf8');
  const lines = hashText.trim().split(/\r?\n/).filter(Boolean);
  const expectedHashed = sanitizedEvidenceFiles.filter(name => name !== hashManifestFileName).sort();
  const observedHashed = [];
  for (const line of lines) {
    const match = /^([0-9a-f]{64})  ([A-Za-z0-9._-]+)$/.exec(line);
    if (!match) throw new Error('SHA-256 manifest contains an invalid line.');
    const [, expectedHash, name] = match;
    if (!expectedHashed.includes(name)) throw new Error(`Hash manifest contains unexpected file ${name}.`);
    const content = readFileSync(resolve(absolute, name));
    if (sha256(content) !== expectedHash) throw new Error(`Artifact hash mismatch: ${name}.`);
    assertSafeArtifactContent(name, content.toString('utf8'));
    observedHashed.push(name);
  }
  if (JSON.stringify(observedHashed.sort()) !== JSON.stringify(expectedHashed)) throw new Error('Hash manifest file set is incomplete.');
  assertSafeArtifactContent(hashManifestFileName, hashText);
  return evidence;
}

function firstBlocker(preflight, lifecycle, cleanup, stepMap) {
  if (preflight.passed !== true) return 'Clean-Linux preflight did not pass.';
  if (lifecycle.blocker) return String(lifecycle.blocker).slice(0, 1000);
  const failed = requiredPassingSteps.find(name => stepMap.get(name)?.passed !== true);
  if (failed) return `Required lifecycle step did not pass: ${failed}.`;
  if (cleanup.passed !== true) return 'Targeted cleanup did not remove all workflow-owned resources.';
  return 'QA-002 lifecycle acceptance is incomplete.';
}

function optionalJson(path, fallback) {
  try { return readJson(path); } catch { return fallback; }
}

function writeJson(path, value) {
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

function required(name) {
  const value = argumentValue(name);
  if (!value) throw new Error(`${name} is required.`);
  return value;
}
