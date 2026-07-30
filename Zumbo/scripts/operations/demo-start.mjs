import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import {
  parseEnvironment,
  repositoryRoot,
  validateLocalEnvironment
} from './prepare-env.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const defaultEvidencePath = resolve(repositoryRoot, 'artifacts/demo-readiness/DEMO-002.json');
const environmentPath = resolve(repositoryRoot, argumentValue('--environment') || 'Backend/.env');
const projectName = argumentValue('--project-name') || 'zumbo-local';
const evidencePath = resolve(repositoryRoot, argumentValue('--evidence') || defaultEvidencePath);
const composePath = resolve(repositoryRoot, 'Backend/docker-compose.yml');
const frontendDirectory = resolve(repositoryRoot, 'Frontend');
const frontendDist = resolve(frontendDirectory, 'dist');
const environment = parseEnvironment(readFileSync(environmentPath, 'utf8')).values;
const secretValues = Object.entries(environment)
  .filter(([name, value]) => value && /(PASSWORD|TOKEN|SIGNING_KEY|CONNECTION_STRING|REPLICA_KEY)/i.test(name))
  .map(([, value]) => value)
  .sort((left, right) => right.length - left.length);
const checks = [];
let frontendProcess;

try {
  await check('environment', () => validateLocalEnvironment(environmentPath));
  await check('docker-engine', () => waitForDockerEngine(120_000));
  await check('compose-config', () => command('docker', composeArguments('config', '--quiet'), 30_000));

  if (process.argv.includes('--build')) {
    await check('frontend-build', () => command(pnpmExecutable(), ['--dir', 'Frontend', 'run', 'build'], 180_000));
    await check('container-build', () => command('docker', composeArguments('build', 'api', 'gateway'), 600_000));
  }

  await check('runtime-config', () => {
    const runtimeConfigPath = resolve(frontendDist, 'runtime-config.js');
    if (!existsSync(runtimeConfigPath)) throw new Error('Frontend dist/runtime-config.js is missing. Run with --build.');
    const content = readFileSync(runtimeConfigPath, 'utf8');
    if (!content.includes(JSON.stringify(environment.ZUMBO_API_URL))) {
      throw new Error('Frontend runtime config does not target the configured gateway URL. Run with --build.');
    }
    return { apiBaseUrl: environment.ZUMBO_API_URL };
  });

  await check('compose-up', () => command(
    'docker',
    composeArguments('up', '--detach', '--no-build', '--wait', '--wait-timeout', '300'),
    330_000
  ));

  await check('service-inventory', () => verifyServiceInventory());
  frontendProcess = await check('frontend', () => ensureFrontend());
  await check('http-readiness', () => verifyHttpReadiness());
  await check('login-entry', () => verifyLoginEntry());
  await check('loopback-publish', () => verifyLoopbackPublishing());

  const result = buildResult(true);
  writeEvidence(result);
  console.log(JSON.stringify({
    passed: true,
    task: result.task,
    reusedFrontend: frontendProcess.reused,
    frontendUrl: environment.ZUMBO_FRONTEND_URL,
    gatewayUrl: environment.ZUMBO_GATEWAY_URL,
    evidence: relativeEvidencePath()
  }, null, 2));
} catch (error) {
  const result = buildResult(false, sanitize(error?.message || String(error)));
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

async function waitForDockerEngine(timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastError = 'Docker engine is unavailable.';
  do {
    const result = spawnSync('docker', ['version', '--format', '{{.Server.Version}}'], {
      cwd: repositoryRoot,
      encoding: 'utf8',
      timeout: 10_000,
      windowsHide: true
    });
    if (result.status === 0 && result.stdout.trim()) return { serverVersion: result.stdout.trim() };
    lastError = sanitize((result.stderr || result.stdout || lastError).trim());
    await delay(2_000);
  } while (Date.now() < deadline);
  throw new Error(`Docker engine did not become ready within ${timeoutMs} ms. ${lastError}`);
}

function verifyServiceInventory() {
  const raw = command('docker', composeArguments('ps', '-a', '--format', 'json'), 30_000);
  const rows = raw.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
  const byService = new Map(rows.map(row => [row.Service, row]));
  const runningServices = ['api', 'gateway', 'worker', 'mongo', 'redis', 'minio', 'opensearch'];

  for (const service of runningServices) {
    const row = byService.get(service);
    if (!row) throw new Error(`Required service is missing: ${service}.`);
    if (row.State !== 'running' || row.Health !== 'healthy') {
      throw new Error(`Required service is not healthy: ${service} (${row.State}/${row.Health || 'no-health'}).`);
    }
  }

  const replicaInit = byService.get('mongo-init-replica');
  if (!replicaInit || replicaInit.State !== 'exited' || Number(replicaInit.ExitCode) !== 0) {
    throw new Error('mongo-init-replica did not complete successfully.');
  }

  return {
    expected: [...runningServices, 'mongo-init-replica'],
    healthy: runningServices,
    completed: ['mongo-init-replica']
  };
}

async function ensureFrontend() {
  const desktopUrl = new URL('/desktop-bulma/index.html', environment.ZUMBO_FRONTEND_URL).toString();
  const existing = await request(desktopUrl, { acceptedStatuses: [200], timeoutMs: 3_000 }).catch(() => undefined);
  if (existing?.status === 200 && /zumbo/i.test(existing.body)) return { reused: true };

  const url = new URL(environment.ZUMBO_FRONTEND_URL);
  await assertPortAvailable(url.hostname, Number(url.port));
  if (!existsSync(resolve(frontendDist, 'security-headers.json'))) {
    throw new Error('Frontend dist is missing. Run demo-start.mjs with --build.');
  }

  const child = spawn(process.execPath, ['tests/static-server.mjs', 'dist'], {
    cwd: frontendDirectory,
    detached: true,
    env: {
      ...process.env,
      ZUMBO_FRONTEND_PORT: url.port
    },
    stdio: 'ignore',
    windowsHide: true
  });
  child.unref();

  const pidPath = resolve(tmpdir(), 'zumbo-demo', 'frontend-preview.pid.json');
  mkdirSync(dirname(pidPath), { recursive: true });
  writeFileSync(pidPath, `${JSON.stringify({
    schemaVersion: 1,
    project: projectName,
    pid: child.pid,
    origin: environment.ZUMBO_FRONTEND_URL,
    startedAtUtc: new Date().toISOString()
  }, null, 2)}\n`, 'utf8');

  const deadline = Date.now() + 30_000;
  do {
    const response = await request(desktopUrl, { acceptedStatuses: [200], timeoutMs: 2_000 }).catch(() => undefined);
    if (response?.status === 200 && /zumbo/i.test(response.body)) {
      return { reused: false, processStarted: true };
    }
    await delay(500);
  } while (Date.now() < deadline);
  throw new Error('Frontend preview did not become ready within 30000 ms.');
}

async function verifyHttpReadiness() {
  const urls = {
    live: new URL('/health/live', environment.ZUMBO_GATEWAY_URL).toString(),
    ready: new URL('/health/ready', environment.ZUMBO_GATEWAY_URL).toString(),
    desktop: new URL('/desktop-bulma/index.html', environment.ZUMBO_FRONTEND_URL).toString(),
    mobile: new URL('/mobile-ionic/index.html', environment.ZUMBO_FRONTEND_URL).toString()
  };
  const statuses = {};
  for (const [name, url] of Object.entries(urls)) {
    statuses[name] = (await request(url, { acceptedStatuses: [200], timeoutMs: 10_000 })).status;
  }
  return statuses;
}

async function verifyLoginEntry() {
  const response = await request(new URL('/api/browser-auth/login', environment.ZUMBO_GATEWAY_URL).toString(), {
    acceptedStatuses: [401],
    timeoutMs: 10_000,
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      origin: environment.ZUMBO_FRONTEND_URL
    },
    body: JSON.stringify({
      usernameOrEmail: 'demo-readiness-missing@invalid.local',
      password: 'invalid-synthetic-password'
    })
  });
  const payload = JSON.parse(response.body);
  if (response.headers.get('access-control-allow-origin') !== environment.ZUMBO_FRONTEND_URL) {
    throw new Error('Login entry did not return the configured loopback CORS origin.');
  }
  if (payload?.error?.code !== 'UNAUTHORIZED' || !payload?.correlationId) {
    throw new Error('Login entry did not return the expected safe unauthorized contract.');
  }
  return { status: response.status, errorCode: payload.error.code, correlationPresent: true };
}

function verifyLoopbackPublishing() {
  const raw = command('docker', composeArguments('ps', '-a', '--format', 'json'), 30_000);
  const rows = raw.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
  const published = rows.flatMap(row => (row.Publishers || [])
    .filter(item => Number(item.PublishedPort) > 0)
    .map(item => ({
      service: row.Service,
      host: item.URL,
      publishedPort: item.PublishedPort,
      targetPort: item.TargetPort
    })));
  if (published.length !== 1
    || published[0].service !== 'gateway'
    || published[0].host !== '127.0.0.1'
    || Number(published[0].publishedPort) !== Number(environment.ZUMBO_GATEWAY_PORT)) {
    throw new Error(`Unexpected Compose published-port inventory: ${JSON.stringify(published)}.`);
  }
  return { compose: published, frontend: environment.ZUMBO_FRONTEND_URL };
}

async function request(url, {
  acceptedStatuses,
  timeoutMs,
  method = 'GET',
  headers,
  body
}) {
  const response = await fetch(url, {
    method,
    headers,
    body,
    redirect: 'manual',
    signal: AbortSignal.timeout(timeoutMs)
  });
  const responseBody = await response.text();
  if (!acceptedStatuses.includes(response.status)) {
    throw new Error(`${method} ${new URL(url).pathname} returned HTTP ${response.status}.`);
  }
  return { status: response.status, headers: response.headers, body: responseBody };
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

function pnpmExecutable() {
  return process.platform === 'win32' ? 'pnpm.cmd' : 'pnpm';
}

function assertPortAvailable(host, port) {
  return new Promise((accept, reject) => {
    const server = createServer();
    server.once('error', error => reject(new Error(`Frontend port ${host}:${port} is unavailable: ${error.code || error.message}.`)));
    server.listen(port, host, () => server.close(error => error ? reject(error) : accept()));
  });
}

function buildResult(passed, blocker = null) {
  return {
    schemaVersion: 1,
    task: 'DEMO-002',
    generatedAtUtc: new Date().toISOString(),
    projectName,
    passed,
    decision: passed ? 'ready' : 'blocked',
    frontendUrl: environment.ZUMBO_FRONTEND_URL,
    gatewayUrl: environment.ZUMBO_GATEWAY_URL,
    checks,
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

function delay(milliseconds) {
  return new Promise(resolvePromise => setTimeout(resolvePromise, milliseconds));
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

if (process.argv[1] && import.meta.url !== pathToFileURL(resolve(process.argv[1])).href) {
  throw new Error(`Unexpected invocation path from ${scriptDirectory}.`);
}
