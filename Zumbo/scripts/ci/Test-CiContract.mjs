import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '../..');
const workflowPath = resolve(root, '.github/workflows/ci.yml');
const manifest = JSON.parse(readFileSync(resolve(root, '.github/ci-gates.json'), 'utf8'));
const source = readFileSync(workflowPath, 'utf8').replaceAll('\r\n', '\n');
const jobs = parseJobs(source);

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
assert.ok(actionUses.length > 0, 'CI must use at least one action.');
for (const action of actionUses) {
  assert.match(action, /^[^@]+@[0-9a-f]{40}$/, `Action '${action}' must be pinned to a full commit SHA.`);
}

assert.match(source, /^permissions:\n  contents: read$/m, 'Workflow default permissions must be contents: read.');
assert.doesNotMatch(source, /^\s*continue-on-error:\s*true\s*$/m, 'Required CI gates cannot continue on error.');
assert.doesNotMatch(source, /actions\/cache@/i, 'Generated evidence and secrets must not use a broad actions/cache entry.');
for (const match of source.matchAll(/^\s*(environment|id-token|packages):\s*(\S+)\s*$/gmi)) {
  assert.equal(match[2], 'read', `CI cannot request ${match[1]}=${match[2]}.`);
}

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
  assert.match(block, /if-no-files-found:\s*error\b/, 'Every artifact must fail when expected evidence is absent.');
  const pathBlock = block.match(/path:\s*\|\n([\s\S]*?)(?=\n\s{10}[a-z-]+:|$)/)?.[1] || '';
  const paths = pathBlock.split('\n').map(line => line.trim()).filter(Boolean);
  assert.ok(paths.length > 0, 'Artifact upload requires explicit paths.');
  for (const path of paths) {
    assert.ok(manifest.allowedArtifactPrefixes.some(prefix => path.startsWith(prefix)), `Artifact path '${path}' is outside the allowlist.`);
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
