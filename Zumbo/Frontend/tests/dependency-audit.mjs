import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(import.meta.dirname, '..');
const policy = JSON.parse(await readFile(resolve(root, 'dependency-policy.json'), 'utf8'));
const audit = runPnpmJson(['audit', '--prod', '--json']);
const advisories = Object.entries(audit.advisories || {}).map(([id, advisory]) => ({ ...advisory, id: String(id) }));
const today = new Date().toISOString().slice(0, 10);
const severityRank = { info: 0, low: 1, moderate: 2, high: 3, critical: 4 };
const failures = [];

for (const advisory of advisories) {
  const findingVersions = [...new Set((advisory.findings || []).map(finding => finding.version))];
  const exception = policy.exceptions.find(candidate =>
    candidate.package === advisory.module_name && candidate.advisoryIds.includes(advisory.id));
  if (!exception) {
    failures.push(`${advisory.id} ${advisory.module_name}: politika dışı ${advisory.severity} bulgusu`);
    continue;
  }
  if (exception.expiresOn < today) failures.push(`${advisory.id}: istisna ${exception.expiresOn} tarihinde sona erdi`);
  if (severityRank[advisory.severity] > severityRank[exception.maxSeverity]) {
    failures.push(`${advisory.id}: ${advisory.severity}, izin verilen ${exception.maxSeverity} düzeyini aşıyor`);
  }
  if (findingVersions.some(version => version !== exception.version)) {
    failures.push(`${advisory.id}: bulgu sürümü (${findingVersions.join(', ')}) istisna sürümüyle (${exception.version}) eşleşmiyor`);
  }
}

for (const exception of policy.exceptions) {
  const packageJson = JSON.parse(await readFile(resolve(root, 'node_modules', exception.package, 'package.json'), 'utf8'));
  if (packageJson.version !== exception.version) {
    failures.push(`${exception.package}: kurulu ${packageJson.version}, politika ${exception.version}`);
  }
  for (const advisoryId of exception.advisoryIds) {
    if (!advisories.some(advisory => advisory.id === advisoryId)) {
      failures.push(`${advisoryId}: artık denetimde bulunmayan istisna politikadan kaldırılmalı`);
    }
  }
}

if (failures.length > 0) throw new Error(`Bağımlılık denetimi başarısız:\n${failures.join('\n')}`);
const counts = audit.metadata?.vulnerabilities || {};
console.log(`Bağımlılık denetimi geçti: ${advisories.length} süreli istisna; kritik=${counts.critical || 0}, yüksek=${counts.high || 0}.`);

function runPnpmJson(args) {
  const executable = process.env.npm_execpath;
  const result = executable
    ? spawnSync(process.execPath, [executable, ...args], { cwd: root, encoding: 'utf8' })
    : spawnSync(process.platform === 'win32' ? 'pnpm.cmd' : 'pnpm', args, { cwd: root, encoding: 'utf8' });
  if (result.error) throw result.error;
  try {
    return JSON.parse(result.stdout);
  } catch {
    throw new Error(`pnpm denetim çıktısı JSON olarak çözümlenemedi: ${result.stderr || result.stdout}`);
  }
}
