import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const checkerPath = fileURLToPath(new URL('./openapi-compat.mjs', import.meta.url));
const tempDirectory = mkdtempSync(join(tmpdir(), 'zumbo-openapi-compat-'));

try {
  const baseline = createDocument({
    requestEnum: ['todo', 'done'],
    requestRequired: true,
    responseEnum: ['todo', 'done']
  });

  assertCase(
    'compatible request expansion and response narrowing',
    baseline,
    createDocument({
      requestEnum: ['todo', 'done', 'blocked'],
      requestRequired: false,
      responseEnum: ['todo']
    }),
    true);

  assertCase(
    'removed accepted request enum',
    baseline,
    createDocument({ requestEnum: ['todo'], requestRequired: true, responseEnum: ['todo', 'done'] }),
    false,
    "Removed accepted request enum value 'done'");

  assertCase(
    'added response enum',
    baseline,
    createDocument({
      requestEnum: ['todo', 'done'],
      requestRequired: true,
      responseEnum: ['todo', 'done', 'blocked']
    }),
    false,
    "Added response enum value 'blocked'");

  const requiredParameter = createDocument({
    requestEnum: ['todo', 'done'],
    requestRequired: true,
    responseEnum: ['todo', 'done']
  });
  requiredParameter.paths['/items'].get.parameters.push({
    name: 'tenant',
    in: 'query',
    required: true,
    schema: { type: 'string' }
  });
  assertCase('added required parameter', baseline, requiredParameter, false, "Added required query parameter 'tenant'");

  console.log('OpenAPI compatibility self-test passed: 4 directional contract cases.');
} finally {
  rmSync(tempDirectory, { recursive: true, force: true });
}

function assertCase(name, baseline, current, expectedSuccess, expectedMessage) {
  const safeName = name.replaceAll(/[^a-z0-9]+/gi, '-').toLowerCase();
  const baselinePath = join(tempDirectory, `${safeName}-baseline.json`);
  const currentPath = join(tempDirectory, `${safeName}-current.json`);
  writeFileSync(baselinePath, JSON.stringify(baseline));
  writeFileSync(currentPath, JSON.stringify(current));

  const result = spawnSync(process.execPath, [checkerPath, baselinePath, currentPath], { encoding: 'utf8' });
  assert.equal(result.status === 0, expectedSuccess, `${name}: ${result.stderr || result.stdout}`);
  if (expectedMessage) assert.match(`${result.stderr}\n${result.stdout}`, new RegExp(escapeRegExp(expectedMessage)));
}

function createDocument({ requestEnum, requestRequired, responseEnum }) {
  return {
    openapi: '3.0.1',
    paths: {
      '/items': {
        get: {
          parameters: [{
            name: 'state',
            in: 'query',
            required: requestRequired,
            schema: { $ref: '#/components/schemas/RequestState' }
          }],
          responses: {
            200: {
              description: 'OK',
              content: {
                'application/json': {
                  schema: { $ref: '#/components/schemas/ResponseState' }
                }
              }
            }
          }
        }
      }
    },
    components: {
      schemas: {
        RequestState: { type: 'string', enum: requestEnum },
        ResponseState: { type: 'string', enum: responseEnum }
      }
    }
  };
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
