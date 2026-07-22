import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import {
  assertProjectName,
  expectedServices,
  parseComposeJsonLines,
  parseEnvironmentFile,
  redact,
  repositoryRoot,
  requireArgument,
  sha256,
  syntheticSecret
} from './qa002-common.mjs';

if (process.platform !== 'linux') throw new Error('QA-002 lifecycle is Linux-only.');

const project = assertProjectName(requireArgument('--project'));
const environmentPath = resolve(requireArgument('--environment'));
const outputPath = resolve(requireArgument('--output'));
const environment = parseEnvironmentFile(environmentPath);
const apiUrl = new URL(environment.ZUMBO_API_URL).toString().replace(/\/$/, '');
const adminPassword = `Qa2!${syntheticSecret(24)}`;
const stamp = `${process.env.GITHUB_RUN_ID || Date.now()}-${process.env.GITHUB_RUN_ATTEMPT || '1'}`.toLowerCase();
const username = `qa002admin${stamp.replace(/[^a-z0-9]/g, '')}`.slice(0, 48);
const organizationId = `qa002-${stamp}`.slice(0, 64);
const markerName = `QA-002 persistent marker ${stamp}`;
const secrets = [adminPassword, ...Object.entries(environment)
  .filter(([name]) => /(PASSWORD|TOKEN|SECRET|SIGNING_KEY|ROOT_USER)/.test(name))
  .map(([, value]) => value)];
const steps = [];
const timings = {};
let firstInventory = [];
let secondInventory = [];
let markerFingerprint = null;
let result;

console.log(`::add-mask::${adminPassword}`);

try {
  await step('environmentPrepared', async () => ({ syntheticCredentials: true, loopbackOnly: true }));
  await commandStep('hostRestoreBuild', 'dotnet', ['restore', 'Backend/Zumbo.sln'], 12 * 60_000);
  await commandStep('hostRestoreBuild', 'dotnet', ['build', 'Backend/Zumbo.sln', '--configuration', 'Release', '--no-restore'], 12 * 60_000, true);
  await commandStep('frontendInstallBuild', 'pnpm', ['--dir', 'Frontend', 'install', '--frozen-lockfile'], 12 * 60_000);
  await commandStep('frontendInstallBuild', 'pnpm', ['--dir', 'Frontend', 'run', 'build'], 8 * 60_000, true);
  await composeStep('composeConfig', ['config', '--quiet'], 2 * 60_000);
  await composeStep('composeBuild', ['build', '--pull', 'api', 'gateway'], 20 * 60_000);
  await composeStep('firstStart', ['up', '--detach', '--no-build', '--wait', '--wait-timeout', '600'], 12 * 60_000);
  firstInventory = await step('firstReadiness', () => verifyReadiness('first'));

  const bootstrap = await step('initialBootstrap', () => register({ username, organizationId }));
  if (!bootstrap.userId || !bootstrap.accessToken || !bootstrap.roles.includes('SystemAdmin')) {
    throw new Error('Initial bootstrap did not return a SystemAdmin identity.');
  }
  const marker = await step('persistentMarkerCreate', () => createMarker(bootstrap.accessToken));
  markerFingerprint = sha256(`${marker.id}\0${marker.tenantKey}\0${marker.name}`);
  await step('persistentMarkerInitialRead', () => verifyMarker(bootstrap.accessToken, marker));

  const volumesBeforeStop = projectVolumes();
  if (!volumesBeforeStop.length) throw new Error('No project-scoped persistent volumes were observed before safe stop.');
  await composeStep('safeStop', ['stop', '--timeout', '60'], 4 * 60_000);
  const volumesAfterStop = projectVolumes();
  if (JSON.stringify(volumesAfterStop) !== JSON.stringify(volumesBeforeStop)) {
    throw new Error('Safe stop changed the project volume inventory.');
  }

  await composeStep('resume', ['up', '--detach', '--no-build', '--wait', '--wait-timeout', '600'], 12 * 60_000);
  secondInventory = await step('secondReadiness', () => verifyReadiness('resume'));
  const login = await step('persistentMarkerPreserved', async () => {
    const auth = await loginAdmin();
    await verifyMarker(auth.accessToken, marker);
    return { markerFingerprint, loginAfterResume: true };
  });
  if (!login.loginAfterResume) throw new Error('Post-resume login marker gate failed.');

  await step('duplicateBootstrapRejected', async () => {
    const duplicate = await apiRequest('/api/auth/register', {
      method: 'POST',
      body: {
        username: `${username}second`.slice(0, 60),
        email: environment.ZUMBO_IDENTITY_ADMIN_EMAIL,
        password: adminPassword,
        organizationId,
        bootstrapToken: environment.ZUMBO_IDENTITY_BOOTSTRAP_TOKEN
      },
      allowError: true
    });
    const errorCode = duplicate.payload?.error?.code;
    if (duplicate.status !== 409 || errorCode !== 'BOOTSTRAP_ALREADY_COMPLETED') {
      throw new Error(`Duplicate bootstrap was not rejected by the canonical contract (HTTP ${duplicate.status}, code ${errorCode || 'missing'}).`);
    }
    return { attempted: true, rejected: true, httpStatus: 409, errorCode };
  });

  result = buildResult(true, null);
} catch (error) {
  const blocker = redact(error.message, secrets).slice(0, 1000);
  console.error(`QA-002 lifecycle failed: ${blocker}`);
  result = buildResult(false, blocker || 'Lifecycle failed without a safe diagnostic.');
  process.exitCode = 1;
} finally {
  mkdirSync(dirname(outputPath), { recursive: true });
  writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
}

function buildResult(passed, blocker) {
  const stepMap = new Map(steps.map(item => [item.name, item]));
  return {
    schemaVersion: 2,
    task: 'QA-002',
    generatedAtUtc: new Date().toISOString(),
    passed,
    blocker,
    project,
    serviceInventoryExpected: expectedServices,
    firstServiceInventory: firstInventory,
    secondServiceInventory: secondInventory,
    allServicesReady: firstInventory.length === expectedServices.length
      && secondInventory.length === expectedServices.length
      && [...firstInventory, ...secondInventory].every(item => item.ready),
    firstRunPassed: stepMap.get('firstStart')?.passed === true && stepMap.get('firstReadiness')?.passed === true,
    initialBootstrapPassed: stepMap.get('initialBootstrap')?.passed === true,
    persistentMarkerCreated: stepMap.get('persistentMarkerCreate')?.passed === true,
    safeStopPassed: stepMap.get('safeStop')?.passed === true,
    resumePassed: stepMap.get('resume')?.passed === true,
    persistentMarkerPreserved: stepMap.get('persistentMarkerPreserved')?.passed === true,
    duplicateBootstrapAttempted: steps.some(item => item.name === 'duplicateBootstrapRejected'),
    duplicateBootstrapRejected: stepMap.get('duplicateBootstrapRejected')?.passed === true,
    markerFingerprint,
    steps,
    timings
  };
}

async function step(name, operation) {
  const started = Date.now();
  try {
    const detail = await operation();
    const durationMs = Date.now() - started;
    timings[name] = (timings[name] || 0) + durationMs;
    upsertStep(name, true, detail, durationMs);
    return detail;
  } catch (error) {
    const durationMs = Date.now() - started;
    timings[name] = (timings[name] || 0) + durationMs;
    upsertStep(name, false, redact(error.message, secrets).slice(0, 500), durationMs);
    throw error;
  }
}

function upsertStep(name, passed, detail, durationMs) {
  const existing = steps.find(item => item.name === name);
  if (existing) {
    existing.passed &&= passed;
    existing.commands = (existing.commands || 1) + 1;
    existing.durationMs += durationMs;
    return;
  }
  steps.push({ name, passed, detail: safeDetail(detail), durationMs, commands: 1 });
}

async function commandStep(stepName, executable, args, timeout, append = false) {
  return step(stepName, () => {
    const commandResult = run(executable, args, timeout);
    return { command: `${executable} ${args.join(' ')}`, exitCode: commandResult.status, appended: append };
  });
}

async function composeStep(stepName, args, timeout) {
  return commandStep(stepName, 'docker', composeArgs(args), timeout);
}

function composeArgs(args) {
  return ['compose', '--project-name', project, '--env-file', environmentPath,
    '-f', resolve(repositoryRoot, 'Backend/docker-compose.yml'), ...args];
}

function run(executable, args, timeout = 60_000) {
  const command = process.platform === 'win32' && executable === 'pnpm' ? 'pnpm.cmd' : executable;
  const completed = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    timeout,
    maxBuffer: 32 * 1024 * 1024,
    env: process.env
  });
  if (completed.status !== 0) {
    const tail = redact(`${completed.stderr || ''}\n${completed.stdout || ''}`, secrets).trim().slice(-4000);
    throw new Error(`${executable} failed with exit ${completed.status ?? 'timeout'}.${tail ? `\n${tail}` : ''}`);
  }
  return completed;
}

async function verifyReadiness(stage) {
  const deadline = Date.now() + 180_000;
  let last = [];
  while (Date.now() < deadline) {
    const output = run('docker', composeArgs(['ps', '--all', '--format', 'json']), 30_000).stdout;
    const entries = parseComposeJsonLines(output);
    last = expectedServices.map(service => {
      const item = entries.find(entry => (entry.Service || entry.service) === service);
      const state = String(item?.State || item?.state || '').toLowerCase();
      const health = String(item?.Health || item?.health || '').toLowerCase();
      const exitCode = Number(item?.ExitCode ?? item?.exitCode ?? -1);
      const ready = service === 'mongo-init-replica'
        ? state === 'exited' && exitCode === 0
        : state === 'running' && health === 'healthy';
      return { service, state: state || 'missing', health: health || 'none', exitCode, ready };
    });
    if (last.every(item => item.ready)) {
      const live = await apiRequest('/health/live', { allowText: true });
      const ready = await apiRequest('/health/ready', { allowText: true });
      if (live.status === 200 && ready.status === 200) return last;
    }
    await new Promise(accept => setTimeout(accept, 3000));
  }
  const failed = last.filter(item => !item.ready).map(item => `${item.service}:${item.state}/${item.health}/${item.exitCode}`);
  throw new Error(`${stage} readiness timed out: ${failed.join(', ')}`);
}

async function register({ username: requestedUsername, organizationId: requestedOrganization }) {
  const response = await apiRequest('/api/auth/register', {
    method: 'POST',
    body: {
      username: requestedUsername,
      email: environment.ZUMBO_IDENTITY_ADMIN_EMAIL,
      password: adminPassword,
      organizationId: requestedOrganization,
      bootstrapToken: environment.ZUMBO_IDENTITY_BOOTSTRAP_TOKEN
    }
  });
  return {
    userId: response.payload?.data?.user?.id,
    accessToken: response.payload?.data?.accessToken,
    roles: response.payload?.data?.user?.roles || []
  };
}

async function loginAdmin() {
  const response = await apiRequest('/api/auth/login', {
    method: 'POST',
    body: { usernameOrEmail: environment.ZUMBO_IDENTITY_ADMIN_EMAIL, password: adminPassword }
  });
  const accessToken = response.payload?.data?.accessToken;
  if (!accessToken) throw new Error('Post-resume login did not return an access token.');
  return { accessToken };
}

async function createMarker(token) {
  const response = await apiRequest('/api/organizations', {
    method: 'POST', token, body: { name: markerName, tenantKey: organizationId }
  });
  const marker = response.payload?.data;
  if (response.status !== 201 || !marker?.id || marker.tenantKey !== organizationId) {
    throw new Error('Persistent marker organization was not created by the supported API contract.');
  }
  return { id: marker.id, tenantKey: marker.tenantKey, name: marker.name };
}

async function verifyMarker(token, marker) {
  const response = await apiRequest('/api/organizations', { token });
  const organizations = response.payload?.data;
  const found = Array.isArray(organizations) && organizations.some(item =>
    item.id === marker.id && item.tenantKey === marker.tenantKey && item.name === marker.name);
  if (!found) throw new Error('Persistent marker was not returned by the supported API contract.');
  return { markerFingerprint: sha256(`${marker.id}\0${marker.tenantKey}\0${marker.name}`), found: true };
}

async function apiRequest(path, { method = 'GET', token, body, allowError = false, allowText = false } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 30_000);
  try {
    const response = await fetch(`${apiUrl}${path}`, {
      method,
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(body ? { 'Content-Type': 'application/json' } : {})
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal
    });
    const text = await response.text();
    const payload = allowText ? text : (text ? JSON.parse(text) : {});
    if (!response.ok && !allowError) throw new Error(`API ${path} failed with HTTP ${response.status}.`);
    return { status: response.status, payload };
  } finally {
    clearTimeout(timer);
  }
}

function projectVolumes() {
  const output = run('docker', ['volume', 'ls', '--quiet', '--filter', `label=com.docker.compose.project=${project}`], 30_000).stdout.trim();
  return output ? output.split(/\r?\n/).filter(Boolean).sort() : [];
}

function safeDetail(detail) {
  if (typeof detail === 'string') return redact(detail, secrets).slice(0, 500);
  if (!detail || typeof detail !== 'object') return detail;
  const copy = structuredClone(detail);
  delete copy.accessToken;
  delete copy.userId;
  delete copy.id;
  delete copy.tenantKey;
  delete copy.name;
  return copy;
}
