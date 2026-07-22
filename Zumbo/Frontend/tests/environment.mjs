import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const backendDirectory = resolve(import.meta.dirname, '../../Backend');
const contractValues = parseEnvFile(resolve(backendDirectory, '.env.example'));
const localValues = parseEnvFile(resolve(backendDirectory, '.env'));

export const frontendBaseUrl = requireContractUrl('ZUMBO_FRONTEND_URL');
export const apiBaseUrl = requireContractUrl('ZUMBO_API_URL');

export function requireLocalSecret(name, purpose) {
  const value = process.env[name] || localValues[name];
  if (!value) {
    throw new Error(`${name} is required ${purpose}. Set it in the process environment or Backend/.env.`);
  }
  return value;
}

function requireContractUrl(name) {
  const value = process.env[name] || localValues[name] || contractValues[name];
  if (!value) throw new Error(`${name} is missing from the environment contract.`);
  const url = new URL(value);
  if (!['http:', 'https:'].includes(url.protocol)) {
    throw new Error(`${name} must be an absolute HTTP or HTTPS URL.`);
  }
  return url.toString().replace(/\/$/, '');
}

function parseEnvFile(path) {
  if (!existsSync(path)) return {};
  return Object.fromEntries(readFileSync(path, 'utf8')
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(line => line && !line.startsWith('#') && line.includes('='))
    .map(line => {
      const separator = line.indexOf('=');
      return [line.slice(0, separator).trim(), line.slice(separator + 1).trim()];
    }));
}
