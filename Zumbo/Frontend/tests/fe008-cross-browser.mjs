import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium, firefox, webkit } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';
import { createRunContext } from './e2e-run-context.mjs';

const catalog = { chromium, firefox, webkit };
const browserArgumentIndex = process.argv.indexOf('--browser');
const selectedBrowser = browserArgumentIndex >= 0 ? process.argv[browserArgumentIndex + 1] : null;
if (selectedBrowser && !catalog[selectedBrowser]) throw new Error(`Unsupported browser: ${selectedBrowser}`);
const browserNames = selectedBrowser ? [selectedBrowser] : Object.keys(catalog);
const artifactRoot = resolve(import.meta.dirname, '../../artifacts/ui/playwright');
const resultPath = resolve(artifactRoot, 'fe008-cross-browser.json');
const startedAt = new Date().toISOString();
const results = [];

await mkdir(artifactRoot, { recursive: true });

function capabilityMatrix(browserName) {
  const shared = {
    desktop: { status: 'executed', coverage: ['render', 'negative-auth', 'a11y', 'reflow', 'visual'] },
    mobile: { status: 'executed', coverage: ['render', 'negative-auth', 'a11y', 'reflow', 'visual'] }
  };
  if (browserName === 'chromium') {
    return {
      ...shared,
      permissions: { status: 'delegated', evidence: 'chromium/result.json#advanced-permission-boundary' },
      offline: { status: 'delegated', evidence: 'chromium/result.json#mobile-offline-state' },
      pwa: { status: 'delegated', evidence: 'chromium/fe007-result.json' }
    };
  }
  return {
    ...shared,
    permissions: { status: 'not-applicable', reason: 'Role mutation and revocation are covered once by the real-API Chromium lifecycle suite.' },
    offline: { status: 'not-applicable', reason: 'CacheStorage lifecycle and deterministic service-worker corruption gates are Chromium-owned.' },
    pwa: { status: 'not-applicable', reason: 'No cross-engine parity claim is made for the dedicated Chromium service-worker gate.' }
  };
}

function accessibleReport(page) {
  return page.evaluate(() => {
    const visible = element => {
      const style = window.getComputedStyle(element);
      return style.display !== 'none' && style.visibility !== 'hidden' && element.getClientRects().length > 0;
    };
    const name = element => {
      const labelledBy = element.getAttribute('aria-labelledby');
      const labelledText = labelledBy
        ? labelledBy.split(/\s+/).map(id => document.getElementById(id)?.textContent || '').join(' ').trim()
        : '';
      const explicitLabel = element.id ? document.querySelector(`label[for="${window.CSS.escape(element.id)}"]`)?.textContent.trim() : '';
      return element.getAttribute('aria-label')
        || labelledText
        || explicitLabel
        || element.closest('label')?.textContent.trim()
        || element.getAttribute('title')
        || element.getAttribute('placeholder')
        || element.textContent.trim();
    };
    const controls = Array.from(document.querySelectorAll('a[href], button, input:not([type="hidden"]), select, textarea'))
      .filter(element => visible(element) && !element.disabled);
    return {
      language: document.documentElement.lang,
      mainCount: Array.from(document.querySelectorAll('main, [role="main"]')).filter(visible).length,
      missingNames: controls.filter(element => !name(element)).map(element => element.outerHTML.slice(0, 180)),
      horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      rawBindings: /\{\{[^}]+\}\}/.test(document.body.innerText),
      styled: Array.from(document.styleSheets).filter(sheet => !sheet.disabled).length >= 2
        && window.getComputedStyle(document.body).fontFamily.length > 0
    };
  });
}

async function runSurface(browserName, browser, surface) {
  const isMobile = surface === 'mobile';
  const context = await browser.newContext(isMobile
    ? { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }
    : { viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const diagnostics = [];
  const unexpected = [];
  let expectedLoginUrl = null;
  let phase = 'navigation';

  page.on('pageerror', error => {
    diagnostics.push({ type: 'pageerror', phase, expected: false, message: error.message });
    unexpected.push(`pageerror: ${error.message}`);
  });
  page.on('requestfailed', request => {
    const detail = `${request.method()} ${request.url()} (${request.failure()?.errorText || 'unknown'})`;
    diagnostics.push({ type: 'requestfailed', phase, expected: false, detail });
    unexpected.push(`requestfailed: ${detail}`);
  });
  page.on('response', response => {
    if (response.status() < 400) return;
    const expectedAnonymousProbe = (response.status() === 401 && response.url().endsWith('/api/browser-auth/session'))
      || (response.status() === 403 && response.url().endsWith('/api/browser-auth/refresh'));
    const expected = expectedAnonymousProbe || (response.status() === 401 && response.url() === expectedLoginUrl);
    diagnostics.push({ type: 'http', phase, expected, status: response.status(), url: response.url() });
    if (!expected) unexpected.push(`HTTP ${response.status()}: ${response.url()}`);
  });
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const location = message.location();
    const expectedProbeUrl = location.url.endsWith('/api/browser-auth/session')
      || location.url.endsWith('/api/browser-auth/refresh');
    const expectedHttpConsole = message.text().includes('Failed to load resource')
      && (expectedProbeUrl || location.url === expectedLoginUrl);
    const expectedHarnessCsp = browserName === 'webkit'
      && phase === 'screenshot'
      && message.text() === "Refused to apply a stylesheet because its hash, its nonce, or 'unsafe-inline' does not appear in the style-src directive of the Content Security Policy.";
    const expected = expectedHttpConsole || expectedHarnessCsp;
    diagnostics.push({
      type: 'console',
      phase,
      expected,
      reason: expectedHarnessCsp ? 'playwright-webkit-screenshot-style' : expectedHttpConsole ? 'declared-negative-http' : null,
      message: message.text(),
      url: location.url || null
    });
    if (!expected) unexpected.push(`console: ${message.text()}`);
  });

  const route = isMobile ? '/mobile-ionic/index.html' : '/desktop-bulma/index.html';
  const readySelector = isMobile ? '.login-surface' : '.desktop-login';
  const screenshotName = `fe008-${surface}-smoke.png`;
  try {
    await page.goto(`${frontendBaseUrl}${route}`, { waitUntil: 'networkidle' });
    await page.locator(readySelector).waitFor();
    phase = 'initial-a11y';
    const initial = await accessibleReport(page);
    assert.equal(initial.language, 'tr', `${surface} document language is missing`);
    assert.equal(initial.mainCount, 1, `${surface} must expose one visible main landmark`);
    assert.deepEqual(initial.missingNames, [], `${surface} controls without accessible names: ${initial.missingNames.join(' | ')}`);
    assert.ok(initial.horizontalOverflow <= 1, `${surface} has ${initial.horizontalOverflow}px horizontal overflow`);
    assert.equal(initial.rawBindings, false, `${surface} exposed an Angular binding`);
    assert.equal(initial.styled, true, `${surface} did not load its styles`);

    const payload = `<img src=x onerror=window.__fe008Xss=1>${browserName}-${surface}`;
    expectedLoginUrl = `${apiBaseUrl}/api/browser-auth/login`;
    phase = 'negative-auth';
    const loginResponse = page.waitForResponse(response => response.url() === expectedLoginUrl && response.status() === 401);
    const inputs = page.locator(`${readySelector} input`);
    await inputs.nth(0).fill(payload);
    await inputs.nth(1).fill('invalid-password');
    await page.locator(readySelector).getByRole('button', { name: 'Giriş yap' }).click();
    await loginResponse;
    const alert = page.locator(`${readySelector} [role="alert"]`);
    await alert.waitFor();
    assert.equal(await page.evaluate(() => window.__fe008Xss), undefined, `${surface} reflected payload executed`);
    assert.doesNotMatch(await page.locator('body').innerText(), /<img src=x/i, `${surface} reflected payload as markup`);

    const afterNegative = await accessibleReport(page);
    assert.ok(afterNegative.horizontalOverflow <= 1, `${surface} negative state overflowed by ${afterNegative.horizontalOverflow}px`);
    assert.deepEqual(afterNegative.missingNames, [], `${surface} negative-state controls lost accessible names`);
    const engineDirectory = resolve(artifactRoot, browserName);
    await mkdir(engineDirectory, { recursive: true });
    phase = 'screenshot';
    await page.screenshot({ path: resolve(engineDirectory, screenshotName), fullPage: true, caret: 'initial' });
    assert.deepEqual(unexpected, [], unexpected.join('\n'));
    return { surface, passed: true, checks: ['render', 'negative-auth', 'a11y', 'reflow', 'visual'], diagnostics, screenshot: `${browserName}/${screenshotName}` };
  } catch (error) {
    return {
      surface,
      passed: false,
      checks: ['render', 'negative-auth', 'a11y', 'reflow', 'visual'],
      diagnostics,
      unexpected,
      error: error instanceof Error ? error.stack || error.message : String(error)
    };
  } finally {
    await context.close();
  }
}

for (const browserName of browserNames) {
  const runContext = createRunContext('FE-008-cross-browser', browserName);
  const browserResult = {
    browser: browserName,
    runId: runContext.runId,
    passed: false,
    capabilities: capabilityMatrix(browserName),
    surfaces: []
  };
  let browser;
  try {
    browser = await catalog[browserName].launch({
      headless: true,
      ...(browserName === 'chromium' && process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {})
    });
    for (const surface of ['desktop', 'mobile']) {
      browserResult.surfaces.push(await runSurface(browserName, browser, surface));
    }
    browserResult.passed = browserResult.surfaces.every(surface => surface.passed);
    if (!browserResult.passed) {
      browserResult.error = browserResult.surfaces
        .filter(surface => !surface.passed)
        .map(surface => `${surface.surface}: ${surface.error}`)
        .join('\n');
    }
  } catch (error) {
    browserResult.error = error instanceof Error ? error.stack || error.message : String(error);
  } finally {
    await browser?.close();
    results.push(browserResult);
  }
}

const report = {
  schemaVersion: 1,
  task: 'FE-008',
  startedAt,
  completedAt: new Date().toISOString(),
  passed: results.every(result => result.passed),
  browsers: results
};
writeFileSync(resultPath, JSON.stringify(report, null, 2));
if (!report.passed) {
  const detail = results.filter(result => !result.passed).map(result => `${result.browser}: ${result.error}`).join('\n');
  throw new Error(`FE-008 cross-browser matrix failed:\n${detail}`);
}
console.log(`FE-008 cross-browser matrix passed: ${results.length} engines, ${results.length * 2} surfaces.`);
