import assert from 'node:assert/strict';
import { appendFile, cp, mkdtemp, readFile, rm, unlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import test from 'node:test';
import {
  buildFrontend,
  generatePwaArtifacts,
  pwaSurfaceCatalog,
  verifyPwaManifest
} from './build-frontend.mjs';

const root = resolve(import.meta.dirname, '..');
const built = await buildFrontend();
const manifests = Object.fromEntries(await Promise.all(pwaSurfaceCatalog.map(async surface => [
  surface.name,
  await verifyPwaManifest(resolve(root, 'dist'), surface.name)
])));

async function copyBuild(t) {
  const directory = await mkdtemp(resolve(tmpdir(), 'zumbo-pwa-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await cp(resolve(root, 'dist'), directory, { recursive: true });
  return directory;
}

test('asset manifest generated desktop ve mobile PWA metadata tasir', () => {
  assert.equal(built.schemaVersion, 1);
  for (const surface of pwaSurfaceCatalog) {
    assert.equal(built.pwa[surface.name].manifest, surface.manifestPath);
    assert.equal(built.pwa[surface.name].cacheName, manifests[surface.name].cacheName);
    assert.equal(built.pwa[surface.name].assets, manifests[surface.name].assets.length);
  }
});

test('generated shell listeleri tum surface modullerini ve yerel runtime varliklarini kapsar', () => {
  const expectedDesktop = [
    './app.js', './board-view.js', './directives.js', './management.js', './planning.js',
    './pwa.js', './realtime.js', './settings.js', './task-board.js', './work-items.js',
    '../runtime-config.js', '../shared/api-client.js', '../vendor/angular/angular.min.js'
  ];
  const expectedMobile = [
    './api.js', './app.js', './auth.js', './details.js', './directives.js', './pwa.js',
    './realtime.js', './tasks.js', './workspace.js', '../runtime-config.js',
    '../shared/api-client.js', '../vendor/ionic/ionic.bundle.min.js'
  ];
  for (const url of expectedDesktop) {
    assert.ok(manifests.desktop.assets.some(asset => asset.url === url), 'desktop shell missing ' + url);
  }
  for (const url of expectedMobile) {
    assert.ok(manifests.mobile.assets.some(asset => asset.url === url), 'mobile shell missing ' + url);
  }
});

test('PWA cache surumu shell iceriginden deterministik turetilir ve surface ile sinirlidir', async t => {
  const directory = await copyBuild(t);
  const originalDesktop = manifests.desktop.cacheName;
  const originalMobile = manifests.mobile.cacheName;
  await appendFile(resolve(directory, 'desktop-bulma/styles.css'), '\n/* pwa version probe */\n');
  const changed = await generatePwaArtifacts(directory);
  assert.notEqual(changed.desktop.cacheName, originalDesktop);
  assert.equal(changed.mobile.cacheName, originalMobile);
  await verifyPwaManifest(directory, 'desktop');
  await verifyPwaManifest(directory, 'mobile');
});

test('PWA manifest dogrulamasi corrupt shell assetini reddeder', async t => {
  const directory = await copyBuild(t);
  await appendFile(resolve(directory, 'mobile-ionic/tasks.js'), '\ncorrupt');
  await assert.rejects(verifyPwaManifest(directory, 'mobile'), /bütünlüğü/);
});

test('PWA manifest dogrulamasi eksik shell assetini reddeder', async t => {
  const directory = await copyBuild(t);
  await unlink(resolve(directory, 'desktop-bulma/pwa.js'));
  await assert.rejects(verifyPwaManifest(directory, 'desktop'), /bütünlüğü/);
});

test('worker install staging cache ve SHA-256 dogrulamasi tamamlanmadan aktif cache yazmaz', async () => {
  for (const surface of pwaSurfaceCatalog) {
    const worker = await readFile(resolve(root, surface.workerPath), 'utf8');
    const verifyAt = worker.indexOf("await sha256(bytes) !== asset.sha256");
    const targetAt = worker.indexOf('const target = await caches.open(CACHE_NAME)');
    assert.ok(verifyAt >= 0 && targetAt > verifyAt);
    assert.match(worker, /const stagingName = CACHE_NAME \+ '-installing'/);
    assert.match(worker, /finally \{\s*await caches\.delete\(stagingName\)/);
  }
});

test('worker yalniz navigation fallback uygular; API hub cross-origin ve mutation bypass edilir', async () => {
  for (const surface of pwaSurfaceCatalog) {
    const worker = await readFile(resolve(root, surface.workerPath), 'utf8');
    assert.match(worker, /event\.request\.method !== 'GET'/);
    assert.match(worker, /isApiOrHub\(url\) \|\| url\.origin !== self\.location\.origin/);
    assert.match(worker, /event\.request\.mode === 'navigate'/);
    assert.match(worker, /cache\.match\(fallbackUrl\)/);
    assert.match(worker, /requestUrl\.pathname === fallbackUrl\.pathname/);
    assert.match(worker, /self\.Response\.redirect\(fallbackUrl\.href, 302\)/);
    assert.match(worker, /\(await cache\.match\(event\.request\)\) \|\| fetch\(event\.request\)/);
    assert.doesNotMatch(worker, /cache\.put\(event\.request/);
  }
});

test('activate yalniz kendi eski Zumbo cachelerini temizler ve update kullanici kontrolludur', async () => {
  const desktopPwa = await readFile(resolve(root, 'desktop-bulma/pwa.js'), 'utf8');
  const mobilePwa = await readFile(resolve(root, 'mobile-ionic/pwa.js'), 'utf8');
  for (const surface of pwaSurfaceCatalog) {
    const worker = await readFile(resolve(root, surface.workerPath), 'utf8');
    assert.match(worker, /key\.startsWith\(CACHE_PREFIX\) && key !== CACHE_NAME/);
    assert.match(worker, /event\.data\.type === 'SKIP_WAITING'/);
  }
  for (const client of [desktopPwa, mobilePwa]) {
    assert.match(client, /updateReady: false/);
    assert.match(client, /installError: false/);
    assert.match(client, /if \(!controlled\) \{\s*controlled = true;\s*return;/);
    assert.match(client, /waiting\.postMessage\(\{ type: 'SKIP_WAITING' \}\)/);
    assert.match(client, /updateViaCache: 'none'/);
  }
});
