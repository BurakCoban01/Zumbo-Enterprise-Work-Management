import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  applicationRoot as root,
  applicationWorkingDirectory,
  assertRootWorkflowLayout,
  rootWorkflowPath
} from '../repository-layout.mjs';

assertRootWorkflowLayout();
assert.throws(() => rootWorkflowPath('../ci.yml'), /Invalid root workflow file name/);
const workflowPath = rootWorkflowPath('ci.yml');
const manifest = JSON.parse(readFileSync(resolve(root, '.github/ci-gates.json'), 'utf8'));
const source = readFileSync(workflowPath, 'utf8').replaceAll('\r\n', '\n');
const jobs = parseJobs(source);
assert.equal(jobs.size, 12, 'CI must retain eleven gate/capability jobs plus the final summary job.');

assert.deepEqual(
  [...new Set(manifest.gates.map(gate => gate.number))].sort((a, b) => a - b),
  Array.from({ length: 19 }, (_, index) => index + 1),
  'The CI gate manifest must map every Section 12.4 gate from 1 through 19.');

for (const jobName of manifest.requiredJobs) {
  assert.ok(jobs.has(jobName), `Required CI job '${jobName}' is missing.`);
  assert.match(jobs.get(jobName).source, /^    timeout-minutes: \d+$/m, `Job '${jobName}' requires a timeout.`);
}
for (const gate of manifest.gates) {
  const job = jobs.get(gate.job);
  assert.ok(job, `Gate ${gate.number} references missing job '${gate.job}'.`);
  assert.ok(job.steps.has(gate.step), `Gate ${gate.number} references missing step '${gate.step}' in '${gate.job}'.`);
}

const actionUses = [...source.matchAll(/^\s+uses:\s*([^\s#]+)(?:\s+#.*)?$/gm)].map(match => match[1]);
assert.equal(actionUses.length, 32, 'CI action inventory changed; review every pin and runtime before accepting it.');
const approvedActionPins = new Map([
  ['actions/checkout', '3d3c42e5aac5ba805825da76410c181273ba90b1'],
  ['actions/setup-dotnet', 'a98b56852c35b8e3190ac28c8c2271da59106c68'],
  ['actions/setup-node', '820762786026740c76f36085b0efc47a31fe5020'],
  ['actions/upload-artifact', '043fb46d1a93c77aae656e7c1c64a875d1fc6a0a'],
  ['github/codeql-action/init', 'c54b30b7df092240050e69945842bc67aee0f0f4'],
  ['github/codeql-action/analyze', 'c54b30b7df092240050e69945842bc67aee0f0f4']
]);
for (const action of actionUses) {
  assert.match(action, /^[^@]+@[0-9a-f]{40}$/, `Action '${action}' must be pinned to a full commit SHA.`);
  const [name, sha] = action.split('@');
  assert.equal(sha, approvedActionPins.get(name), `Action '${name}' is not pinned to the reviewed Node 24-compatible release.`);
}

assert.match(source, /^permissions:\n  contents: read$/m, 'Workflow default permissions must be contents: read.');
assert.match(source, /^defaults:\n  run:\n    working-directory: Zumbo$/m, 'CI run steps must execute from the application root.');
assert.doesNotMatch(source, /^\s*continue-on-error:\s*true\s*$/m, 'Required CI gates cannot continue on error.');
assert.doesNotMatch(source, /actions\/cache@/i, 'Generated evidence and secrets must not use a broad actions/cache entry.');
for (const match of source.matchAll(/^\s*(environment|id-token|packages):\s*(\S+)\s*$/gmi)) {
  assert.equal(match[2], 'read', `CI cannot request ${match[1]}=${match[2]}.`);
}
const codeqlJobs = new Map([
  ['codeql-csharp', 'csharp'],
  ['codeql-javascript-typescript', 'javascript-typescript']
]);
const capabilityJob = jobs.get('codeql-capability');
const summaryJob = jobs.get('ci-summary');
assert.match(capabilityJob.source, /^    outputs:\n      enabled: \$\{\{ steps\.evaluate\.outputs\.enabled \}\}\n      state: \$\{\{ steps\.evaluate\.outputs\.state \}\}\n      applicability: \$\{\{ steps\.evaluate\.outputs\.applicability \}\}\n      expected_codeql_result: \$\{\{ steps\.evaluate\.outputs\.expected_codeql_result \}\}$/m);
assert.match(capabilityJob.source, /ZUMBO_REPOSITORY_PRIVATE: \$\{\{ github\.event\.repository\.private \}\}/);
assert.match(capabilityJob.source, /ZUMBO_CODE_SECURITY_VARIABLE: \$\{\{ vars\.GITHUB_CODE_SECURITY_ENABLED \}\}/);
assert.match(capabilityJob.source, /node scripts\/ci\/Test-CodeqlCapability\.mjs --runtime/);
for (const [jobName, language] of codeqlJobs) {
  const job = jobs.get(jobName);
  assert.match(
    job.source,
    /^    permissions:\n      actions: read\n      contents: read\n      security-events: write$/m,
    `${jobName} must receive only the minimum permissions required to upload analysis results.`);
  assert.match(job.source, /^    needs: codeql-capability\n    if: needs\.codeql-capability\.outputs\.enabled == 'true'$/m);
  assert.match(job.source, new RegExp(`languages: ${language}\\n`));
  assert.match(job.source, new RegExp(`category: /language:${language}(?:\\n|$)`));
}
assert.match(summaryJob.source, /^    if: always\(\)$/m);
assert.match(summaryJob.source, /^      - codeql-capability\n      - codeql-csharp\n      - codeql-javascript-typescript$/m);
assert.match(summaryJob.source, /ZUMBO_RESULT_CODEQL_CAPABILITY: \$\{\{ needs\.codeql-capability\.result \}\}/);
assert.match(summaryJob.source, /ZUMBO_CODEQL_ENABLED: \$\{\{ needs\.codeql-capability\.outputs\.enabled \}\}/);
assert.match(summaryJob.source, /ZUMBO_CODEQL_STATE: \$\{\{ needs\.codeql-capability\.outputs\.state \}\}/);
assert.match(summaryJob.source, /ZUMBO_CODEQL_APPLICABILITY: \$\{\{ needs\.codeql-capability\.outputs\.applicability \}\}/);
assert.match(summaryJob.source, /ZUMBO_RESULT_CODEQL_CSHARP: \$\{\{ needs\.codeql-csharp\.result \}\}/);
assert.match(summaryJob.source, /ZUMBO_RESULT_CODEQL_JAVASCRIPT_TYPESCRIPT: \$\{\{ needs\.codeql-javascript-typescript\.result \}\}/);
assert.match(summaryJob.source, /node scripts\/ci\/Test-CiSummary\.mjs --runtime/);
assert.ok(jobs.get('ci-contract').steps.has('CodeQL capability policy contract'));
assert.ok(jobs.get('ci-contract').steps.has('CI summary policy contract'));
for (const [name, job] of jobs) {
  if (codeqlJobs.has(name)) continue;
  assert.doesNotMatch(job.source, /^\s+[a-z-]+:\s*write\s*$/m, `Only CodeQL may receive a write permission; found one in '${name}'.`);
}

const frontendPackage = JSON.parse(readFileSync(resolve(root, 'Frontend/package.json'), 'utf8'));
assert.equal(frontendPackage.scripts['browser:install'], 'playwright-core install --with-deps chromium');
assert.equal(frontendPackage.devDependencies['playwright-core'], '1.61.1');
assert.equal(frontendPackage.devDependencies.playwright, undefined, 'The browser CLI must come from playwright-core.');
assert.equal(frontendPackage.devDependencies['@playwright/test'], undefined, 'The browser CLI must not require @playwright/test.');
const browserJob = jobs.get('runtime-browser');
assert.match(browserJob.source, /pnpm --dir Frontend run browser:install/);
assert.match(browserJob.source, /pnpm --dir Frontend exec playwright-core --version/);
assert.match(browserJob.source, /chromium\.executablePath\(\)[\s\S]*existsSync\(executable\)/);
assert.doesNotMatch(browserJob.source, /exec playwright install/);
assert.match(browserJob.source, /- name: Require browser evidence\n\s+run: test -f artifacts\/ui\/playwright\/fe008-cross-browser\.json/);
assert.match(jobs.get('provider-postgresql').source, /- name: Require migration evidence\n\s+run: test -f artifacts\/migrations\/ci\/postgresql\.sql/);
assert.ok(jobs.get('ci-contract').steps.has('Generated quality artifacts freshness'), 'CI must rebuild generated quality artifacts and reject a diff.');

const capabilityPolicy = JSON.parse(readFileSync(resolve(root, 'docs/quality/codeql-capability-policy.json'), 'utf8'));
const capabilityEvidence = JSON.parse(readFileSync(resolve(root, 'artifacts/quality/QA-001-codeql-capability.json'), 'utf8'));
assert.equal(capabilityPolicy.states.unavailable.name, 'ExternalPlatformUnavailable');
assert.equal(capabilityPolicy.states.unavailable.applicability, 'NotApplicableExternalPlatform');
assert.equal(capabilityPolicy.states.unavailable.expectedCodeqlResult, 'skipped');
assert.equal(capabilityPolicy.states.unavailable.codeqlPassed, false);
assert.deepEqual(capabilityPolicy.codeql.jobs, {
  csharp: 'codeql-csharp',
  'javascript-typescript': 'codeql-javascript-typescript'
});
assert.equal(capabilityEvidence.decision.state, 'ExternalPlatformUnavailable');
assert.equal(capabilityEvidence.decision.applicability, 'NotApplicableExternalPlatform');
assert.equal(capabilityEvidence.decision.codeqlPassed, false);
assert.deepEqual(capabilityEvidence.decision.expectedLanguageJobResults, {
  'codeql-csharp': 'skipped',
  'codeql-javascript-typescript': 'skipped'
});
assert.equal(capabilityEvidence.observation.sarifUploadSucceeded, false);

const runCommands = [...source.matchAll(/^\s+run:\s*(?:\|\s*\n((?:\s{10,}.*\n?)*)|([^\n]+))$/gm)]
  .map(match => `${match[1] || ''}${match[2] || ''}`);
const forbiddenCommands = [
  /\bdocker\s+(?:image\s+)?push\b/i,
  /\b(?:kubectl|helm)\b/i,
  /\b(?:npm|pnpm)\s+publish\b/i,
  /\bdotnet\s+nuget\s+push\b/i,
  /\bgh\s+(?:release|workflow)\b/i,
  /\b(?:az|aws|gcloud)\s+.*\bdeploy\b/i
];
for (const command of runCommands) {
  for (const forbidden of forbiddenCommands) {
    assert.doesNotMatch(command, forbidden, `Forbidden publish/deployment command matched ${forbidden}.`);
  }
}

const artifactSteps = [...source.matchAll(/uses:\s*actions\/upload-artifact@[0-9a-f]{40}[\s\S]*?with:\n([\s\S]*?)(?=\n\s{6}- name:|\n\s{4}[a-z0-9-]+:|\n\s*$)/g)];
assert.ok(artifactSteps.length >= 4, 'CI must retain bounded evidence from core, browser, migration, and security gates.');
for (const [, block] of artifactSteps) {
  assert.match(block, /retention-days:\s*(?:7|14)\b/, 'Every artifact must have bounded retention.');
  const artifactName = block.match(/name:\s*([^\n]+)/)?.[1]?.trim();
  if (['browser-smoke-evidence', 'migration-evidence'].includes(artifactName)) {
    assert.match(block, /if-no-files-found:\s*ignore\b/, `${artifactName} upload must not mask the primary test failure when no partial evidence exists.`);
  } else {
    assert.match(block, /if-no-files-found:\s*error\b/, 'Required non-browser artifacts must fail when expected evidence is absent.');
  }
  assert.doesNotMatch(block, /include-hidden-files:\s*true\b/, 'CI artifacts cannot include hidden files.');
  const pathBlock = block.match(/path:\s*\|\n([\s\S]*?)(?=\n\s{10}[a-z-]+:|$)/)?.[1] || '';
  const paths = pathBlock.split('\n').map(line => line.trim()).filter(Boolean);
  assert.ok(paths.length > 0, 'Artifact upload requires explicit paths.');
  for (const path of paths) {
    const applicationPrefix = `${applicationWorkingDirectory}/`;
    assert.ok(path.startsWith(applicationPrefix), `Artifact path '${path}' must be rooted under ${applicationPrefix}.`);
    const applicationPath = path.slice(applicationPrefix.length);
    assert.ok(manifest.allowedArtifactPrefixes.some(prefix => applicationPath.startsWith(prefix)), `Artifact path '${path}' is outside the allowlist.`);
    assert.doesNotMatch(path, /(?:\.env|\.log|\.out|\.err|\*\*)/i, `Artifact path '${path}' can retain secrets or unbounded files.`);
  }
}

for (const requiredCleanupJob of ['provider-mongo', 'provider-postgresql', 'external-dependencies', 'runtime-browser']) {
  const job = jobs.get(requiredCleanupJob);
  assert.match(job.source, /- name: Targeted cleanup\n\s+if: always\(\)/, `Job '${requiredCleanupJob}' requires unconditional targeted cleanup.`);
  assert.match(job.source, /docker compose[\s\S]* down --volumes --remove-orphans/, `Job '${requiredCleanupJob}' must remove only its Compose project resources.`);
}

console.log(`CI contract passed: 19 gates mapped across ${jobs.size} bounded jobs; ${actionUses.length} action uses are SHA-pinned.`);

function parseJobs(text) {
  const lines = text.split('\n');
  const jobsIndex = lines.findIndex(line => line === 'jobs:');
  assert.ok(jobsIndex >= 0, 'Workflow jobs section is missing.');
  const entries = new Map();
  for (let index = jobsIndex + 1; index < lines.length; index += 1) {
    const match = lines[index].match(/^  ([a-z0-9-]+):$/);
    if (!match) continue;
    let end = index + 1;
    while (end < lines.length && !/^  [a-z0-9-]+:$/.test(lines[end])) end += 1;
    const block = lines.slice(index, end).join('\n');
    const steps = new Set([...block.matchAll(/^      - name:\s*(.+)$/gm)].map(step => step[1].trim()));
    entries.set(match[1], { source: block, steps });
    index = end - 1;
  }
  return entries;
}
