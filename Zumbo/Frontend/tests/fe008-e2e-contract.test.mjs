import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const root = resolve(import.meta.dirname, '..');

test('run context produces unique controlled desktop and mobile tenants', () => {
  const first = createRunContext('FE-008', 'Chromium', 1234, '11111111-1111-1111-1111-111111111111');
  const second = createRunContext('FE-008', 'Chromium', 1234, '22222222-2222-2222-2222-222222222222');
  assert.notEqual(first.runId, second.runId);
  assert.equal(first.tenants.desktop, `${first.runId}-desktop`);
  assert.equal(first.tenants.mobile, `${first.runId}-mobile`);
  assert.match(first.runId, /^[a-z0-9-]+$/);
});

test('cleanup ledger runs in reverse order and continues after a failure', async () => {
  const order = [];
  const ledger = createCleanupLedger();
  ledger.add('first', async () => order.push('first'));
  ledger.add('broken', async () => { order.push('broken'); throw new Error('cleanup failed'); });
  ledger.add('last', async () => order.push('last'));
  assert.equal(ledger.add('last', async () => {}), false);

  const result = await ledger.run();
  assert.deepEqual(order, ['last', 'broken', 'first']);
  assert.equal(result.attempted, 3);
  assert.equal(result.failed, 1);
  assert.strictEqual(await ledger.run(), result);
});

test('full UI suite rewrites fixture tenants and always invokes cleanup', async () => {
  const source = await readFile(resolve(root, 'tests/ui-quality.mjs'), 'utf8');
  assert.match(source, /createRunContext\('FE-008', browserName\)/);
  assert.match(source, /routeDemoRegistration\(page, runContext\.tenants\.desktop/);
  assert.match(source, /routeDemoRegistration\(mobilePage, runContext\.tenants\.mobile/);
  assert.match(source, /cleanupResult = await cleanupLedger\.run\(\)/);
  assert.match(source, /archiveTenant/);
});

test('full UI suite reports structured diagnostics and cleanup evidence', async () => {
  const source = await readFile(resolve(root, 'tests/ui-quality.mjs'), 'utf8');
  for (const field of ['runId', 'tenants', 'cleanup', 'diagnostics', 'consoleFailures', 'networkFailures']) {
    assert.match(source, new RegExp(`\\b${field}\\b`));
  }
  assert.match(source, /page\.on\('requestfailed'/);
});

test('cross-browser runner declares three engines and capability decisions', async () => {
  const source = await readFile(resolve(root, 'tests/fe008-cross-browser.mjs'), 'utf8');
  assert.match(source, /chromium/);
  assert.match(source, /firefox/);
  assert.match(source, /webkit/);
  assert.match(source, /capabilities/);
  assert.match(source, /desktop/);
  assert.match(source, /mobile/);
});

test('package scripts expose WebKit and the FE-008 matrix', async () => {
  const packageJson = JSON.parse(await readFile(resolve(root, 'package.json'), 'utf8'));
  assert.match(packageJson.scripts['test:e2e:webkit'], /webkit/);
  assert.match(packageJson.scripts['test:fe008:cross-browser'], /fe008-cross-browser/);
});
