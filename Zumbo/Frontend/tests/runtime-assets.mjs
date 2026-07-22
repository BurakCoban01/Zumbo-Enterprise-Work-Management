import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium, firefox } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { startStaticServer } from './static-server.mjs';

const browserArgumentIndex = process.argv.indexOf('--browser');
const browserName = browserArgumentIndex >= 0 ? process.argv[browserArgumentIndex + 1] : 'chromium';
const browserType = browserName === 'chromium' ? chromium : browserName === 'firefox' ? firefox : null;
if (!browserType) throw new Error(`Desteklenmeyen tarayıcı: ${browserName}`);

const root = resolve(import.meta.dirname, '..');
const outputDirectory = resolve(root, '../artifacts/ui/playwright', browserName, 'local-assets');
await mkdir(outputDirectory, { recursive: true });
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await browserType.launch({ headless: true });

try {
  for (const shell of [
    { name: 'desktop', path: '/desktop-bulma/index.html', globals: ['angular', 'signalR', 'lucide'], selector: '.desktop-login' },
    { name: 'mobile', path: '/mobile-ionic/index.html', globals: ['angular', 'ionic', 'signalR'], selector: 'ion-nav-view' }
  ]) {
    const context = await browser.newContext({ viewport: shell.name === 'desktop' ? { width: 1440, height: 1000 } : { width: 390, height: 844 } });
    const page = await context.newPage();
    const requests = [];
    const failures = [];
    page.on('request', request => requests.push(request.url()));
    page.on('requestfailed', request => failures.push(`${request.url()}: ${request.failure()?.errorText || 'başarısız'}`));
    page.on('response', response => {
      if (response.status() === 401 && /\/api\/browser-auth\/(?:session|refresh)$/.test(response.url())) return;
      if (response.status() >= 400) failures.push(`${response.status()} ${response.url()}`);
    });
    page.on('pageerror', error => failures.push(error.message));
    await page.route(`${apiBaseUrl}/**`, route => route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ error: { code: 'unauthorized', message: 'Oturum yok.' } })
    }));

    await page.goto(`${server.origin}${shell.path}`, { waitUntil: 'networkidle' });
    const documentResponse = await page.request.get(`${server.origin}${shell.path}`);
    const csp = documentResponse.headers()['content-security-policy'];
    assert.match(csp, /script-src 'self'/, `${shell.name}: kati CSP basligi eksik`);
    assert.doesNotMatch(csp, /unsafe-inline|unsafe-eval|\*/i, `${shell.name}: CSP tehlikeli direktif iceriyor`);
    await page.locator(shell.selector).waitFor();
    for (const globalName of shell.globals) {
      assert.equal(await page.evaluate(name => typeof window[name] !== 'undefined', globalName), true, `${shell.name}: ${globalName} yüklenmedi`);
    }
    await page.waitForFunction(async () => Boolean((await navigator.serviceWorker.getRegistration())?.active));
    const cachedUrls = await page.evaluate(async () => {
      const urls = [];
      for (const name of await caches.keys()) {
        const cache = await caches.open(name);
        urls.push(...(await cache.keys()).map(request => request.url));
      }
      return urls;
    });
    const allowedRequestOrigins = new Set([server.origin, new URL(apiBaseUrl).origin]);
    const externalRequests = requests.filter(url => !allowedRequestOrigins.has(new URL(url).origin));
    const externalCacheEntries = cachedUrls.filter(url => new URL(url).origin !== server.origin);
    assert.deepEqual(externalRequests, [], `${shell.name}: CDN veya üçüncü taraf isteği bulundu`);
    assert.deepEqual(externalCacheEntries, [], `${shell.name}: dış origin önbellek girdisi bulundu`);
    assert.deepEqual(failures, [], `${shell.name}: kaynak yükleme hatası bulundu`);
    assert.ok(cachedUrls.some(url => url.includes('/vendor/')), `${shell.name}: yerel vendor varlıkları önbelleğe alınmadı`);
    await page.screenshot({ path: resolve(outputDirectory, `${shell.name}.png`), fullPage: true });
    await context.close();
  }
  console.log(`${browserName}: masaüstü ve mobil kabuk yalnız yerel çalışma zamanı varlıklarıyla doğrulandı.`);
} finally {
  await browser.close();
  await server.close();
}
