import { createHash } from 'node:crypto';
import { readdir, readFile, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(import.meta.dirname, '..');
const components = new Map();

for (const assetPath of await findProjectAssets(resolve(root, 'Backend', 'src'))) {
  const assets = JSON.parse(await readFile(assetPath, 'utf8'));
  for (const [coordinate, metadata] of Object.entries(assets.libraries || {})) {
    if (metadata.type !== 'package') continue;
    const split = coordinate.lastIndexOf('/');
    addComponent('nuget', coordinate.slice(0, split), coordinate.slice(split + 1));
  }
}

const frontendTree = runPnpm(['--dir', resolve(root, 'Frontend'), 'list', '--prod', '--json', '--depth', 'Infinity']);
for (const project of frontendTree) collectNpmDependencies(project.dependencies || {});

const sorted = [...components.values()].sort((left, right) => left.purl.localeCompare(right.purl));
const digest = createHash('sha256').update(JSON.stringify(sorted)).digest('hex');
const serial = `${digest.slice(0, 8)}-${digest.slice(8, 12)}-4${digest.slice(13, 16)}-a${digest.slice(17, 20)}-${digest.slice(20, 32)}`;
const bom = {
  bomFormat: 'CycloneDX',
  specVersion: '1.5',
  serialNumber: `urn:uuid:${serial}`,
  version: 1,
  metadata: {
    component: {
      type: 'application',
      'bom-ref': 'pkg:generic/zumbo@1.0.0',
      name: 'Zumbo',
      version: '1.0.0',
      purl: 'pkg:generic/zumbo@1.0.0'
    }
  },
  components: sorted
};

await writeFile(
  resolve(root, 'artifacts', 'security', 'SEC-008.sbom.cdx.json'),
  `${JSON.stringify(bom, null, 2)}\n`,
  'utf8'
);
console.log(`CycloneDX SBOM üretildi: ${sorted.length} benzersiz üretim bileşeni.`);

function addComponent(ecosystem, name, version) {
  if (!name || !version) return;
  const purlName = ecosystem === 'npm' && name.startsWith('@')
    ? `%40${name.slice(1).split('/').map(encodeURIComponent).join('/')}`
    : encodeURIComponent(name);
  const purl = `pkg:${ecosystem}/${purlName}@${encodeURIComponent(version)}`;
  components.set(purl, {
    type: 'library',
    'bom-ref': purl,
    name,
    version,
    purl
  });
}

function collectNpmDependencies(dependencies) {
  for (const [name, dependency] of Object.entries(dependencies)) {
    addComponent('npm', name, dependency.version);
    collectNpmDependencies(dependency.dependencies || {});
  }
}

async function findProjectAssets(directory) {
  const found = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (!entry.isDirectory() || entry.name === 'bin') continue;
    const child = join(directory, entry.name);
    if (entry.name === 'obj') {
      const candidate = join(child, 'project.assets.json');
      try {
        await readFile(candidate, 'utf8');
        found.push(candidate);
      } catch (error) {
        if (error.code !== 'ENOENT') throw error;
      }
      continue;
    }
    found.push(...await findProjectAssets(child));
  }
  return found;
}

function runPnpm(args) {
  const executable = process.platform === 'win32' ? 'pnpm.cmd' : 'pnpm';
  const result = spawnSync(executable, args, { cwd: root, encoding: 'utf8' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(result.stderr || result.stdout);
  return JSON.parse(result.stdout);
}
