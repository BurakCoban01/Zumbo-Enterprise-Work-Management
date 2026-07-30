import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const worker = await readFile(resolve(root, 'mobile-ionic/service-worker.js'), 'utf8');
const client = await readFile(resolve(root, 'mobile-ionic/pwa.js'), 'utf8');
const auth = await readFile(resolve(root, 'mobile-ionic/auth.js'), 'utf8');
const template = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const styles = await readFile(resolve(root, 'mobile-ionic/styles.css'), 'utf8');
const manifest = JSON.parse(await readFile(resolve(root, 'mobile-ionic/manifest.webmanifest'), 'utf8'));

test('mobile worker caches only verified shell assets and bypasses authenticated data paths', () => {
  assert.match(worker, /event\.request\.method !== 'GET'/);
  assert.match(worker, /isApiOrHub\(url\) \|\| url\.origin !== self\.location\.origin/);
  assert.match(worker, /url\.pathname\.startsWith\('\/api\/'\)/);
  assert.match(worker, /url\.pathname\.startsWith\('\/hubs\/'\)/);
  assert.doesNotMatch(worker, /cache\.put\(event\.request/);
});

test('mobile worker limits offline fallback to navigation and verifies every staged asset', () => {
  assert.match(worker, /event\.request\.mode === 'navigate'/);
  assert.match(worker, /self\.Response\.redirect\(fallbackUrl\.href, 302\)/);
  assert.match(worker, /await sha256\(bytes\) !== asset\.sha256/);
  assert.match(worker, /const target = await caches\.open\(CACHE_NAME\)/);
  assert.ok(
    worker.indexOf('await sha256(bytes) !== asset.sha256')
      < worker.indexOf('const target = await caches.open(CACHE_NAME)')
  );
});

test('mobile client exposes offline install failure and user-controlled update states', () => {
  assert.match(client, /offline: !\$window\.navigator\.onLine/);
  assert.match(client, /event\.type === 'offline'/);
  assert.match(client, /if \(!offline && state\.registration\)/);
  assert.match(client, /updateReady: false/);
  assert.match(client, /installError: false/);
  assert.match(client, /updateViaCache: 'none'/);
  assert.match(client, /waiting\.postMessage\(\{ type: 'SKIP_WAITING' \}\)/);
  assert.match(template, /role="status" aria-live="polite"/);
  assert.match(template, /Çevrimdışısınız\./);
  assert.match(template, /Zumbo güncellemesi hazır\./);
  assert.match(template, /Çevrimdışı kullanım hazırlanamadı\./);
  assert.match(template, /shell\.pwa\.updateReady && !shell\.pwa\.offline && !shell\.pwa\.installError/);
  assert.match(styles, /\.mobile-pwa-state \.button \{[\s\S]*min-width: 76px;[\s\S]*min-height: 44px;[\s\S]*white-space: nowrap;/);
  assert.match(styles, /\.theme-dark \.mobile-pwa-state\.error/);
});

test('mobile authentication mutations are blocked while the shell is offline', () => {
  assert.equal((auth.match(/mobilePwaService\.state\.offline/g) || []).length, 4);
  assert.match(auth, /Çevrimdışıyken giriş yapılamaz\./);
  assert.match(auth, /Çevrimdışıyken demo çalışma alanı oluşturulamaz\./);
  assert.match(auth, /Çevrimdışıyken sıfırlama bağlantısı gönderilemez\./);
  assert.match(auth, /Çevrimdışıyken parola değiştirilemez\./);
  assert.match(template, /ng-disabled="shell\.pwa\.offline">Giriş yap/);
  assert.match(template, /ng-disabled="vm\.pending \|\| shell\.pwa\.offline">Bağlantı gönder/);
  assert.match(template, /ng-disabled="vm\.pending \|\| shell\.pwa\.offline">Parolayı değiştir/);
});

test('mobile install metadata stays scoped and standalone', () => {
  assert.equal(manifest.scope, './');
  assert.equal(manifest.start_url, './index.html#/app/dashboard');
  assert.equal(manifest.display, 'standalone');
  assert.equal(manifest.icons.some(icon => icon.sizes === '192x192'), true);
  assert.equal(manifest.icons.some(icon => icon.sizes === '512x512' && icon.purpose === 'maskable'), true);
});
