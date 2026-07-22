import assert from 'node:assert/strict';
import { appendFile, cp, mkdir, mkdtemp, readFile, rm, unlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { buildFrontend, generatePwaArtifacts } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/playwright/chromium');
await mkdir(output, { recursive: true });
await buildFrontend();

const workspaces = [];
const servers = [];
const browser = await chromium.launch({
  headless: true,
  ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {})
});
const checks = [];

async function copyBuild(prefix) {
  const directory = await mkdtemp(resolve(tmpdir(), prefix));
  workspaces.push(directory);
  await cp(resolve(root, 'dist'), directory, { recursive: true });
  return directory;
}

async function cacheKeys(page, cacheName) {
  return page.evaluate(async name => {
    const cache = await caches.open(name);
    return (await cache.keys()).map(request => request.url).sort();
  }, cacheName);
}

async function cacheNames(page) {
  return page.evaluate(() => caches.keys());
}

function expectedUrls(origin, surface, manifest) {
  const workerUrl = origin + '/' + surface.directory + '/service-worker.js';
  return manifest.assets.map(asset => new URL(asset.url, workerUrl).href).sort();
}

async function readPwaManifest(directory, surface) {
  return JSON.parse(await readFile(resolve(directory, surface.directory, 'pwa-manifest.json'), 'utf8'));
}

async function exerciseSurface(server, directory, surface) {
  const context = await browser.newContext({
    viewport: surface.name === 'desktop' ? { width: 1280, height: 900 } : { width: 390, height: 844 },
    ...(surface.name === 'mobile' ? { isMobile: true, hasTouch: true } : {})
  });
  const page = await context.newPage();
  const indexUrl = server.origin + '/' + surface.directory + '/index.html';
  const scopePath = '/' + surface.directory + '/';
  try {
    await page.goto(indexUrl, { waitUntil: 'networkidle' });
    await page.waitForFunction(async scope => Boolean((await navigator.serviceWorker.getRegistration(scope))?.active), scopePath);
    await page.reload({ waitUntil: 'networkidle' });
    await page.waitForFunction(() => Boolean(navigator.serviceWorker.controller));

    const initialManifest = await readPwaManifest(directory, surface);
    assert.deepEqual(
      await cacheKeys(page, initialManifest.cacheName),
      expectedUrls(server.origin, surface, initialManifest),
      surface.name + ' first install cache differs from generated manifest'
    );
    checks.push(surface.name + '-first-install');

    await page.reload({ waitUntil: 'networkidle' });
    assert.deepEqual(
      await cacheKeys(page, initialManifest.cacheName),
      expectedUrls(server.origin, surface, initialManifest),
      surface.name + ' repeat load changed the verified cache set'
    );
    checks.push(surface.name + '-repeat-load');

    await page.evaluate(async () => {
      await Promise.all([
        fetch('/api/pwa-cache-probe'),
        fetch('/hubs/pwa-cache-probe'),
        fetch('./missing-static-probe.js')
      ]);
    });
    const afterBypass = (await cacheKeys(page, initialManifest.cacheName)).join('\n');
    assert.doesNotMatch(afterBypass, /pwa-cache-probe|missing-static-probe/);
    checks.push(surface.name + '-api-hub-cache-bypass');

    await context.setOffline(true);
    await page.goto(server.origin + '/' + surface.directory + '/offline/deep-link', { waitUntil: 'domcontentloaded' });
    assert.equal(await page.title(), surface.title);
    assert.equal(new URL(page.url()).pathname, '/' + surface.directory + '/index.html');
    await page.locator(surface.name === 'desktop' ? '.desktop-login-panel' : '.login-surface').waitFor();
    const renderedState = await page.evaluate(() => ({
      angularReady: Boolean(window.angular),
      rawBinding: document.body.innerText.includes('{{'),
      styled: window.getComputedStyle(document.body).fontFamily !== 'Times New Roman'
    }));
    assert.deepEqual(renderedState, { angularReady: true, rawBinding: false, styled: true });
    const offlineResults = await page.evaluate(async directoryName => {
      async function resolves(path) {
        try {
          await fetch(path);
          return true;
        } catch {
          return false;
        }
      }
      return {
        staticAsset: await resolves('/' + directoryName + '/missing-offline.js'),
        api: await resolves('/api/offline-probe'),
        hub: await resolves('/hubs/offline-probe')
      };
    }, surface.directory);
    assert.deepEqual(offlineResults, { staticAsset: false, api: false, hub: false });
    checks.push(surface.name + '-navigation-only-offline');
    await context.setOffline(false);

    const obsoleteCache = surface.cachePrefix + 'obsolete';
    const foreignCache = 'foreign-product-cache-' + surface.name;
    await page.evaluate(async names => {
      await caches.open(names.obsolete);
      await caches.open(names.foreign);
    }, { obsolete: obsoleteCache, foreign: foreignCache });

    await appendFile(resolve(directory, surface.directory, 'styles.css'), '\n/* browser update probe */\n');
    await generatePwaArtifacts(directory);
    const updatedManifest = await readPwaManifest(directory, surface);
    assert.notEqual(updatedManifest.cacheName, initialManifest.cacheName);

    await page.evaluate(async scope => {
      const registration = await navigator.serviceWorker.getRegistration(scope);
      await registration.update();
    }, scopePath);
    await page.waitForFunction(async scope => Boolean((await navigator.serviceWorker.getRegistration(scope))?.waiting), scopePath);
    await page.waitForFunction(async cacheName => (await caches.keys()).includes(cacheName), updatedManifest.cacheName);
    const updateButton = page.getByRole('button', { name: 'Güncelle' });
    await updateButton.waitFor();
    await page.screenshot({
      path: resolve(output, surface.name + '-pwa-update.png'),
      fullPage: true
    });
    const beforeActivation = await cacheNames(page);
    assert.ok(beforeActivation.includes(initialManifest.cacheName));
    assert.ok(beforeActivation.includes(obsoleteCache));
    assert.ok(beforeActivation.includes(foreignCache));
    checks.push(surface.name + '-user-controlled-update');

    await updateButton.click();
    await page.waitForTimeout(500);
    await page.waitForFunction(async values => {
      const names = await caches.keys();
      return names.includes(values.current)
        && names.includes(values.foreign)
        && !names.includes(values.previous)
        && !names.includes(values.obsolete);
    }, {
      current: updatedManifest.cacheName,
      previous: initialManifest.cacheName,
      obsolete: obsoleteCache,
      foreign: foreignCache
    });
    assert.deepEqual(
      await cacheKeys(page, updatedManifest.cacheName),
      expectedUrls(server.origin, surface, updatedManifest)
    );
    checks.push(surface.name + '-scoped-cache-cleanup');
  } finally {
    await context.close();
  }
}

async function exerciseBrokenInstall(server, surface) {
  const context = await browser.newContext({
    viewport: surface.name === 'desktop' ? { width: 1280, height: 900 } : { width: 390, height: 844 },
    ...(surface.name === 'mobile' ? { isMobile: true, hasTouch: true } : {})
  });
  const page = await context.newPage();
  try {
    await page.goto(server.origin + '/' + surface.directory + '/index.html', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(2500);
    const state = await page.evaluate(async values => {
      const registration = await navigator.serviceWorker.getRegistration(values.scope);
      return {
        active: Boolean(registration?.active),
        cacheNames: (await caches.keys()).filter(name => name.startsWith(values.prefix))
      };
    }, { scope: '/' + surface.directory + '/', prefix: surface.cachePrefix });
    assert.equal(state.active, false, surface.name + ' broken install became active');
    assert.deepEqual(state.cacheNames, [], surface.name + ' broken install left a cache');
    checks.push(surface.name + '-' + surface.failure + '-rejected');
  } finally {
    await context.close();
  }
}

try {
  const validDirectory = await copyBuild('zumbo-fe007-valid-');
  const validServer = await startStaticServer(validDirectory);
  servers.push(validServer);
  await exerciseSurface(validServer, validDirectory, {
    name: 'desktop',
    directory: 'desktop-bulma',
    title: 'Zumbo Desktop',
    cachePrefix: 'zumbo-desktop-shell-'
  });
  await exerciseSurface(validServer, validDirectory, {
    name: 'mobile',
    directory: 'mobile-ionic',
    title: 'Zumbo',
    cachePrefix: 'zumbo-mobile-shell-'
  });

  const brokenDirectory = await copyBuild('zumbo-fe007-broken-');
  await appendFile(resolve(brokenDirectory, 'desktop-bulma/manifest.webmanifest'), '\n');
  await unlink(resolve(brokenDirectory, 'mobile-ionic/manifest.webmanifest'));
  const brokenServer = await startStaticServer(brokenDirectory);
  servers.push(brokenServer);
  await exerciseBrokenInstall(brokenServer, {
    name: 'desktop',
    directory: 'desktop-bulma',
    cachePrefix: 'zumbo-desktop-shell-',
    failure: 'corrupt-asset'
  });
  await exerciseBrokenInstall(brokenServer, {
    name: 'mobile',
    directory: 'mobile-ionic',
    cachePrefix: 'zumbo-mobile-shell-',
    failure: 'missing-asset'
  });

  const result = { passed: true, browser: 'chromium', checks };
  await writeFile(resolve(output, 'fe007-result.json'), JSON.stringify(result, null, 2) + '\n', 'utf8');
  console.log('FE-007 Chromium: ' + checks.length + '/' + checks.length + ' kontrol geçti.');
} catch (error) {
  await writeFile(resolve(output, 'fe007-result.json'), JSON.stringify({
    passed: false,
    browser: 'chromium',
    checks,
    error: error.stack || error.message
  }, null, 2) + '\n', 'utf8');
  throw error;
} finally {
  await browser.close();
  await Promise.all(servers.map(server => server.close().catch(() => {})));
  await Promise.all(workspaces.map(directory => rm(directory, { recursive: true, force: true })));
}
