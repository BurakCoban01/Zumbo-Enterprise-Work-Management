import { existsSync, readdirSync, realpathSync } from 'node:fs';
import { dirname, isAbsolute, relative, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const applicationRoot = realpathSync(resolve(dirname(fileURLToPath(import.meta.url)), '..'));
export const gitRepositoryRoot = discoverGitRoot(applicationRoot);
export const applicationWorkingDirectory = relative(gitRepositoryRoot, applicationRoot).replaceAll('\\', '/');
export const rootWorkflowDirectory = resolve(gitRepositoryRoot, '.github/workflows');

if (applicationWorkingDirectory !== 'Zumbo') {
  throw new Error(`Expected application root to be the Zumbo directory directly below Git root; observed '${applicationWorkingDirectory}'.`);
}

export function rootWorkflowPath(fileName) {
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]*\.ya?ml$/.test(fileName || '')) {
    throw new Error(`Invalid root workflow file name: ${fileName}`);
  }
  return resolve(rootWorkflowDirectory, fileName);
}

export function assertRootWorkflowLayout(requiredFiles = ['ci.yml', 'qa-002-clean-linux.yml']) {
  for (const fileName of requiredFiles) {
    if (!existsSync(rootWorkflowPath(fileName))) throw new Error(`Git-root workflow is missing: .github/workflows/${fileName}`);
  }
  const nestedDirectory = resolve(applicationRoot, '.github/workflows');
  const nestedWorkflows = existsSync(nestedDirectory) ? findWorkflowFiles(nestedDirectory) : [];
  if (nestedWorkflows.length) {
    throw new Error(`Nested application workflows are forbidden: ${nestedWorkflows.join(', ')}`);
  }
}

function discoverGitRoot(cwd) {
  const result = spawnSync('git', ['rev-parse', '--show-toplevel'], {
    cwd,
    encoding: 'utf8',
    timeout: 30_000
  });
  if (result.status !== 0 || !result.stdout.trim()) throw new Error('Unable to resolve the actual Git repository root.');
  const root = realpathSync(resolve(result.stdout.trim()));
  const applicationRelative = relative(root, cwd);
  if (!applicationRelative || applicationRelative.startsWith('..') || isAbsolute(applicationRelative)) {
    throw new Error('Application root is not safely contained by the actual Git repository root.');
  }
  return root;
}

function findWorkflowFiles(directory) {
  const files = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const absolute = resolve(directory, entry.name);
    if (entry.isDirectory()) files.push(...findWorkflowFiles(absolute));
    else if (/\.ya?ml$/i.test(entry.name)) files.push(relative(applicationRoot, absolute).replaceAll('\\', '/'));
  }
  return files;
}
