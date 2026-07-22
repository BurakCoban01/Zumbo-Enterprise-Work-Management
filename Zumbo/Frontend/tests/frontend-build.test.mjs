import assert from 'node:assert/strict';
import { cp, mkdtemp, readFile, rm, unlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import test from 'node:test';
import {
  buildFrontend,
  createSecurityHeaders,
  findRuntimeCdnReferences,
  pwaSurfaceCatalog,
  vendorAssetCatalog,
  verifyAssetManifest,
  verifyPwaManifest,
  verifyStrictCspCompatibility
} from './build-frontend.mjs';

const root = resolve(import.meta.dirname, '..');

test('uretim CSP politikasi satir ici kodu ve tehlikeli direktifleri engeller', async () => {
  await buildFrontend();
  const headers = createSecurityHeaders('https://api.zumbo.test');
  const policy = headers['Content-Security-Policy'];
  assert.doesNotMatch(policy, /unsafe-inline|unsafe-eval|\*/i);
  assert.match(policy, /script-src 'self'/);
  assert.match(policy, /style-src 'self'/);
  assert.equal(headers['Strict-Transport-Security'], 'max-age=31536000; includeSubDomains');
  await verifyStrictCspCompatibility(resolve(root, 'dist'), policy);
});

test('yerel çalışma zamanı varlıkları tam ve sabit sürümlüdür', () => {
  const packages = new Map(vendorAssetCatalog.map(entry => [entry.package, entry.version]));
  assert.deepEqual(Object.fromEntries(packages), {
    angular: '1.8.3',
    bulma: '1.0.2',
    '@microsoft/signalr': '8.0.7',
    lucide: '1.24.0',
    'ionic-sdk': '1.3.2'
  });
  assert.equal(new Set(vendorAssetCatalog.map(entry => entry.destination)).size, vendorAssetCatalog.length);
});

test('kaynak ve üretim çıktısı çalışma zamanı CDN referansı içermez', async () => {
  assert.deepEqual(await findRuntimeCdnReferences(root), []);
  await buildFrontend();
  assert.deepEqual(await findRuntimeCdnReferences(resolve(root, 'dist')), []);
});

test('varlık manifesti eksik veya değiştirilmiş dosyayı reddeder', async t => {
  await buildFrontend();
  const temporaryRoot = await mkdtemp(resolve(tmpdir(), 'zumbo-frontend-'));
  t.after(() => rm(temporaryRoot, { recursive: true, force: true }));
  await cp(resolve(root, 'dist'), temporaryRoot, { recursive: true });
  await verifyAssetManifest(temporaryRoot);

  const manifest = JSON.parse(await readFile(resolve(temporaryRoot, 'asset-manifest.json'), 'utf8'));
  const target = resolve(temporaryRoot, manifest.assets[0].path);
  const original = await readFile(target);
  await writeFile(target, Buffer.concat([original, Buffer.from('degisti')]));
  await assert.rejects(verifyAssetManifest(temporaryRoot), /bütünlüğü/);
  await writeFile(target, original);
  await unlink(target);
  await assert.rejects(verifyAssetManifest(temporaryRoot), /dosya listesi/);
});

test('generated PWA manifestleri iki yüzeyin doğrulanmış shell varlıklarını taşır', async () => {
  await buildFrontend();
  const assetManifest = JSON.parse(await readFile(resolve(root, 'dist/asset-manifest.json'), 'utf8'));
  assert.deepEqual(pwaSurfaceCatalog.map(surface => surface.name), ['desktop', 'mobile']);
  for (const surface of pwaSurfaceCatalog) {
    const manifest = await verifyPwaManifest(resolve(root, 'dist'), surface.name);
    assert.equal(assetManifest.pwa[surface.name].cacheName, manifest.cacheName);
    assert.equal(assetManifest.pwa[surface.name].assets, manifest.assets.length);
    assert.ok(manifest.assets.some(asset => asset.url === './index.html'));
    assert.ok(manifest.assets.every(asset => asset.bytes > 0 && /^[a-f0-9]{64}$/.test(asset.sha256)));
  }
});

test('service worker API ve hub isteklerini önbelleklemez, yalnız gezinme için çevrimdışı kabuk kullanır', async () => {
  for (const path of ['desktop-bulma/service-worker.js', 'mobile-ionic/service-worker.js']) {
    const body = await readFile(resolve(root, path), 'utf8');
    assert.match(body, /__ZUMBO_GENERATED_CACHE_NAME__/);
    assert.match(body, /fetch\(MANIFEST_URL, \{ cache: 'no-store'/);
    assert.match(body, /assetResponse\.clone\(\)\.arrayBuffer\(\)/);
    assert.match(body, /sha256\(bytes\) !== asset\.sha256/);
    assert.match(body, /url\.pathname\.startsWith\('\/api\/'\)/);
    assert.match(body, /url\.pathname\.startsWith\('\/hubs\/'\)/);
    assert.match(body, /event\.request\.mode === 'navigate'/);
    assert.match(body, /const fallbackUrl = new URL\(NAVIGATION_FALLBACK/);
    assert.match(body, /cache\.match\(fallbackUrl\)/);
    assert.match(body, /\(await cache\.match\(event\.request\)\) \|\| fetch\(event\.request\)/);
    assert.doesNotMatch(body, /cache\.put\(event\.request/);
    assert.doesNotMatch(body, /STATIC_HOSTS|https?:\/\//);
  }
});

test('desktop ve mobil realtime istemcileri version gap ve reconnect resync uygular', async () => {
  for (const path of ['desktop-bulma/app.js', 'mobile-ionic/app.js']) {
    const modules = path.startsWith('desktop-bulma/')
      ? ['desktop-bulma/app.js', 'desktop-bulma/realtime.js', 'desktop-bulma/task-board.js']
      : ['mobile-ionic/app.js', 'mobile-ionic/realtime.js', 'mobile-ionic/tasks.js'];
    const body = (await Promise.all(modules.map(file => readFile(resolve(root, file), 'utf8')))).join('\n');
    assert.match(body, /change\.schemaVersion !== protocolVersion/);
    assert.match(body, /change\.resourceVersion > previous \+ 1/);
    assert.match(body, /eventType: 'resyncRequired'/);
    assert.match(body, /requestResync\('reconnected'\)/);
    assert.match(body, /requestResync\('network-online'\)/);
    assert.match(body, /withStatefulReconnect\(\{ bufferSize: 65536 \}\)/);
    assert.match(body, /realtimeService\.synchronize\(vm\.tasks\)/);
  }
});
