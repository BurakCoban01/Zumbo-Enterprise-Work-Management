import { randomBytes } from 'node:crypto';
import { chmodSync, existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
export const repositoryRoot = resolve(scriptDirectory, '../..');
export const environmentTemplatePath = resolve(repositoryRoot, 'Backend/.env.example');
export const defaultEnvironmentPath = resolve(repositoryRoot, 'Backend/.env');

const generatedValues = Object.freeze({
  ZUMBO_GRAFANA_ADMIN_PASSWORD: () => secret(32),
  ZUMBO_TLS_PFX_PASSWORD: () => secret(32),
  ZUMBO_MONGO_REPLICA_KEY: () => randomBytes(48).toString('base64'),
  ZUMBO_MONGO_ROOT_PASSWORD: () => secret(32),
  ZUMBO_REDIS_PASSWORD: () => secret(32),
  ZUMBO_OPENSEARCH_ADMIN_PASSWORD: () => secret(32),
  ZUMBO_POSTGRES_PASSWORD: () => secret(32),
  ZUMBO_POSTGRES_TEST_PASSWORD: () => secret(32),
  ZUMBO_JWT_SIGNING_KEY: () => secret(64),
  ZUMBO_MINIO_ROOT_USER: () => `zumbo-local-${randomBytes(6).toString('hex')}`,
  ZUMBO_MINIO_ROOT_PASSWORD: () => secret(32),
  ZUMBO_IDENTITY_BOOTSTRAP_TOKEN: () => secret(32)
});

const runtimeSecretRules = Object.freeze({
  ZUMBO_JWT_SIGNING_KEY: 64,
  ZUMBO_MINIO_ROOT_USER: 3,
  ZUMBO_MINIO_ROOT_PASSWORD: 16,
  ZUMBO_IDENTITY_BOOTSTRAP_TOKEN: 24
});

export function createLocalEnvironment(outputPath = defaultEnvironmentPath) {
  const absoluteOutput = resolve(outputPath);
  if (existsSync(absoluteOutput)) {
    throw new Error(`Environment file already exists and will not be overwritten: ${absoluteOutput}`);
  }

  let content = readFileSync(environmentTemplatePath, 'utf8');
  for (const [name, factory] of Object.entries(generatedValues)) {
    const pattern = new RegExp(`^${name}=.*$`, 'm');
    if (!pattern.test(content)) throw new Error(`Environment template is missing ${name}.`);
    content = content.replace(pattern, `${name}=${factory()}`);
  }
  content = replaceValue(content, 'ZUMBO_DOCKER_SUBNET', selectAvailableDockerSubnet());

  writeFileSync(absoluteOutput, content, { encoding: 'utf8', flag: 'wx', mode: 0o600 });
  if (process.platform !== 'win32') chmodSync(absoluteOutput, 0o600);
  const summary = validateLocalEnvironment(absoluteOutput);
  return { output: absoluteOutput, ...summary };
}

export function validateLocalEnvironment(path = defaultEnvironmentPath) {
  const absolutePath = resolve(path);
  if (!existsSync(absolutePath)) throw new Error(`Environment file does not exist: ${absolutePath}`);
  const { values, duplicates } = parseEnvironment(readFileSync(absolutePath, 'utf8'));
  if (duplicates.length > 0) throw new Error(`Duplicate environment keys: ${duplicates.join(', ')}`);

  for (const [name, minimumLength] of Object.entries(runtimeSecretRules)) {
    const value = values[name] || '';
    if (value.length < minimumLength || /replace-with|example|<[^>]+>/i.test(value)) {
      throw new Error(`${name} is missing, too short, or still a placeholder.`);
    }
  }

  for (const name of ['ZUMBO_API_URL', 'ZUMBO_GATEWAY_URL', 'ZUMBO_FRONTEND_URL']) {
    const url = new URL(values[name]);
    if (!['127.0.0.1', 'localhost'].includes(url.hostname)) {
      throw new Error(`${name} must remain loopback for the local runbook.`);
    }
  }
  for (const name of ['ZUMBO_API_BIND_HOST', 'ZUMBO_GATEWAY_BIND_HOST', 'ZUMBO_FRONTEND_BIND_HOST']) {
    if (values[name] !== '127.0.0.1') throw new Error(`${name} must be 127.0.0.1.`);
  }

  return {
    keys: Object.keys(values).length,
    generatedSecretKeys: Object.keys(generatedValues).length,
    loopbackOnly: true
  };
}

function replaceValue(content, name, value) {
  const pattern = new RegExp(`^${name}=.*$`, 'm');
  if (!pattern.test(content)) throw new Error(`Environment template is missing ${name}.`);
  return content.replace(pattern, `${name}=${value}`);
}

function selectAvailableDockerSubnet() {
  const networks = spawnSync('docker', ['network', 'ls', '--format', '{{.ID}}'], { encoding: 'utf8' });
  if (networks.status !== 0) throw new Error('Docker daemon is required to select a non-conflicting local subnet.');
  const occupied = networks.stdout.split(/\r?\n/).filter(Boolean).flatMap(id => {
    const inspection = spawnSync('docker', ['network', 'inspect', id, '--format', '{{range .IPAM.Config}}{{.Subnet}} {{end}}'], { encoding: 'utf8' });
    return inspection.status === 0 ? inspection.stdout.trim().split(/\s+/).filter(Boolean) : [];
  }).map(cidrRange).filter(Boolean);

  for (let octet = 10; octet <= 99; octet += 1) {
    const candidate = `10.250.${octet}.0/24`;
    const range = cidrRange(candidate);
    if (!occupied.some(existing => existing.start <= range.end && range.start <= existing.end)) return candidate;
  }
  throw new Error('No free local Docker subnet was found in 10.250.10.0/24-10.250.99.0/24.');
}

function cidrRange(cidr) {
  const match = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\/(\d|[12]\d|3[0-2])$/.exec(cidr);
  if (!match) return undefined;
  const octets = match.slice(1, 5).map(Number);
  if (octets.some(value => value > 255)) return undefined;
  const prefix = Number(match[5]);
  const address = (((octets[0] * 256 + octets[1]) * 256 + octets[2]) * 256 + octets[3]) >>> 0;
  const size = 2 ** (32 - prefix);
  const start = Math.floor(address / size) * size;
  return { start, end: start + size - 1 };
}

export function parseEnvironment(content) {
  const values = {};
  const duplicates = [];
  for (const rawLine of content.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#') || !line.includes('=')) continue;
    const separator = line.indexOf('=');
    const name = line.slice(0, separator).trim();
    if (Object.hasOwn(values, name)) duplicates.push(name);
    values[name] = line.slice(separator + 1).trim();
  }
  return { values, duplicates };
}

function secret(bytes) {
  return randomBytes(bytes).toString('base64url');
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const checkPath = argumentValue('--check');
    const result = checkPath
      ? { output: resolve(checkPath), ...validateLocalEnvironment(checkPath) }
      : createLocalEnvironment(argumentValue('--output') || defaultEnvironmentPath);
    console.log(JSON.stringify({ passed: true, ...result }, null, 2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
