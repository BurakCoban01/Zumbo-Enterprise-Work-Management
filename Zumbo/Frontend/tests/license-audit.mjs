import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(import.meta.dirname, '..');
const policy = JSON.parse(await readFile(resolve(root, 'license-policy.json'), 'utf8'));
const licenses = runPnpmJson(['licenses', 'list', '--prod', '--json']);
const discovered = Object.keys(licenses).sort();
const rejected = discovered.filter(license => !policy.allowed.includes(license));
if (rejected.length > 0) throw new Error(`İzin verilmeyen veya bilinmeyen lisanslar: ${rejected.join(', ')}`);

const packages = Object.values(licenses).flat();
if (packages.length === 0) throw new Error('Üretim bağımlılıkları için lisans kaydı bulunamadı.');
console.log(`Lisans denetimi geçti: ${packages.length} paket, ${discovered.join(', ')}.`);

function runPnpmJson(args) {
  const executable = process.env.npm_execpath;
  const result = executable
    ? spawnSync(process.execPath, [executable, ...args], { cwd: root, encoding: 'utf8' })
    : spawnSync(process.platform === 'win32' ? 'pnpm.cmd' : 'pnpm', args, { cwd: root, encoding: 'utf8' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(result.stderr || result.stdout);
  return JSON.parse(result.stdout);
}
