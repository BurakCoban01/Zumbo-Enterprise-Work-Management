import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const appRoot = resolve(root, '..');
const screenshotRoot = resolve(appRoot, 'artifacts/ui/v3-design');
const manifestPath = resolve(appRoot, 'artifacts/v3/V3-DESIGN-001-visual.json');
await mkdir(screenshotRoot, { recursive: true });
const build = await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const captures = [];

try {
  for (const surface of [
    { name: 'gallery-desktop', path: '/shared/component-gallery.html', selector: '#gallery-main', viewport: { width: 1440, height: 1000 }, expected: 'Bileşen galerisi' },
    { name: 'gallery-mobile', path: '/shared/component-gallery.html', selector: '#gallery-main', viewport: { width: 390, height: 844 }, expected: 'Bileşen galerisi' },
    { name: 'login-desktop', path: '/desktop-bulma/index.html', selector: '.desktop-login-frame', viewport: { width: 1440, height: 1000 }, expected: 'İşinize kaldığınız yerden devam edin.' },
    { name: 'login-mobile', path: '/mobile-ionic/index.html', selector: '.brand-lockup', viewport: { width: 390, height: 844 }, expected: 'İşinize kaldığınız yerden devam edin.' }
  ]) {
    const context = await browser.newContext({ viewport: surface.viewport, reducedMotion: 'reduce' });
    const page = await context.newPage();
    const failures = [];
    const requests = [];
    page.on('request', request => requests.push(request.url()));
    page.on('requestfailed', request => failures.push(`${request.url()}: ${request.failure()?.errorText || 'failed'}`));
    page.on('pageerror', error => failures.push(error.message));
    page.on('response', response => {
      if (response.status() === 401 && /\/api\/browser-auth\/(?:session|refresh)$/.test(response.url())) return;
      if (response.status() >= 400) failures.push(`${response.status()} ${response.url()}`);
    });
    await page.route(`${apiBaseUrl}/**`, route => route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ error: { code: 'unauthorized', message: 'Oturum yok.' } })
    }));
    await page.goto(`${server.origin}${surface.path}`, { waitUntil: 'networkidle' });
    await page.locator(surface.selector).waitFor();
    assert.ok((await page.locator('body').innerText()).includes(surface.expected), `${surface.name}: expected product copy is missing.`);
    const layout = await page.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
    assert.ok(layout.scrollWidth <= layout.width + 1, `${surface.name}: horizontal page overflow ${layout.scrollWidth}/${layout.width}.`);
    await page.keyboard.press('Tab');
    const focus = await page.evaluate(() => ({ tag: document.activeElement?.tagName, outline: window.getComputedStyle(document.activeElement).outlineStyle }));
    assert.notEqual(focus.tag, 'BODY', `${surface.name}: keyboard focus did not enter the surface.`);
    assert.notEqual(focus.outline, 'none', `${surface.name}: focused control has no visible outline.`);
    await page.evaluate(() => document.activeElement?.blur());
    const allowedOrigins = new Set([server.origin, new URL(apiBaseUrl).origin]);
    assert.deepEqual(requests.filter(url => !allowedOrigins.has(new URL(url).origin)), [], `${surface.name}: external request found.`);
    assert.deepEqual(failures, [], `${surface.name}: runtime failure found.`);
    const screenshotPath = resolve(screenshotRoot, `${surface.name}.png`);
    await page.screenshot({ path: screenshotPath, fullPage: true });
    const screenshot = await readFile(screenshotPath);
    assert.ok(screenshot.length > 10_000, `${surface.name}: screenshot is unexpectedly small.`);
    captures.push({
      name: surface.name,
      route: surface.path,
      viewport: surface.viewport,
      screenshot: `artifacts/ui/v3-design/${surface.name}.png`,
      bytes: screenshot.length,
      sha256: createHash('sha256').update(screenshot).digest('hex'),
      horizontalOverflow: false,
      keyboardFocusVisible: true,
      externalRequests: 0,
      runtimeFailures: 0
    });
    await context.close();
  }
} finally {
  await browser.close();
  await server.close();
}

const manifest = {
  schemaVersion: 1,
  task: 'V3-DESIGN-001',
  generatedAtUtc: new Date().toISOString(),
  browser: 'chromium',
  localRuntimeAssetsOnly: true,
  buildAssets: build.assets.length,
  captures
};
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
console.log(`V3-DESIGN-001 browser passed: ${captures.length} responsive captures, visible keyboard focus, zero overflow/external requests/runtime failures.`);
