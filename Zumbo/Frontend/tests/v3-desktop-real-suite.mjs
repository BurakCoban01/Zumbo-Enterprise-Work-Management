import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, readdir, rm, writeFile } from 'node:fs/promises';
import { freemem, tmpdir } from 'node:os';
import { basename, relative, resolve } from 'node:path';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { realBrowserTestTimeoutMs } from './v3-desktop-real-suite-config.mjs';

const frontendRoot = resolve(import.meta.dirname, '..');
const appRoot = resolve(frontendRoot, '..');
const apiDirectory = resolve(appRoot, 'Backend/src/Zumbo.Api');
const apiDll = resolve(apiDirectory, 'bin/Release/net8.0/Zumbo.Api.dll');
const outputDirectory = resolve(appRoot, 'artifacts/ui/v3-qa-001');
const storageBase = resolve(tmpdir(), 'zumbo-v3-qa-001');
const match = process.argv.find(argument => argument.startsWith('--match='))?.slice('--match='.length);
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for the V3-QA-001 isolated real-browser suite');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for the V3-QA-001 isolated real-browser suite');
const signingKey = requireLocalSecret('ZUMBO_JWT_SIGNING_KEY', 'for the V3-QA-001 isolated API processes');
const apiOrigin = new URL(apiBaseUrl).origin;
const frontendOrigin = new URL(frontendBaseUrl).origin;

await mkdir(outputDirectory, { recursive: true });
await mkdir(storageBase, { recursive: true });
await assertEndpoint(`${frontendBaseUrl}/desktop-bulma/index.html`, 'frontend');
await assertPortAvailable();

const testFiles = (await readdir(import.meta.dirname))
  .filter(name => /^v3-(?!mobile-).+-real-browser\.mjs$/.test(name))
  .filter(name => !match || name.includes(match))
  .sort();
assert.ok(testFiles.length, `No desktop real-browser tests matched ${match || 'the suite filter'}.`);

const minimumFreeMemoryMiB = Number(process.env.ZUMBO_QA_MIN_FREE_MEMORY_MIB || 1024);
const observedFreeMemoryMiB = Math.floor(freemem() / 1024 / 1024);
if (!match && observedFreeMemoryMiB < minimumFreeMemoryMiB) {
  const reason = `V3-QA-001 requires at least ${minimumFreeMemoryMiB} MiB free physical memory; ${observedFreeMemoryMiB} MiB is available.`;
  await writeFile(
    resolve(outputDirectory, 'real-suite.json'),
    `${JSON.stringify({
      schemaVersion: 1,
      taskId: 'V3-QA-001',
      generatedAtUtc: new Date().toISOString(),
      status: 'Blocked',
      reason,
      minimumFreeMemoryMiB,
      observedFreeMemoryMiB,
      total: 0,
      passed: 0,
      failed: 0,
      results: []
    }, null, 2)}\n`,
    'utf8'
  );
  throw new Error(reason);
}

const results = [];
for (const testFile of testFiles) {
  const startedAt = Date.now();
  const timeoutMs = realBrowserTestTimeoutMs(testFile);
  const storageRoot = resolve(storageBase, `${basename(testFile, '.mjs')}-${startedAt}`);
  assertScopedStorage(storageRoot);
  const apiLog = [];
  const apiProcess = spawn('dotnet', [apiDll], {
    cwd: apiDirectory,
    env: apiEnvironment(storageRoot),
    windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe']
  });
  captureTail(apiProcess.stdout, apiLog);
  captureTail(apiProcess.stderr, apiLog);

  let failure = null;
  let exitCode = null;
  let signal = null;
  let timedOut = false;
  try {
    await waitForApi(apiProcess, apiLog);
    process.stdout.write(`RUN ${testFile}\n`);
    const execution = await runTest(testFile, timeoutMs);
    exitCode = execution.exitCode;
    signal = execution.signal;
    timedOut = execution.timedOut;
    if (timedOut) {
      failure = `Browser test exceeded its ${timeoutMs}ms timeout.`;
      process.stderr.write(`${apiLog.slice(-30).join('\n')}\n`);
    } else if (exitCode !== 0) {
      failure = `Browser test exited with code ${exitCode}.`;
      process.stderr.write(`${apiLog.slice(-30).join('\n')}\n`);
    }
  } catch (error) {
    failure = error instanceof Error ? error.message : String(error);
  } finally {
    await stopProcess(apiProcess);
    await rm(storageRoot, { recursive: true, force: true });
  }

  const result = {
    testFile,
    passed: !failure,
    exitCode,
    signal,
    timeoutMs,
    timedOut,
    durationMs: Date.now() - startedAt,
    failure,
    apiLogTail: failure ? apiLog.slice(-30) : []
  };
  results.push(result);
  process.stdout.write(`${result.passed ? 'PASS' : 'FAIL'} ${testFile} ${result.durationMs}ms\n`);
}

const manifest = {
  schemaVersion: 1,
  taskId: 'V3-QA-001',
  generatedAtUtc: new Date().toISOString(),
  status: results.some(result => !result.passed) ? 'Failed' : 'Passed',
  backend: 'real-dotnet-api',
  persistenceProvider: 'InMemory',
  isolatedApiProcessPerTest: true,
  apiOrigin,
  frontendOrigin,
  total: results.length,
  passed: results.filter(result => result.passed).length,
  failed: results.filter(result => !result.passed).length,
  results
};
await writeFile(
  resolve(outputDirectory, 'real-suite.json'),
  `${JSON.stringify(manifest, null, 2)}\n`,
  'utf8'
);

assert.equal(
  manifest.failed,
  0,
  results.filter(result => !result.passed).map(result => `${result.testFile}: ${result.failure}`).join('\n')
);
console.log(`V3-QA-001 isolated real-browser suite passed: ${manifest.passed}/${manifest.total} desktop scenarios.`);

function apiEnvironment(storageRoot) {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: apiOrigin,
    Persistence__Provider: 'InMemory',
    Search__Provider: 'InMemory',
    RateLimiting__Provider: 'InMemory',
    RateLimiting__ApiPermitLimit: '1000',
    RateLimiting__SearchPermitLimit: '1000',
    RateLimiting__ReportPermitLimit: '1000',
    RateLimiting__BulkPermitLimit: '1000',
    DistributedLock__Provider: 'InMemory',
    ReadModelCache__Provider: 'InMemory',
    Storage__Provider: 'Local',
    Storage__Local__RootPath: storageRoot,
    WorkItemRecurrence__IntervalSeconds: '5',
    IdentityBootstrap__AdminEmails__0: adminEmail,
    IdentityBootstrap__BootstrapToken: bootstrapToken,
    Jwt__SigningKey: signingKey,
    RegistrationProvisioning__Mode: 'LocalDemo',
    Cors__AllowedOrigins__0: frontendOrigin,
    Webhooks__AllowHttpLoopback: 'true',
    Webhooks__MaximumAttempts: '2',
    Webhooks__BaseRetrySeconds: '1',
    Webhooks__MaximumRetrySeconds: '1',
    Webhooks__RetryJitterRatio: '0',
    Webhooks__DispatcherIntervalSeconds: '1',
    DevelopmentProviders__AllowHttpLoopback: 'true',
    DevelopmentProviders__AllowedHosts__0: '127.0.0.1',
    DevelopmentProviders__AllowedHosts__1: 'api.github.com',
    DevelopmentProviders__AllowedHosts__2: 'gitlab.com',
    Audit__HashChainEnabled: 'true',
    Audit__IntegrityKey: signingKey
  };
}

async function assertEndpoint(url, label) {
  const response = await fetch(url);
  assert.ok(response.ok, `${label} endpoint returned HTTP ${response.status}.`);
}

async function assertPortAvailable() {
  try {
    const response = await fetch(`${apiBaseUrl}/health/live`, { signal: globalThis.AbortSignal.timeout(1_000) });
    if (response.ok) throw new Error(`API port ${apiOrigin} is already in use.`);
  } catch (error) {
    if (error instanceof Error && error.message.includes('already in use')) throw error;
  }
}

async function waitForApi(processHandle, log) {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    if (processHandle.exitCode !== null) {
      throw new Error(`API exited before readiness with code ${processHandle.exitCode}.\n${log.slice(-20).join('\n')}`);
    }
    try {
      const response = await fetch(`${apiBaseUrl}/health/ready`, { signal: globalThis.AbortSignal.timeout(1_000) });
      if (response.ok) return;
    } catch (_) {
      // Startup connection failures are expected until Kestrel begins listening.
    }
    await delay(500);
  }
  throw new Error(`API did not become ready within 30 seconds.\n${log.slice(-20).join('\n')}`);
}

async function runTest(testFile, timeoutMs) {
  const child = spawn(process.execPath, [resolve(import.meta.dirname, testFile)], {
    cwd: frontendRoot,
    env: process.env,
    windowsHide: true,
    stdio: 'inherit'
  });
  let timedOut = false;
  const timeout = globalThis.setTimeout(() => {
    timedOut = true;
    child.kill();
  }, timeoutMs);
  const [exitCode, signal] = await once(child, 'exit');
  globalThis.clearTimeout(timeout);
  return { exitCode, signal, timedOut };
}

async function stopProcess(processHandle) {
  if (processHandle.exitCode !== null || processHandle.signalCode !== null) return;
  processHandle.kill();
  await Promise.race([once(processHandle, 'exit'), delay(5_000)]);
  if (processHandle.exitCode === null && processHandle.signalCode === null) {
    processHandle.kill('SIGKILL');
    await once(processHandle, 'exit');
  }
}

function captureTail(stream, lines) {
  let remainder = '';
  stream.setEncoding('utf8');
  stream.on('data', chunk => {
    const parts = `${remainder}${chunk}`.split(/\r?\n/);
    remainder = parts.pop() || '';
    lines.push(...parts);
    if (lines.length > 100) lines.splice(0, lines.length - 100);
  });
}

function assertScopedStorage(path) {
  const scoped = relative(storageBase, path);
  assert.ok(scoped && !scoped.startsWith('..') && !scoped.includes(':') && resolve(storageBase, scoped) === path);
}

function delay(milliseconds) {
  return new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));
}
