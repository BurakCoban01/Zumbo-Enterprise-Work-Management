import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  applicationRoot,
  gitRepositoryRoot
} from '../repository-layout.mjs';

const frontendRoot = resolve(applicationRoot, 'Frontend');
const authoritativeFiles = [
  'Frontend/package.json',
  'Frontend/pnpm-lock.yaml',
  'Frontend/pnpm-workspace.yaml',
  'Frontend/.npmrc'
];

assert.equal(existsSync(resolve(applicationRoot, 'pnpm-lock.yaml')), false, 'App-root pnpm lock must not duplicate Frontend authority.');
assert.equal(existsSync(resolve(applicationRoot, 'pnpm-workspace.yaml')), false, 'App-root pnpm workspace must not duplicate Frontend authority.');
for (const path of authoritativeFiles) {
  assert.ok(existsSync(resolve(applicationRoot, path)), `Authoritative frontend file is missing: ${path}`);
}

const tracked = spawnSync('git', ['ls-files', '-z', '--cached', '--', 'Zumbo/**/pnpm-lock.yaml', 'Zumbo/**/pnpm-workspace.yaml', 'Zumbo/pnpm-lock.yaml', 'Zumbo/pnpm-workspace.yaml'], {
  cwd: gitRepositoryRoot,
  encoding: 'utf8',
  timeout: 30_000
});
assert.equal(tracked.status, 0, `Unable to inspect tracked pnpm manifests: ${tracked.stderr.trim()}`);
const trackedExisting = tracked.stdout
  .split('\0')
  .filter(Boolean)
  .map(path => path.replaceAll('\\', '/'))
  .filter(path => existsSync(resolve(gitRepositoryRoot, path)))
  .sort();
assert.deepEqual(trackedExisting, ['Zumbo/Frontend/pnpm-lock.yaml', 'Zumbo/Frontend/pnpm-workspace.yaml']);

const packageJson = JSON.parse(readFileSync(resolve(frontendRoot, 'package.json'), 'utf8'));
assert.equal(packageJson.packageManager, 'pnpm@9.0.0');
assert.deepEqual(packageJson.engines, { node: '>=20.9.0 <21', pnpm: '9.0.0' });

const workspace = readFileSync(resolve(frontendRoot, 'pnpm-workspace.yaml'), 'utf8').replaceAll('\r\n', '\n').trim();
assert.equal(workspace, 'packages:\n  - .');
const lock = readFileSync(resolve(frontendRoot, 'pnpm-lock.yaml'), 'utf8');
assert.match(lock, /^lockfileVersion: '9\.0'/);
assert.match(lock, /^  \.:$/m, 'Frontend lock must contain the package-root importer.');

const npmrc = new Set(readFileSync(resolve(frontendRoot, '.npmrc'), 'utf8').split(/\r?\n/).filter(Boolean));
for (const policy of ['engine-strict=true', 'ignore-scripts=true', 'save-exact=true', 'shared-workspace-lockfile=true', 'strict-peer-dependencies=true']) {
  assert.ok(npmrc.has(policy), `Frontend/.npmrc is missing '${policy}'.`);
}

const workflow = readFileSync(resolve(gitRepositoryRoot, '.github/workflows/ci.yml'), 'utf8');
const pnpmWorkflowLines = workflow
  .split(/\r?\n/)
  .filter(line => /^\s+(?:run:\s+)?pnpm\b/.test(line));
assert.ok(pnpmWorkflowLines.length >= 10, 'CI pnpm command inventory is unexpectedly small.');
for (const line of pnpmWorkflowLines) {
  assert.match(line, /pnpm --dir Frontend/, `CI command bypasses the authoritative Frontend root: ${line.trim()}`);
}

const readme = readFileSync(resolve(applicationRoot, 'readme.md'), 'utf8');
const firstRun = readFileSync(resolve(applicationRoot, 'docs/runbooks/first-run.md'), 'utf8');
assert.match(readme, /Frontend\/` tek pnpm workspace köküdür/);
assert.match(firstRun, /Frontend\/` authoritative pnpm workspace köküdür/);

const evidence = JSON.parse(readFileSync(resolve(applicationRoot, 'artifacts/v3/V3-FE-001.json'), 'utf8'));
assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-FE-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.authoritativeRoot, 'Frontend');
assert.deepEqual(evidence.workspaceFilesAfter, ['Frontend/pnpm-lock.yaml', 'Frontend/pnpm-workspace.yaml']);
assert.equal(evidence.hashes.frontendLockSha256, sha256(readFileSync(resolve(frontendRoot, 'pnpm-lock.yaml'))));
assert.equal(evidence.hashes.frontendWorkspaceSha256, sha256(readFileSync(resolve(frontendRoot, 'pnpm-workspace.yaml'))));
assert.match(evidence.hashes.packageSha256, /^[a-f0-9]{64}$/, 'Historical package hash must remain a SHA-256 digest.');
assert.equal(evidence.validation.unit.passed, 73);
assert.equal(evidence.validation.build.assets, 47);
assert.equal(evidence.validation.browser.surfaces, 2);
assert.equal(evidence.validation.node24Negative.exitCode, 1);

console.log('V3-FE-001 workspace passed: one Frontend lock/workspace, pnpm 9.0, Node 20 policy, 73 unit tests, 47 assets and 2 Chromium surfaces.');

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}
