import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  applicationRoot,
  gitRepositoryRoot
} from '../repository-layout.mjs';

if (process.argv.includes('--print-source-manifest')) {
  console.log(JSON.stringify(buildSourceManifest(), null, 2));
  process.exit(0);
}

const evidencePath = resolve(applicationRoot, 'artifacts/v3/V3-BASE-002.json');
assert.ok(existsSync(evidencePath), 'V3-BASE-002 machine evidence is missing.');
const evidence = JSON.parse(readFileSync(evidencePath, 'utf8'));

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-BASE-002');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.match(evidence.sourceCommit, /^[0-9a-f]{40}$/);
assert.equal(evidence.remote.run.headSha, evidence.sourceCommit, 'Remote CI must target the recorded source commit.');
assert.deepEqual(evidence.sourceManifest, {
  fileCount: 454,
  totalBytes: 3669973,
  manifestSha256: '745af8e844c6fee4c4de215be032baa744c53319bd018f5cb8226600ad6ffaf1',
  solutionSha256: '4ed279f06d59524f7029bfbcdd322158a867e9f0182f0af2e1582b81fab46910',
  frontendPackageSha256: '1a02c2eb40e2bd485e02e5cad29ef1d498d5f25daa5dcef48c6f6d3213989589',
  rootLockSha256: 'f46a703a67a28244ad12dbcf6da45c56923afb7f5a9d2ab3fed51b4436250376',
  frontendLockSha256: '35ed4ddd475f4aeecc066e74cc0e2f175290d5d782026240a953591ce325e6ee',
  ciWorkflowSha256: 'c63e49320b6da9fe7d029b8786b6cfd99cfccb5945c140f2b9ef0f43d340d32b'
}, 'V3-BASE-002 immutable source manifest changed.');

assert.deepEqual(evidence.local.dotnet.tests.total, { passed: 330, failed: 0, skipped: 0 });
assert.equal(evidence.local.dotnet.build.warnings, 0);
assert.equal(evidence.local.dotnet.build.errors, 0);
assert.equal(evidence.local.frontend.unit.passed, 73);
assert.equal(evidence.local.frontend.unit.failed, 0);
assert.equal(evidence.local.frontend.build.assets, 47);
assert.equal(evidence.local.frontend.dependencyAudit.critical, 0);
assert.equal(evidence.local.frontend.licenseAudit.packages, 22);
assert.equal(evidence.local.browserAssetSmoke.browser, 'chromium');
assert.equal(evidence.local.browserAssetSmoke.surfaces, 2);
assert.equal(evidence.local.browserAssetSmoke.passed, true);

assert.equal(evidence.remote.run.id, 29945545877);
assert.equal(evidence.remote.run.conclusion, 'success');
assert.equal(evidence.remote.jobs.filter(job => job.conclusion === 'success').length, 8);
assert.deepEqual(evidence.remote.providerTests.mongo, { passed: 66, failed: 0, skipped: 0 });
assert.equal(evidence.remote.providerTests.postgresql.passed, 73);
assert.equal(evidence.remote.providerTests.postgresql.filteredRerunPassed, 2);
assert.deepEqual(evidence.remote.providerTests.externalDependencies, { passed: 5, failed: 0, skipped: 0 });
assert.equal(evidence.remote.browserSmoke.engines, 1);
assert.equal(evidence.remote.browserSmoke.surfaces, 2);
assert.equal(evidence.remote.browserSmoke.passed, true);
assert.ok(evidence.notRunOrDeferred.length >= 3, 'Unavailable and heavy gates must remain explicit.');

console.log('V3-BASE-002 evidence passed: local .NET 330, frontend 73, Chromium 2 surfaces; remote provider/runtime jobs 8/8 successful.');

function buildSourceManifest() {
  const scopes = [
    '.github/workflows/ci.yml',
    'Zumbo/Backend',
    'Zumbo/Frontend'
  ];
  const result = spawnSync('git', ['ls-files', '-z', '--cached', '--', ...scopes], {
    cwd: gitRepositoryRoot,
    encoding: 'utf8',
    timeout: 30_000
  });
  assert.equal(result.status, 0, `Unable to inventory V3 baseline sources: ${result.stderr.trim()}`);
  const paths = result.stdout
    .split('\0')
    .filter(Boolean)
    .map(path => path.replaceAll('\\', '/'))
    .filter(path => existsSync(resolve(gitRepositoryRoot, path)) && statSync(resolve(gitRepositoryRoot, path)).isFile())
    .sort(compareCodePoints);

  let totalBytes = 0;
  const rows = paths.map(path => {
    const content = readFileSync(resolve(gitRepositoryRoot, path));
    totalBytes += content.length;
    return `${path}\0${content.length}\0${sha256(content)}`;
  });

  return {
    fileCount: paths.length,
    totalBytes,
    manifestSha256: sha256(Buffer.from(rows.join('\n'), 'utf8')),
    solutionSha256: fileSha('Zumbo/Backend/Zumbo.sln'),
    frontendPackageSha256: fileSha('Zumbo/Frontend/package.json'),
    frontendLockSha256: fileSha('Zumbo/Frontend/pnpm-lock.yaml'),
    ciWorkflowSha256: fileSha('.github/workflows/ci.yml')
  };
}

function fileSha(path) {
  return sha256(readFileSync(resolve(gitRepositoryRoot, path)));
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

function compareCodePoints(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}
