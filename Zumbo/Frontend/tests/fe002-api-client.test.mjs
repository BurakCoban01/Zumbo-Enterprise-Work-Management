import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import vm from 'node:vm';
import { webcrypto } from 'node:crypto';

const root = resolve(import.meta.dirname, '..');
const source = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');
const browser = {
  crypto: webcrypto,
  location: { origin: 'https://app.zumbo.test' }
};
vm.runInNewContext(source, { window: browser, Uint8Array });
const core = browser.ZumboApiClientCore;

test('runtime config tek ve normalize edilmis API base URL uretir', () => {
  assert.equal(core.resolveBaseUrl({ apiBaseUrl: ' https://api.zumbo.test/// ' }, 'https://fallback.test'), 'https://api.zumbo.test');
  assert.equal(core.resolveBaseUrl({}, 'https://app.zumbo.test/'), 'https://app.zumbo.test');
});

test('API istemcisi standart zarfi acar, dogrudan operasyon govdesini korur', () => {
  const data = { dependencies: [{ dependency: 'mongodb' }] };
  assert.deepEqual(
    core.unwrapResponseBody({ success: true, data, error: null, correlationId: 'corr-1' }),
    data
  );
  assert.deepEqual(core.unwrapResponseBody(data), data);
  assert.deepEqual(core.unwrapResponseBody({ data: 'domain-value', status: 'available' }), {
    data: 'domain-value',
    status: 'available'
  });
});

test('refresh sonrasi replay yalniz safe method veya explicit idempotency key icin acilir', () => {
  assert.equal(core.canReplay('GET', null), true);
  assert.equal(core.canReplay('HEAD', null), true);
  assert.equal(core.canReplay('POST', null), false);
  assert.equal(core.canReplay('PATCH', ''), false);
  assert.equal(core.canReplay('DELETE', 'idem-123'), true);
  assert.equal(core.validateIdempotencyKey(' idem-123 '), 'idem-123');
  assert.throws(() => core.validateIdempotencyKey('x'.repeat(129)), /128/);
  assert.throws(() => core.validateIdempotencyKey('bad\nkey'), /128/);
});

test('single-flight gate eszamanli refresh cagrisini tek promise uzerinde birlestirir', async () => {
  const gate = core.createSingleFlight();
  let starts = 0;
  let release;
  const first = gate.run(() => {
    starts += 1;
    return new Promise(resolvePromise => { release = resolvePromise; });
  });
  const second = gate.run(() => {
    starts += 1;
    return Promise.resolve('unexpected');
  });
  assert.equal(first, second);
  assert.equal(starts, 1);
  release('ok');
  assert.deepEqual(await Promise.all([first, second]), ['ok', 'ok']);
  await Promise.resolve();
  assert.equal(await gate.run(() => { starts += 1; return Promise.resolve('next'); }), 'next');
  assert.equal(starts, 2);
});

test('request registry scope cancellation ve context generation ile stale commit engeller', () => {
  const registry = core.createRequestRegistry();
  const canceled = [];
  const firstGeneration = registry.register('one', 'tasks', reason => canceled.push(['one', reason]));
  registry.register('two', 'reports', reason => canceled.push(['two', reason]));
  registry.cancelScope('tasks', 'replaced');
  assert.deepEqual(canceled, [['one', 'replaced']]);
  assert.equal(registry.isCurrent(firstGeneration), true);
  registry.transition('project:next');
  assert.deepEqual(canceled, [['one', 'replaced'], ['two', 'context-changed']]);
  assert.equal(registry.isCurrent(firstGeneration), false);
  assert.equal(registry.activeCount(), 0);
});

test('normalized hata yalniz guvenli sozlesmeyi ve correlation bilgisini tasir', () => {
  const normalized = core.normalizeError({
    status: 503,
    data: {
      error: { code: 'DEPENDENCY_UNAVAILABLE', message: 'Servis gecici olarak kullanilamiyor.' },
      correlationId: 'corr-42',
      refreshToken: 'must-not-leak'
    },
    config: { headers: { Authorization: 'Bearer must-not-leak' } }
  });
  assert.equal(normalized.status, 503);
  assert.equal(normalized.code, 'DEPENDENCY_UNAVAILABLE');
  assert.equal(normalized.correlationId, 'corr-42');
  assert.equal(normalized.retryable, true);
  assert.doesNotMatch(JSON.stringify(normalized), /must-not-leak|Authorization/);
});

test('logout tenant state temizler, cihaz gorunum tercihlerini korur', () => {
  const localValues = new Map(core.tenantLocalKeys.map(key => [key, 'tenant-value']));
  localValues.set('zumbo.theme', 'dark');
  const sessionValues = new Map(core.tenantSessionKeys.map(key => [key, 'csrf-value']));
  const storage = values => ({
    removeItem: key => values.delete(key)
  });
  core.clearTenantStorage(storage(localValues), storage(sessionValues));
  assert.equal(localValues.get('zumbo.theme'), 'dark');
  assert.equal(core.tenantLocalKeys.some(key => localValues.has(key)), false);
  assert.equal(core.tenantSessionKeys.some(key => sessionValues.has(key)), false);
});

test('desktop ve mobile ayni shared AngularJS istemci modulunu yukler', async () => {
  for (const surface of ['desktop-bulma', 'mobile-ionic']) {
    const [html, app] = await Promise.all([
      readFile(resolve(root, surface, 'index.html'), 'utf8'),
      readFile(resolve(root, surface, 'app.js'), 'utf8')
    ]);
    assert.match(html, /\.\.\/shared\/api-client\.js/);
    assert.match(app, /zumbo\.shared\.api/);
    assert.doesNotMatch(app, /factory\('apiClient'/);
    assert.doesNotMatch(app, /zumbo\.apiBaseUrl/);
  }
});
