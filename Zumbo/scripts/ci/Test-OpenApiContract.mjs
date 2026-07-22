import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '../..');
const assembly = resolve(root, 'Backend/src/Zumbo.Api/bin/Release/net8.0/Zumbo.Api.dll');
const baseline = resolve(root, 'contracts/openapi.v1.json');
const output = resolve(root, 'artifacts/contracts/generated/openapi.current.json');
const port = 58155;
const origin = `http://127.0.0.1:${port}`;

await mkdir(resolve(root, 'artifacts/contracts/generated'), { recursive: true });
const api = spawn('dotnet', [assembly, '--urls', origin], {
  cwd: dirname(assembly),
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    Persistence__Provider: 'InMemory',
    RateLimiting__Provider: 'InMemory',
    Storage__Provider: 'Local',
    Storage__Local__RootPath: resolve(root, 'artifacts/contracts/generated/storage'),
    Search__Provider: 'InMemory',
    DistributedLock__Provider: 'InMemory',
    Realtime__Backplane: 'InMemory',
    ReadModelCache__Provider: 'InMemory',
    BackgroundJobs__Enabled: 'false'
  },
  stdio: ['ignore', 'pipe', 'pipe']
});

let logs = '';
api.stdout.on('data', chunk => { logs += chunk.toString(); });
api.stderr.on('data', chunk => { logs += chunk.toString(); });

try {
  const response = await waitFor(`${origin}/swagger/v1/swagger.json`, 45_000);
  const document = await response.json();
  await writeFile(output, `${JSON.stringify(document, null, 2)}\n`, 'utf8');
  await readFile(baseline, 'utf8');
  await runCompatibilityCheck(baseline, output);
} finally {
  api.kill('SIGTERM');
  await Promise.race([
    new Promise(resolveExit => api.once('exit', resolveExit)),
    new Promise(resolveTimeout => setTimeout(resolveTimeout, 5_000))
  ]);
  if (api.exitCode === null) api.kill('SIGKILL');
}

async function waitFor(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (api.exitCode !== null) throw new Error(`API exited before OpenAPI generation.\n${logs}`);
    try {
      const response = await fetch(url);
      if (response.ok) return response;
    } catch { }
    await new Promise(resolveDelay => setTimeout(resolveDelay, 250));
  }
  throw new Error(`Timed out waiting for ${url}.\n${logs}`);
}

async function runCompatibilityCheck(baselinePath, currentPath) {
  const child = spawn(process.execPath, [resolve(import.meta.dirname, 'openapi-compat.mjs'), baselinePath, currentPath], {
    cwd: root,
    stdio: 'inherit'
  });
  const exitCode = await new Promise(resolveExit => child.once('exit', resolveExit));
  if (exitCode !== 0) throw new Error(`OpenAPI compatibility check failed with exit code ${exitCode}.`);
}
