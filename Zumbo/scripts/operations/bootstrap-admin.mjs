import { resolve } from 'node:path';
import { readFileSync } from 'node:fs';
import { parseEnvironment, validateLocalEnvironment } from './prepare-env.mjs';

const environmentPath = resolve(argumentValue('--environment') || 'Backend/.env');
validateLocalEnvironment(environmentPath);
const environment = parseEnvironment(readFileSync(environmentPath, 'utf8')).values;
const apiUrl = new URL(environment.ZUMBO_API_URL);
const email = environment.ZUMBO_IDENTITY_ADMIN_EMAIL;
const username = argumentValue('--username') || 'localadmin';
const organizationId = argumentValue('--organization') || 'local-dev';

if (process.argv.includes('--check')) {
  console.log(JSON.stringify({ passed: true, apiOrigin: apiUrl.origin, emailConfigured: Boolean(email) }));
  process.exit(0);
}

const password = process.env.ZUMBO_BOOTSTRAP_ADMIN_PASSWORD || await readHidden('Initial local administrator password: ');
if (password.length < 12 || !/[a-z]/.test(password) || !/[A-Z]/.test(password) || !/\d/.test(password) || !/[^a-zA-Z0-9]/.test(password)) {
  throw new Error('Password must be at least 12 characters and include lower, upper, digit, and symbol classes.');
}

const response = await fetch(new URL('/api/auth/register', apiUrl), {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    username,
    email,
    password,
    organizationId,
    bootstrapToken: environment.ZUMBO_IDENTITY_BOOTSTRAP_TOKEN
  })
});
const payload = await response.json().catch(() => ({}));
if (!response.ok) throw new Error(payload.error?.message || `Administrator bootstrap failed with HTTP ${response.status}.`);
console.log(JSON.stringify({
  passed: true,
  userId: payload.data?.user?.id,
  organizationId: payload.data?.user?.organizationId,
  roles: payload.data?.user?.roles || []
}, null, 2));

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function readHidden(prompt) {
  if (!process.stdin.isTTY) throw new Error('Interactive terminal required, or set ZUMBO_BOOTSTRAP_ADMIN_PASSWORD in the process environment.');
  return new Promise((accept, reject) => {
    const input = process.stdin;
    let value = '';
    process.stdout.write(prompt);
    input.setRawMode(true);
    input.resume();
    input.setEncoding('utf8');
    const cleanup = () => {
      input.off('data', onData);
      input.setRawMode(false);
      input.pause();
    };
    const onData = character => {
      if (character === '\u0003') {
        cleanup();
        reject(new Error('Administrator bootstrap cancelled.'));
      } else if (character === '\r' || character === '\n') {
        cleanup();
        process.stdout.write('\n');
        accept(value);
      } else if (character === '\u007f' || character === '\b') {
        value = value.slice(0, -1);
      } else if (character >= ' ') {
        value += character;
      }
    };
    input.on('data', onData);
  });
}
