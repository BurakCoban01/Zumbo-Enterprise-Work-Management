import { cpus, freemem, totalmem } from 'node:os';
import { mkdirSync, readFileSync, statfsSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import {
  assertSha,
  expectedServices,
  repositoryRoot,
  requireArgument
} from './qa002-common.mjs';

const targetSha = assertSha(requireArgument('--target-sha'), 'target SHA');
const environmentPath = resolve(requireArgument('--environment'));
const outputPath = resolve(requireArgument('--output'));
const checks = [];

check('linuxRunner', () => {
  if (process.platform !== 'linux') throw new Error('QA-002 clean-Linux preflight is Linux-only.');
  return { platform: process.platform };
});

const initialResources = check('initialResources', () => ({
  containers: list('docker', ['ps', '--all', '--quiet']).length,
  networks: list('docker', ['network', 'ls', '--quiet']).length,
  volumes: list('docker', ['volume', 'ls', '--quiet']).length
})) || { containers: -1, networks: -1, volumes: -1 };
const head = check('exactCommit', () => command('git', ['rev-parse', 'HEAD']));
check('cleanCheckout', () => {
  const status = command('git', ['status', '--porcelain=v1', '--untracked-files=all']);
  if (status) throw new Error('Checkout is not clean before QA-002 execution.');
  if (head !== targetSha) throw new Error(`Checked out ${head}; expected ${targetSha}.`);
  return { head, clean: true };
});

const requiredFiles = [
  'Backend/.env.example',
  'Backend/docker-compose.yml',
  'Backend/src/Zumbo.Api/Dockerfile',
  'Backend/src/Zumbo.Gateway/Dockerfile',
  'scripts/operations/prepare-env.mjs',
  'scripts/operations/bootstrap-admin.mjs',
  'scripts/operations/qa002-common.mjs',
  'scripts/operations/qa002-linux-lifecycle.mjs',
  'scripts/operations/qa002-cleanup.mjs',
  'scripts/operations/qa002-evidence.mjs',
  'docs/runbooks/first-run.md',
  'docs/runbooks/daily-use.md',
  'docs/runbooks/troubleshooting.md',
  'docs/runbooks/backup-restore.md',
  'docs/runbooks/security-operations.md'
];
check('requiredFiles', () => {
  const missing = requiredFiles.filter(path => !readable(resolve(repositoryRoot, path)));
  if (missing.length) throw new Error(`Required files are missing: ${missing.join(', ')}`);
  return { count: requiredFiles.length };
});

const composeJson = check('composeConfig', () => JSON.parse(command('docker', [
  'compose', '--project-name', 'zumbo-qa002-preflight', '--env-file', environmentPath,
  '-f', resolve(repositoryRoot, 'Backend/docker-compose.yml'), 'config', '--format', 'json'
], 120_000))) || {};
const configuredServices = Object.keys(composeJson.services || {}).sort();
check('serviceInventory', () => {
  if (JSON.stringify(configuredServices) !== JSON.stringify([...expectedServices].sort())) {
    throw new Error(`Expected ${expectedServices.join(', ')}; observed ${configuredServices.join(', ')}.`);
  }
  const apiHealth = readFileSync(resolve(repositoryRoot, 'Backend/src/Zumbo.Api/Dockerfile'), 'utf8').includes('HEALTHCHECK');
  const gatewayHealth = readFileSync(resolve(repositoryRoot, 'Backend/src/Zumbo.Gateway/Dockerfile'), 'utf8').includes('HEALTHCHECK');
  const withoutHealth = expectedServices.filter(name => {
    if (name === 'mongo-init-replica') return false;
    if (name === 'api' || name === 'worker') return !apiHealth;
    if (name === 'gateway') return !gatewayHealth;
    return !composeJson.services[name]?.healthcheck;
  });
  if (withoutHealth.length) throw new Error(`Services without healthchecks: ${withoutHealth.join(', ')}`);
  return { expected: expectedServices, observed: configuredServices };
});

const gatewayPorts = composeJson.services?.gateway?.ports || [];
check('portAndReadinessContract', () => {
  const gateway = gatewayPorts[0];
  if (gateway?.host_ip !== '127.0.0.1' || Number(gateway?.target) !== 8080) {
    throw new Error('Gateway must publish only target 8080 on loopback.');
  }
  for (const service of expectedServices.filter(name => name !== 'gateway')) {
    if ((composeJson.services?.[service]?.ports || []).length) throw new Error(`${service} unexpectedly publishes a host port.`);
  }
  return {
    gateway: `${gateway.host_ip}:${gateway.published}->${gateway.target}`,
    endpoints: ['/health/live', '/health/ready'],
    frontendPortContract: '127.0.0.1:58177'
  };
});

const runtime = check('runnerRuntime', () => {
  const disk = statfsSync(repositoryRoot);
  const osRelease = parseOsRelease(readFileSync('/etc/os-release', 'utf8'));
  return {
    osRelease: {
      id: osRelease.ID,
      versionId: osRelease.VERSION_ID,
      prettyName: osRelease.PRETTY_NAME
    },
    kernel: command('uname', ['-srmo']),
    cpuCount: cpus().length,
    memory: {
      totalMiB: Math.floor(totalmem() / 1024 / 1024),
      availableMiB: Math.floor(freemem() / 1024 / 1024)
    },
    disk: {
      totalMiB: Math.floor(Number(disk.blocks) * Number(disk.bsize) / 1024 / 1024),
      availableMiB: Math.floor(Number(disk.bavail) * Number(disk.bsize) / 1024 / 1024)
    },
    dockerVersion: command('docker', ['version', '--format', '{{.Server.Version}}']),
    composeVersion: command('docker', ['compose', 'version', '--short'])
  };
}) || {};
const result = {
  schemaVersion: 2,
  task: 'QA-002',
  generatedAtUtc: new Date().toISOString(),
  passed: checks.every(item => item.passed),
  targetCommitSha: targetSha,
  runner: {
    image: process.env.ImageOS === 'ubuntu24' ? 'ubuntu-24.04' : (process.env.QA002_RUNNER_IMAGE || 'ubuntu-24.04'),
    os: 'Linux',
    osRelease: runtime.osRelease || {},
    kernel: runtime.kernel || 'unavailable',
    cpuCount: runtime.cpuCount || 0,
    memory: runtime.memory || {},
    disk: runtime.disk || {}
  },
  dockerVersion: runtime.dockerVersion || 'unavailable',
  composeVersion: runtime.composeVersion || 'unavailable',
  initialResources,
  repositoryClean: checks.find(item => item.name === 'cleanCheckout')?.passed === true,
  composeConfigPassed: checks.find(item => item.name === 'composeConfig')?.passed === true,
  requiredFiles,
  serviceInventoryExpected: expectedServices,
  serviceInventoryConfigured: configuredServices,
  portReadinessContract: checks.find(item => item.name === 'portAndReadinessContract')?.detail,
  checks
};

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
console.log(`QA-002 preflight: ${result.passed ? 'passed' : 'failed'}; ${expectedServices.length} services configured.`);
if (!result.passed) process.exitCode = 1;

function check(name, operation) {
  try {
    const detail = operation();
    checks.push({ name, passed: true, detail });
    return detail;
  } catch (error) {
    checks.push({ name, passed: false, detail: error.message });
    return undefined;
  }
}

function command(name, args, timeout = 30_000) {
  const result = spawnSync(name, args, { cwd: repositoryRoot, encoding: 'utf8', timeout, maxBuffer: 10 * 1024 * 1024 });
  if (result.status !== 0) throw new Error((result.stderr || result.stdout || `${name} failed`).trim());
  return result.stdout.trim();
}

function list(name, args) {
  const output = command(name, args);
  return output ? output.split(/\r?\n/).filter(Boolean) : [];
}

function readable(path) {
  try { readFileSync(path); return true; } catch { return false; }
}

function parseOsRelease(text) {
  return Object.fromEntries(text.split(/\r?\n/).filter(line => line.includes('=')).map(line => {
    const separator = line.indexOf('=');
    return [line.slice(0, separator), line.slice(separator + 1).replace(/^"|"$/g, '')];
  }));
}
