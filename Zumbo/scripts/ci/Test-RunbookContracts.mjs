import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { createLocalEnvironment, repositoryRoot, validateLocalEnvironment } from '../operations/prepare-env.mjs';

const dockerCliImage = 'docker@sha256:402f150151fd6e68c8f9a0cb7c50d35ee3bf64268d920716b4f6dc93bf093830';
const runbooks = Object.freeze({
  'docs/runbooks/first-run.md': ['## Windows', '## Linux', 'prepare-env.mjs', 'preflight.mjs', '--wait'],
  'docs/runbooks/daily-use.md': ['## Başlatma', '## Güvenli Durdurma', '## Resume', 'down --remove-orphans'],
  'docs/runbooks/local-demo-walkthrough.md': [
    '## İlk Başlatma',
    '## Tekrar Başlatma',
    '## Kapsamı Sınırlı Güvenli Reset',
    '## Kısa Ürün Walkthrough',
    '## Veriyi Koruyan Güvenli Durdurma',
    'demo-start.mjs',
    'demo-stop.mjs',
    'demo-prepare.mjs'
  ],
  'docs/runbooks/troubleshooting.md': ['Get-NetTCPConnection', 'ss -ltnp', 'docker compose', 'health/ready'],
  'docs/runbooks/backup-restore.md': ['Backup-Zumbo.ps1', 'Restore-Zumbo.ps1', 'ConfirmIsolatedTarget'],
  'docs/runbooks/security-operations.md': ['Invoke-SecurityGate.ps1', 'secret', 'rotation']
});
const requiredTargets = [
  'Backend/.env.example',
  'Backend/docker-compose.yml',
  'Backend/scripts/Backup-Zumbo.ps1',
  'Backend/scripts/Restore-Zumbo.ps1',
  'docs/operations/backup-restore-and-provider-migration.md',
  'docs/operations/production-like-container-runbook.md',
  'docs/security/security-gates.md',
  'scripts/operations/prepare-env.mjs',
  'scripts/operations/preflight.mjs',
  'scripts/operations/bootstrap-admin.mjs',
  'scripts/operations/demo-start.mjs',
  'scripts/operations/demo-stop.mjs',
  'scripts/operations/demo-prepare.mjs'
];
const checks = [];
const temporaryEnvironment = resolve(repositoryRoot, `Backend/.env.qa002-contract-${process.pid}`);
const skipHostPreflight = process.argv.includes('--skip-host-preflight');
const commandTimeoutMilliseconds = 300_000;

try {
  for (const [path, markers] of Object.entries(runbooks)) {
    const absolutePath = resolve(repositoryRoot, path);
    assert.ok(existsSync(absolutePath), `Missing mandatory runbook: ${path}`);
    const content = readFileSync(absolutePath, 'utf8');
    for (const marker of markers) assert.ok(content.includes(marker), `${path} is missing '${marker}'.`);
    assert.doesNotMatch(content, /docker\s+(?:system|volume|network|builder)\s+prune/i, `${path} contains a global prune command.`);
    assert.doesNotMatch(content, /docker\s+compose[^\n]*(?:down\s+-v|down[^\n]*--volumes)/i, `${path} contains destructive volume cleanup.`);
    checks.push({ name: `runbook:${path}`, passed: true });
  }

  for (const path of requiredTargets) {
    assert.ok(existsSync(resolve(repositoryRoot, path)), `Referenced target does not exist: ${path}`);
  }
  checks.push({ name: 'referenced-targets', passed: true, count: requiredTargets.length });

  const demoStart = readFileSync(resolve(repositoryRoot, 'scripts/operations/demo-start.mjs'), 'utf8');
  assert.match(demoStart, /const composeWaitTimeoutSeconds = 900;/);
  assert.match(demoStart, /const composeCommandTimeoutMilliseconds = 930_000;/);
  assert.match(demoStart, /const readinessRequestTimeoutMilliseconds = 60_000;/);

  const compose = readFileSync(resolve(repositoryRoot, 'Backend/docker-compose.yml'), 'utf8');
  assert.match(compose, /HealthChecks__DependencyTimeoutSeconds: 30/);
  assert.match(compose, /Gateway__UpstreamTimeoutSeconds: 60/);
  assert.match(compose, /opensearch:[\s\S]*?retries: 60[\s\S]*?start_period: 120s/);

  const apiHealthRegistration = readFileSync(resolve(
    repositoryRoot,
    'Backend/src/Zumbo.Api/Composition/Hosting/Registrars/ApiHostOperationsRegistrar.cs'
  ), 'utf8');
  assert.match(apiHealthRegistration, /GetValue\("HealthChecks:DependencyTimeoutSeconds", 5\)/);
  assert.match(apiHealthRegistration, /dependencyHealthTimeoutSeconds is < 1 or > 120/);
  checks.push({ name: 'demo-readiness-timeouts', passed: true });

  const environmentResult = createLocalEnvironment(temporaryEnvironment);
  assert.equal(environmentResult.loopbackOnly, true);
  assert.ok(environmentResult.generatedSecretKeys >= 10);
  assert.throws(() => createLocalEnvironment(temporaryEnvironment), /will not be overwritten/);
  validateLocalEnvironment(temporaryEnvironment);
  run(process.execPath, [
    resolve(repositoryRoot, 'scripts/operations/bootstrap-admin.mjs'),
    '--environment', temporaryEnvironment, '--check'
  ]);
  checks.push({ name: 'environment-generation', passed: true, overwriteRefused: true });

  if (!skipHostPreflight) {
    run(process.execPath, [
      resolve(repositoryRoot, 'scripts/operations/preflight.mjs'),
      '--environment', temporaryEnvironment
    ]);
    checks.push({ name: 'windows-preflight', passed: true });
  } else {
    checks.push({ name: 'windows-preflight', passed: false, skipped: true, reason: 'Explicit static contract mode.' });
  }

  run('docker', [
    'compose', '--project-name', 'zumbo-qa002-windows', '--env-file', temporaryEnvironment,
    '-f', resolve(repositoryRoot, 'Backend/docker-compose.yml'), 'config', '--quiet'
  ]);
  checks.push({ name: 'windows-compose-config', passed: true });

  run('docker', [
    'run', '--rm',
    '--volume', `${repositoryRoot}:/workspace`,
    '--workdir', '/workspace',
    dockerCliImage,
    'docker', 'compose', '--project-name', 'zumbo-qa002-linux',
    '--env-file', `/workspace/Backend/${temporaryEnvironment.split(/[\\/]/).at(-1)}`,
    '-f', '/workspace/Backend/docker-compose.yml', 'config', '--quiet'
  ]);
  checks.push({ name: 'linux-compose-config', passed: true, image: dockerCliImage });

  const result = {
    schemaVersion: 1,
    task: 'QA-002',
    generatedAtUtc: new Date().toISOString(),
    passed: true,
    mode: skipHostPreflight ? 'static-contract' : 'full-contract',
    mandatoryRunbooks: Object.keys(runbooks).length,
    checks
  };
  const evidencePath = argumentValue('--evidence');
  if (evidencePath) {
    const absoluteEvidence = resolve(evidencePath);
    mkdirSync(dirname(absoluteEvidence), { recursive: true });
    writeFileSync(absoluteEvidence, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
  }
  console.log(`Runbook contracts passed: ${Object.keys(runbooks).length} mandatory files, ${checks.length} checks.`);
} finally {
  rmSync(temporaryEnvironment, { force: true });
}

function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    timeout: commandTimeoutMilliseconds
  });
  assert.ifError(result.error);
  assert.equal(result.status, 0, `${command} ${args.join(' ')}\n${result.stderr || result.stdout}`);
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}
