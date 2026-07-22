import { createHash, randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
export const expectedServices = Object.freeze([
  'api',
  'gateway',
  'worker',
  'mongo',
  'mongo-init-replica',
  'redis',
  'minio',
  'opensearch'
]);
export const evidenceFileName = 'qa-002-remote-evidence.json';
export const hashManifestFileName = 'qa-002.sha256';
export const sanitizedEvidenceFiles = Object.freeze([
  evidenceFileName,
  'preflight-summary.json',
  'service-health.json',
  'command-summary.json',
  'cleanup-summary.json',
  'qa-002-summary.txt',
  hashManifestFileName
]);
export const requiredPassingSteps = Object.freeze([
  'cleanCheckout',
  'environmentPrepared',
  'hostRestoreBuild',
  'frontendInstallBuild',
  'composeConfig',
  'composeBuild',
  'firstStart',
  'firstReadiness',
  'initialBootstrap',
  'persistentMarkerCreate',
  'persistentMarkerInitialRead',
  'safeStop',
  'resume',
  'secondReadiness',
  'persistentMarkerPreserved',
  'duplicateBootstrapRejected',
  'cleanup',
  'schemaValidation'
]);

export function argumentValue(name, argv = process.argv) {
  const index = argv.indexOf(name);
  return index >= 0 ? argv[index + 1] : undefined;
}

export function requireArgument(name, argv = process.argv) {
  const value = argumentValue(name, argv);
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

export function assertSha(value, label = 'SHA') {
  if (!/^[0-9a-f]{40}$/i.test(value || '')) throw new Error(`${label} must be exactly 40 hexadecimal characters.`);
  return value.toLowerCase();
}

export function assertProjectName(value) {
  if (!/^zumbo-qa002-[a-z0-9-]+$/.test(value || '')) {
    throw new Error('Project name must match zumbo-qa002-<run-owned-suffix>.');
  }
  return value;
}

export function syntheticSecret(bytes = 32) {
  return randomBytes(bytes).toString('base64url');
}

export function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

export function readJson(path) {
  return JSON.parse(readFileSync(resolve(path), 'utf8'));
}

export function parseEnvironmentFile(path) {
  const values = {};
  for (const rawLine of readFileSync(resolve(path), 'utf8').split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#') || !line.includes('=')) continue;
    const separator = line.indexOf('=');
    values[line.slice(0, separator).trim()] = line.slice(separator + 1).trim();
  }
  return values;
}

export function redact(value, secrets = []) {
  let output = String(value || '');
  for (const secret of secrets.filter(item => typeof item === 'string' && item.length >= 3).sort((a, b) => b.length - a.length)) {
    output = output.split(secret).join('[REDACTED]');
  }
  output = output
    .replace(/Bearer\s+[A-Za-z0-9._~-]+/gi, 'Bearer [REDACTED]')
    .replace(/(password|token|secret|api[_-]?key)(\s*[=:]\s*)[^\s,;]+/gi, '$1$2[REDACTED]')
    .replace(/-----BEGIN[\s\S]*?PRIVATE KEY-----/g, '[REDACTED PRIVATE KEY]');
  return output;
}

export function parseComposeJsonLines(text) {
  const trimmed = String(text || '').trim();
  if (!trimmed) return [];
  try {
    const parsed = JSON.parse(trimmed);
    return Array.isArray(parsed) ? parsed : [parsed];
  } catch {
    return trimmed.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
  }
}

export function validateQa002Evidence(evidence, expectedSha) {
  const failures = [];
  const booleanFields = [
    'passed', 'allServicesReady', 'firstRunPassed', 'initialBootstrapPassed',
    'persistentMarkerCreated', 'safeStopPassed', 'resumePassed',
    'persistentMarkerPreserved', 'duplicateBootstrapAttempted',
    'duplicateBootstrapRejected', 'cleanupPassed', 'noDeployment',
    'noPublicExposure', 'noImagePush', 'productionDataUsed', 'productionSecretsUsed'
  ];
  if (evidence?.schemaVersion !== 2 || evidence?.task !== 'QA-002') failures.push('schemaVersion/task is invalid.');
  if (!/^\d{4}-\d{2}-\d{2}T/.test(evidence?.generatedAtUtc || '')) failures.push('generatedAtUtc is invalid.');
  if (!evidence?.repository || !evidence?.workflowName || !evidence?.workflowRunId || !evidence?.workflowRunAttempt) failures.push('Workflow identity is incomplete.');
  if (!/^[0-9a-f]{40}$/.test(evidence?.targetCommitSha || '')) failures.push('targetCommitSha is invalid.');
  if (expectedSha && evidence?.targetCommitSha !== expectedSha.toLowerCase()) failures.push('targetCommitSha does not match the expected immutable commit.');
  if (evidence?.runnerImage !== 'ubuntu-24.04' || evidence?.runnerOs !== 'Linux') failures.push('Runner must be Linux on ubuntu-24.04.');
  if (!evidence?.kernel || !evidence?.dockerVersion || !evidence?.composeVersion) failures.push('Runtime version inventory is incomplete.');
  for (const field of booleanFields) if (typeof evidence?.[field] !== 'boolean') failures.push(`${field} must be boolean.`);
  if (JSON.stringify(evidence?.serviceInventoryExpected) !== JSON.stringify(expectedServices)) failures.push('Expected service inventory drifted.');
  const observed = Array.isArray(evidence?.serviceInventoryObserved) ? evidence.serviceInventoryObserved : [];
  if (new Set(observed.map(item => item.service)).size !== observed.length) failures.push('Observed services are not unique.');
  if (evidence?.allServicesReady && JSON.stringify(observed.map(item => item.service).sort()) !== JSON.stringify([...expectedServices].sort())) failures.push('Ready evidence does not contain exactly eight services.');
  if (evidence?.allServicesReady && observed.some(item => item.ready !== true)) failures.push('A reported service is not ready.');
  if (!Array.isArray(evidence?.stepResults) || !evidence.stepResults.length) failures.push('stepResults is missing.');
  if (!evidence?.timings || typeof evidence.timings !== 'object') failures.push('timings is missing.');
  if (!Array.isArray(evidence?.sanitizedEvidenceFiles) || JSON.stringify(evidence.sanitizedEvidenceFiles) !== JSON.stringify(sanitizedEvidenceFiles)) failures.push('Sanitized evidence allow-list drifted.');
  if (evidence?.sha256Manifest !== hashManifestFileName) failures.push('SHA-256 manifest path drifted.');
  if (evidence?.noDeployment !== true || evidence?.noPublicExposure !== true || evidence?.noImagePush !== true) failures.push('No-deployment/public-exposure/image-push invariant failed.');
  if (evidence?.productionDataUsed !== false || evidence?.productionSecretsUsed !== false) failures.push('Production data or secrets cannot be used.');

  const stepMap = new Map((evidence?.stepResults || []).map(item => [item.name, item]));
  const semanticGates = [
    evidence?.allServicesReady,
    evidence?.firstRunPassed,
    evidence?.initialBootstrapPassed,
    evidence?.persistentMarkerCreated,
    evidence?.safeStopPassed,
    evidence?.resumePassed,
    evidence?.persistentMarkerPreserved,
    evidence?.duplicateBootstrapAttempted,
    evidence?.duplicateBootstrapRejected,
    evidence?.cleanupPassed,
    ...requiredPassingSteps.map(name => stepMap.get(name)?.passed === true)
  ];
  if (evidence?.passed && semanticGates.some(value => value !== true)) failures.push('passed=true is inconsistent with required lifecycle gates.');
  if (evidence?.passed && (evidence?.decision !== 'passed' || evidence?.blocker !== null)) failures.push('Passing evidence must have decision=passed and no blocker.');
  if (!evidence?.passed && (evidence?.decision !== 'blocked' || typeof evidence?.blocker !== 'string' || !evidence.blocker)) failures.push('Failing evidence must have decision=blocked and an explicit blocker.');
  return failures;
}

export function assertSafeArtifactContent(path, content) {
  const name = path.replaceAll('\\', '/').split('/').at(-1);
  if (!sanitizedEvidenceFiles.includes(name)) throw new Error(`Unexpected artifact file: ${name}`);
  const forbidden = [
    /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/,
    /(?:^|\n)ZUMBO_[A-Z0-9_]+=/,
    /Bearer\s+[A-Za-z0-9._~-]{12,}/i,
    /github_pat_[A-Za-z0-9_]{20,}/,
    /gh[pousr]_[A-Za-z0-9_]{20,}/
  ];
  if (forbidden.some(pattern => pattern.test(content))) throw new Error(`${name} contains forbidden secret-bearing content.`);
}
