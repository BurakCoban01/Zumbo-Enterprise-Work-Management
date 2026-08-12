#!/usr/bin/env node
import { spawnSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync
} from 'node:fs';
import { createConnection } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, resolve } from 'node:path';
import {
  parseEnvironment,
  repositoryRoot,
  validateLocalEnvironment
} from './prepare-env.mjs';

const environmentPath = resolve(repositoryRoot, argumentValue('--environment') || 'Backend/.env');
const projectName = argumentValue('--project-name') || 'zumbo-local';
const evidencePath = resolve(
  repositoryRoot,
  argumentValue('--evidence') || 'artifacts/demo-readiness/DEMO-010-stop.json'
);
const composePath = resolve(repositoryRoot, 'Backend/docker-compose.yml');
const pidPath = resolve(tmpdir(), 'zumbo-demo', 'frontend-preview.pid.json');
const environment = parseEnvironment(readFileSync(environmentPath, 'utf8')).values;
const secretValues = Object.entries(environment)
  .filter(([name, value]) => value && /(PASSWORD|TOKEN|SIGNING_KEY|CONNECTION_STRING|REPLICA_KEY)/i.test(name))
  .map(([, value]) => value)
  .sort((left, right) => right.length - left.length);
const checks = [];

try {
  await check('environment', () => validateLocalEnvironment(environmentPath));
  const volumesBefore = await check('named-volumes-before', () => namedVolumes());
  await check('frontend-stop', () => stopOwnedFrontend());
  await check('compose-stop', () => command('docker', composeArguments('stop'), 180_000));
  await check('ports-released', () => waitForPortsReleased());
  const volumesAfter = await check('named-volumes-after', () => namedVolumes());
  if (JSON.stringify(volumesAfter) !== JSON.stringify(volumesBefore)) {
    throw new Error('Named volume inventory changed during the data-preserving stop.');
  }
  checks.push({
    name: 'named-volumes-preserved',
    passed: true,
    durationMs: 0,
    detail: { count: volumesAfter.length, names: volumesAfter }
  });

  const result = buildResult(true, volumesBefore, volumesAfter);
  writeEvidence(result);
  console.log(JSON.stringify({
    passed: true,
    task: result.task,
    projectName,
    frontendStopped: true,
    composeStopped: true,
    namedVolumesPreserved: volumesAfter.length,
    evidence: relativeEvidencePath()
  }, null, 2));
} catch (error) {
  const result = buildResult(false, undefined, undefined, sanitize(error?.message || String(error)));
  writeEvidence(result);
  console.error(result.blocker);
  process.exitCode = 1;
}

async function check(name, operation) {
  const started = Date.now();
  try {
    const detail = await operation();
    checks.push({ name, passed: true, durationMs: Date.now() - started, detail });
    return detail;
  } catch (error) {
    const detail = sanitize(error?.message || String(error));
    checks.push({ name, passed: false, durationMs: Date.now() - started, detail });
    throw error;
  }
}

async function stopOwnedFrontend() {
  const url = new URL(environment.ZUMBO_FRONTEND_URL);
  const port = Number(url.port);
  if (!existsSync(pidPath)) {
    if (await isPortOpen(url.hostname, port)) {
      throw new Error(
        `Frontend port ${url.hostname}:${port} is open without Zumbo ownership metadata. Stop its foreground terminal manually.`
      );
    }
    return { alreadyStopped: true };
  }

  const metadata = JSON.parse(readFileSync(pidPath, 'utf8'));
  if (metadata.schemaVersion !== 1
    || metadata.project !== projectName
    || metadata.origin !== environment.ZUMBO_FRONTEND_URL
    || !Number.isSafeInteger(metadata.pid)
    || metadata.pid <= 0) {
    throw new Error('Frontend ownership metadata does not match this environment and project.');
  }

  const identity = processIdentity(metadata.pid);
  if (!identity) {
    rmSync(pidPath, { force: true });
    if (await isPortOpen(url.hostname, port)) {
      throw new Error('The recorded frontend process is absent but the configured frontend port is still open.');
    }
    return { alreadyStopped: true, staleMetadataRemoved: true };
  }

  const normalizedCommand = identity.commandLine.replaceAll('\\', '/').toLowerCase();
  if (!identity.name.toLowerCase().startsWith('node')
    || !normalizedCommand.includes('tests/static-server.mjs')
    || !normalizedCommand.includes(' dist-modern')
    || !normalizedCommand.includes('--canonical')) {
    throw new Error('The recorded PID is not the owned Zumbo frontend preview process.');
  }

  process.kill(metadata.pid, 'SIGTERM');
  await waitUntil(() => !processExists(metadata.pid), 10_000, 'Frontend preview did not stop within 10000 ms.');
  rmSync(pidPath, { force: true });
  await waitUntil(
    async () => !(await isPortOpen(url.hostname, port)),
    10_000,
    `Frontend port ${url.hostname}:${port} was not released.`
  );
  return { pid: metadata.pid, identityVerified: true };
}

function processIdentity(pid) {
  if (!processExists(pid)) return undefined;
  if (process.platform === 'win32') {
    const script = [
      `$p = Get-CimInstance Win32_Process -Filter "ProcessId=${pid}"`,
      'if ($p) { [Console]::Out.Write(($p.Name + "`n" + $p.CommandLine)) }'
    ].join('; ');
    const result = spawnSync('powershell.exe', [
      '-NoProfile',
      '-NonInteractive',
      '-Command',
      script
    ], {
      cwd: repositoryRoot,
      encoding: 'utf8',
      timeout: 10_000,
      windowsHide: true
    });
    if (result.status !== 0 || !result.stdout.trim()) return undefined;
    const [name, ...command] = result.stdout.trim().split(/\r?\n/);
    return { name, commandLine: command.join(' ') };
  }

  const result = spawnSync('ps', ['-p', String(pid), '-o', 'comm=', '-o', 'args='], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    timeout: 10_000
  });
  if (result.status !== 0 || !result.stdout.trim()) return undefined;
  const line = result.stdout.trim();
  const separator = line.indexOf(' ');
  return {
    name: separator < 0 ? line : line.slice(0, separator),
    commandLine: separator < 0 ? line : line.slice(separator + 1)
  };
}

function processExists(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    if (error?.code === 'EPERM') return true;
    return false;
  }
}

function namedVolumes() {
  const output = command('docker', [
    'volume',
    'ls',
    '--filter', `label=com.docker.compose.project=${projectName}`,
    '--format', '{{.Name}}'
  ], 30_000);
  return output.split(/\r?\n/).map(value => value.trim()).filter(Boolean).sort();
}

async function waitForPortsReleased() {
  const targets = [
    new URL(environment.ZUMBO_GATEWAY_URL),
    new URL(environment.ZUMBO_FRONTEND_URL)
  ];
  await waitUntil(async () => {
    const states = await Promise.all(targets.map(url => isPortOpen(url.hostname, Number(url.port))));
    return states.every(open => !open);
  }, 30_000, 'Gateway or frontend loopback port was not released within 30000 ms.');
  return targets.map(url => ({ host: url.hostname, port: Number(url.port), listening: false }));
}

function isPortOpen(host, port) {
  return new Promise(resolvePromise => {
    const socket = createConnection({ host, port });
    const done = open => {
      socket.removeAllListeners();
      socket.destroy();
      resolvePromise(open);
    };
    socket.setTimeout(500);
    socket.once('connect', () => done(true));
    socket.once('timeout', () => done(false));
    socket.once('error', () => done(false));
  });
}

async function waitUntil(predicate, timeoutMs, message) {
  const deadline = Date.now() + timeoutMs;
  do {
    if (await predicate()) return;
    await new Promise(resolvePromise => setTimeout(resolvePromise, 250));
  } while (Date.now() < deadline);
  throw new Error(message);
}

function command(name, args, timeout) {
  const result = spawnSync(name, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    timeout,
    windowsHide: true,
    maxBuffer: 10 * 1024 * 1024
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error((result.stderr || result.stdout || `${name} failed with exit code ${result.status}`).trim());
  }
  return result.stdout.trim();
}

function composeArguments(...args) {
  return [
    'compose',
    '--project-name', projectName,
    '--env-file', environmentPath,
    '-f', composePath,
    ...args
  ];
}

function buildResult(passed, volumesBefore = null, volumesAfter = null, blocker = null) {
  return {
    schemaVersion: 1,
    task: 'DEMO-010',
    mode: 'stop',
    generatedAtUtc: new Date().toISOString(),
    projectName,
    passed,
    decision: passed ? 'stopped-data-preserved' : 'blocked',
    checks,
    namedVolumesBefore: volumesBefore,
    namedVolumesAfter: volumesAfter,
    blocker,
    noDeployment: true,
    noPublicExposure: true,
    noVolumeDeletion: true,
    noGlobalCleanup: true
  };
}

function writeEvidence(result) {
  mkdirSync(dirname(evidencePath), { recursive: true });
  writeFileSync(evidencePath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
}

function relativeEvidencePath() {
  return evidencePath.slice(repositoryRoot.length + 1).replaceAll('\\', '/');
}

function sanitize(value) {
  let result = String(value);
  for (const secretValue of secretValues) result = result.replaceAll(secretValue, '[redacted]');
  return result;
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}
