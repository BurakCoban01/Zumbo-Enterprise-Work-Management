import { readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';

const files = readdirSync(new URL('.', import.meta.url))
  .filter((name) => /^modern-.*\.test\.mjs$/u.test(name))
  .sort()
  .map((name) => `tests/${name}`);

if (files.length === 0) {
  throw new Error('No modern frontend test files were found.');
}

const result = spawnSync(
  process.execPath,
  ['--test', '--test-concurrency=1', ...files],
  { cwd: new URL('..', import.meta.url), stdio: 'inherit' },
);

if (result.error) {
  throw result.error;
}

process.exitCode = result.status ?? 1;
