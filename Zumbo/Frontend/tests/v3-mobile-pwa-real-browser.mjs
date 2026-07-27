import assert from 'node:assert/strict';
import { appendFile, cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { buildFrontend, generatePwaArtifacts } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-mobile-003-real');
const surface = {
  directory: 'mobile-ionic',
  scope: '/mobile-ionic/',
  cachePrefix: 'zumbo-mobile-shell-'
};
const checks = [];
const failures = [];
const captures = [];
const workspaces = [];
const servers = [];

await mkdir(output, { recursive: true });
await buildFrontend();
const browser = await chromium.launch({ headless: true });

async function copyBuild(prefix) {
  const directory = await mkdtemp(resolve(tmpdir(), prefix));
  workspaces.push(directory);
  await cp(resolve(root, 'dist'), directory, { recursive: true });
  return directory;
}

async function start(directory) {
  const server = await startStaticServer(directory);
  servers.push(server);
  return server;
}

async function readManifest(directory) {
  return JSON.parse(await readFile(resolve(directory, surface.directory, 'pwa-manifest.json'), 'utf8'));
}

async function cacheState(page, cacheName) {
  return page.evaluate(async name => {
    const cache = await caches.open(name);
    return {
      names: await caches.keys(),
      urls: (await cache.keys()).map(request => request.url).sort()
    };
  }, cacheName);
}

function expectedUrls(origin, manifest) {
  const workerUrl = origin + '/mobile-ionic/service-worker.js';
  return manifest.assets.map(asset => new URL(asset.url, workerUrl).href).sort();
}

async function createMobilePage(server) {
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
    hasTouch: true,
    isMobile: true,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  const page = await context.newPage();
  page.on('pageerror', error => failures.push('pageerror: ' + error.message));
  page.on('console', message => {
    if (message.type() === 'error' && !/Failed to load resource|WebSocket|signalr/i.test(message.text())) {
      failures.push('console: ' + message.text());
    }
  });
  return { context, page };
}

async function waitForControl(page) {
  await page.waitForFunction(async scope => Boolean((await navigator.serviceWorker.getRegistration(scope))?.active), surface.scope);
  await page.reload({ waitUntil: 'networkidle' });
  await page.waitForFunction(() => Boolean(navigator.serviceWorker.controller));
}

async function screenshot(page, name, state) {
  const path = resolve(output, name);
  await page.screenshot({ path, fullPage: true });
  captures.push({ screenshot: `artifacts/ui/v3-mobile-003-real/${name}`, state, viewport: '390x844' });
}

async function exerciseValidInstall() {
  const directory = await copyBuild('zumbo-v3-mobile-003-valid-');
  const server = await start(directory);
  const manifest = await readManifest(directory);
  const { context, page } = await createMobilePage(server);
  try {
    const privateProbe = `${server.origin}/api/private-pwa-probe`;
    const degradedProbe = `${server.origin}/api/degraded-pwa-probe`;
    await page.route(privateProbe, async route => {
      assert.equal(route.request().headers().authorization, 'Bearer synthetic-pwa-token');
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        headers: { 'Cache-Control': 'private, no-store' },
        body: JSON.stringify({ displayName: 'Sentetik Kullanıcı', private: true })
      });
    });
    await page.route(degradedProbe, route => route.fulfill({
      status: 503,
      contentType: 'application/json',
      headers: { 'Cache-Control': 'no-store' },
      body: JSON.stringify({ error: { code: 'SEARCH_UNAVAILABLE', message: 'Geçici olarak kullanılamıyor.' } })
    }));

    await page.goto(`${server.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'networkidle' });
    await waitForControl(page);

    const initial = await cacheState(page, manifest.cacheName);
    assert.deepEqual(initial.urls, expectedUrls(server.origin, manifest));
    checks.push('verified-first-install');

    await page.reload({ waitUntil: 'networkidle' });
    assert.deepEqual((await cacheState(page, manifest.cacheName)).urls, initial.urls);
    checks.push('repeat-install-cache-stable');

    const privateResult = await page.evaluate(async url => {
      const response = await fetch(url, {
        credentials: 'include',
        headers: { Authorization: 'Bearer synthetic-pwa-token' }
      });
      return { status: response.status, body: await response.json() };
    }, privateProbe);
    assert.deepEqual(privateResult, {
      status: 200,
      body: { displayName: 'Sentetik Kullanıcı', private: true }
    });
    assert.equal((await cacheState(page, manifest.cacheName)).urls.includes(privateProbe), false);
    checks.push('authenticated-api-response-not-cached');

    const degraded = await page.evaluate(async url => {
      const response = await fetch(url);
      return { status: response.status, body: await response.json() };
    }, degradedProbe);
    assert.equal(degraded.status, 503);
    assert.equal(degraded.body.error.code, 'SEARCH_UNAVAILABLE');
    assert.equal((await cacheState(page, manifest.cacheName)).urls.includes(degradedProbe), false);
    checks.push('degraded-api-has-no-stale-cache-fallback');

    await context.setOffline(true);
    await page.goto(`${server.origin}/mobile-ionic/offline/deep-link#/forgot-password`, {
      waitUntil: 'domcontentloaded'
    });
    await page.evaluate(() => window.dispatchEvent(new window.Event('offline')));
    await page.locator('.mobile-pwa-state.offline').waitFor({ timeout: 10000 });
    await page.waitForFunction(() => document.body.innerText.includes('Parola sıfırlama'));
    assert.equal(await page.getByRole('button', { name: 'Bağlantı gönder' }).isDisabled(), true);
    const offlineState = await page.evaluate(() => ({
      path: location.pathname,
      hash: location.hash,
      overflow: document.documentElement.scrollWidth > window.innerWidth + 1,
      rawBinding: document.body.innerText.includes('{{')
    }));
    assert.deepEqual(offlineState, {
      path: '/mobile-ionic/index.html',
      hash: '#/forgot-password',
      overflow: false,
      rawBinding: false
    });
    await screenshot(page, 'offline-deep-link.png', 'offline-deep-link');
    checks.push('offline-deep-link-navigation-shell');
    await context.setOffline(false);
    await page.evaluate(() => window.dispatchEvent(new window.Event('online')));
    await page.waitForFunction(() => !document.querySelector('.mobile-pwa-state.offline'));

    await page.goto(`${server.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'networkidle' });
    const obsoleteCache = surface.cachePrefix + 'obsolete';
    const foreignCache = 'synthetic-foreign-product-cache';
    await page.evaluate(async names => {
      await caches.open(names.obsolete);
      await caches.open(names.foreign);
    }, { obsolete: obsoleteCache, foreign: foreignCache });

    await appendFile(resolve(directory, surface.directory, 'styles.css'), '\n/* v3 mobile update probe */\n');
    await generatePwaArtifacts(directory);
    const updatedManifest = await readManifest(directory);
    assert.notEqual(updatedManifest.cacheName, manifest.cacheName);
    await page.evaluate(async scope => (await navigator.serviceWorker.getRegistration(scope)).update(), surface.scope);
    await page.waitForFunction(async scope => Boolean((await navigator.serviceWorker.getRegistration(scope))?.waiting), surface.scope);
    const updateButton = page.getByRole('button', { name: 'Güncelle' });
    await updateButton.waitFor();
    assert.equal(await page.locator('.mobile-pwa-state.update').isVisible(), true);
    await screenshot(page, 'update-ready.png', 'update-ready');
    checks.push('user-controlled-update-prompt');

    await updateButton.click();
    await page.waitForFunction(async values => {
      const names = await caches.keys();
      return names.includes(values.current)
        && names.includes(values.foreign)
        && !names.includes(values.previous)
        && !names.includes(values.obsolete);
    }, {
      current: updatedManifest.cacheName,
      previous: manifest.cacheName,
      obsolete: obsoleteCache,
      foreign: foreignCache
    });
    assert.deepEqual(
      (await cacheState(page, updatedManifest.cacheName)).urls,
      expectedUrls(server.origin, updatedManifest)
    );
    checks.push('scoped-update-cache-cleanup');
  } finally {
    await context.close();
  }
}

async function exerciseCorruptUpdate() {
  const directory = await copyBuild('zumbo-v3-mobile-003-corrupt-update-');
  const server = await start(directory);
  const originalManifest = await readManifest(directory);
  const { context, page } = await createMobilePage(server);
  try {
    await page.goto(`${server.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'networkidle' });
    await waitForControl(page);
    await appendFile(resolve(directory, surface.directory, 'styles.css'), '\n/* corrupt update contract */\n');
    await generatePwaArtifacts(directory);
    const corruptManifest = await readManifest(directory);
    await appendFile(resolve(directory, surface.directory, 'manifest.webmanifest'), '\n');
    await page.evaluate(async scope => (await navigator.serviceWorker.getRegistration(scope)).update(), surface.scope);
    await page.waitForFunction(async values => {
      const registration = await navigator.serviceWorker.getRegistration(values.scope);
      const names = await caches.keys();
      return Boolean(registration?.active)
        && !registration.installing
        && !registration.waiting
        && names.includes(values.previous)
        && !names.includes(values.corrupt);
    }, {
      scope: surface.scope,
      previous: originalManifest.cacheName,
      corrupt: corruptManifest.cacheName
    });
    assert.equal(await page.locator('.mobile-pwa-state.update').count(), 0);
    checks.push('corrupt-update-retains-active-shell');
  } finally {
    await context.close();
  }
}

async function exerciseCorruptFirstInstall() {
  const directory = await copyBuild('zumbo-v3-mobile-003-corrupt-first-');
  await appendFile(resolve(directory, surface.directory, 'manifest.webmanifest'), '\n');
  const server = await start(directory);
  const { context, page } = await createMobilePage(server);
  try {
    await page.goto(`${server.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'domcontentloaded' });
    await page.locator('.mobile-pwa-state.error').waitFor({ timeout: 10000 });
    await page.locator('.login-surface').waitFor();
    assert.match(await page.locator('.mobile-pwa-state.error').innerText(), /hazırlanamadı/i);
    const state = await page.evaluate(async prefix => {
      const registration = await navigator.serviceWorker.getRegistration('/mobile-ionic/');
      return {
        active: Boolean(registration?.active),
        caches: (await caches.keys()).filter(name => name.startsWith(prefix)),
        overflow: document.documentElement.scrollWidth > window.innerWidth + 1
      };
    }, surface.cachePrefix);
    assert.deepEqual(state, { active: false, caches: [], overflow: false });
    await screenshot(page, 'corrupt-install-error.png', 'corrupt-install-error');
    checks.push('corrupt-first-install-visible-and-rejected');
  } finally {
    await context.close();
  }
}

try {
  await exerciseValidInstall();
  await exerciseCorruptUpdate();
  await exerciseCorruptFirstInstall();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await Promise.all(servers.map(server => server.close().catch(() => {})));
  await Promise.all(workspaces.map(directory => rm(directory, { recursive: true, force: true })));
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-MOBILE-003',
  passed: failures.length === 0,
  browser: 'chromium',
  viewport: '390x844',
  checks,
  captures,
  failures
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 9);
console.log(JSON.stringify(result, null, 2));
