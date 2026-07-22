import { mkdirSync, statfsSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { freemem } from 'node:os';
import { resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { parseEnvironment, repositoryRoot, validateLocalEnvironment } from './prepare-env.mjs';
import { readFileSync } from 'node:fs';

const environmentPath = resolve(argumentValue('--environment') || 'Backend/.env');
const minimumMemoryMiB = numberArgument('--minimum-free-memory-mib', 2048);
const minimumDiskMiB = numberArgument('--minimum-free-disk-mib', 4096);
const environment = parseEnvironment(readFileSync(environmentPath, 'utf8')).values;
const checks = [];

await check('environment', () => validateLocalEnvironment(environmentPath));
await check('node', () => {
  const [major, minor] = process.versions.node.split('.').map(Number);
  if (major !== 20 || minor < 9) throw new Error(`Node 20.9.x-20.x required; found ${process.versions.node}.`);
  return process.versions.node;
});
await check('pnpm', () => {
  const version = command('pnpm', ['--version']);
  if (version !== '9.0.0') throw new Error(`pnpm 9.0.0 required; found ${version}.`);
  return version;
});
await check('dotnet', () => {
  const sdks = command('dotnet', ['--list-sdks']);
  if (!sdks.split(/\r?\n/).some(line => /^8\.|^9\./.test(line))) {
    throw new Error('A .NET SDK capable of targeting .NET 8 is required.');
  }
  return sdks.split(/\r?\n/).filter(Boolean);
});
await check('docker', () => command('docker', ['version', '--format', '{{.Client.Version}}|{{.Server.Version}}']));
await check('compose', () => command('docker', ['compose', 'version', '--short']));
await check('memory', () => {
  const freeMiB = Math.floor(freemem() / 1024 / 1024);
  if (freeMiB < minimumMemoryMiB) throw new Error(`Free memory ${freeMiB} MiB is below ${minimumMemoryMiB} MiB.`);
  return { freeMiB, minimumMiB: minimumMemoryMiB };
});
await check('disk', () => {
  const stats = statfsSync(repositoryRoot);
  const freeMiB = Math.floor(Number(stats.bavail) * Number(stats.bsize) / 1024 / 1024);
  if (freeMiB < minimumDiskMiB) throw new Error(`Free disk ${freeMiB} MiB is below ${minimumDiskMiB} MiB.`);
  return { freeMiB, minimumMiB: minimumDiskMiB };
});
await check('gateway-port', () => probePort(environment.ZUMBO_GATEWAY_BIND_HOST, environment.ZUMBO_GATEWAY_PORT));
await check('frontend-port', () => probePort(environment.ZUMBO_FRONTEND_BIND_HOST, environment.ZUMBO_FRONTEND_PORT));
await check('compose-config', () => command('docker', [
  'compose', '--project-name', 'zumbo-preflight', '--env-file', environmentPath,
  '-f', resolve(repositoryRoot, 'Backend/docker-compose.yml'), 'config', '--quiet'
]));

const result = {
  schemaVersion: 1,
  task: 'QA-002',
  generatedAtUtc: new Date().toISOString(),
  passed: checks.every(item => item.passed),
  checks
};
const evidencePath = argumentValue('--evidence');
if (evidencePath) {
  const absoluteEvidence = resolve(evidencePath);
  mkdirSync(resolve(absoluteEvidence, '..'), { recursive: true });
  writeFileSync(absoluteEvidence, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
}
console.log(JSON.stringify(result, null, 2));
if (!result.passed) process.exitCode = 1;

async function check(name, operation) {
  try {
    checks.push({ name, passed: true, detail: await operation() });
  } catch (error) {
    checks.push({ name, passed: false, detail: error.message });
  }
}

function command(name, args) {
  const executable = process.platform === 'win32' && name === 'pnpm' ? 'pnpm.cmd' : name;
  const result = spawnSync(executable, args, {
    cwd: repositoryRoot,
    encoding: 'utf8'
  });
  if (result.status !== 0) throw new Error((result.stderr || result.stdout || `${name} failed`).trim());
  return result.stdout.trim();
}

function probePort(host, rawPort) {
  const port = Number.parseInt(rawPort, 10);
  if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error(`Invalid local port: ${rawPort}`);
  return new Promise((accept, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(port, host, () => server.close(error => error ? reject(error) : accept(`${host}:${port}`)));
  });
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function numberArgument(name, fallback) {
  const raw = argumentValue(name);
  if (raw === undefined) return fallback;
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isInteger(parsed) || parsed < 1) throw new Error(`${name} must be a positive integer.`);
  return parsed;
}
