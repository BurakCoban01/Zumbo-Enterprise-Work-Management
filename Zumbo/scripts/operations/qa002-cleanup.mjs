import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { assertProjectName, repositoryRoot, requireArgument } from './qa002-common.mjs';

const project = assertProjectName(requireArgument('--project'));
const environmentPath = resolve(requireArgument('--environment'));
const outputPath = resolve(requireArgument('--output'));
const started = Date.now();
const compose = ['compose', '--project-name', project, '--env-file', environmentPath,
  '-f', resolve(repositoryRoot, 'Backend/docker-compose.yml')];
const down = spawnSync('docker', [...compose, 'down', '--volumes', '--remove-orphans', '--timeout', '60'], {
  cwd: repositoryRoot,
  encoding: 'utf8',
  timeout: 5 * 60_000,
  maxBuffer: 4 * 1024 * 1024
});

const resources = {
  containers: projectResources(['ps', '--all', '--quiet', '--filter', `label=com.docker.compose.project=${project}`]),
  networks: projectResources(['network', 'ls', '--quiet', '--filter', `label=com.docker.compose.project=${project}`]),
  volumes: projectResources(['volume', 'ls', '--quiet', '--filter', `label=com.docker.compose.project=${project}`])
};
const passed = down.status === 0 && Object.values(resources).every(items => items.length === 0);
const result = {
  schemaVersion: 2,
  task: 'QA-002',
  generatedAtUtc: new Date().toISOString(),
  project,
  passed,
  targetedProjectOnly: true,
  volumeRemovalScope: 'disposable workflow project only',
  globalPrune: false,
  downExitCode: down.status ?? -1,
  remaining: Object.fromEntries(Object.entries(resources).map(([key, items]) => [key, items.length])),
  durationMs: Date.now() - started
};

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
console.log(`QA-002 targeted cleanup: ${passed ? 'passed' : 'failed'}; containers=${resources.containers.length}, networks=${resources.networks.length}, volumes=${resources.volumes.length}.`);
if (!passed) process.exitCode = 1;

function projectResources(args) {
  const result = spawnSync('docker', args, { cwd: repositoryRoot, encoding: 'utf8', timeout: 30_000 });
  if (result.status !== 0) return ['inventory-command-failed'];
  return result.stdout.trim() ? result.stdout.trim().split(/\r?\n/).filter(Boolean) : [];
}
