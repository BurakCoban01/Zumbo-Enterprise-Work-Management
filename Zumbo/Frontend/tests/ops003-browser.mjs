import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';

const outputDirectory = resolve(import.meta.dirname, '../../artifacts/ui/playwright/ops-003');
await mkdir(outputDirectory, { recursive: true });

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
  colorScheme: 'light'
});

async function markSearchAsDegraded(page) {
  await page.route(`${apiBaseUrl}/api/work-items/search`, async route => {
    const response = await route.fetch();
    const payload = await response.json();
    assert.ok(payload.data && Array.isArray(payload.data.items), 'Search response did not contain a page');
    payload.data.degraded = true;
    await route.fulfill({
      response,
      contentType: 'application/json',
      body: JSON.stringify(payload)
    });
  });
}

async function assertVisibleWithoutOverflow(page, selector) {
  const element = page.locator(selector);
  await element.waitFor({ state: 'visible', timeout: 30_000 });
  const layout = await element.evaluate(node => {
    const rect = node.getBoundingClientRect();
    return {
      left: rect.left,
      right: rect.right,
      top: rect.top,
      bottom: rect.bottom,
      viewportWidth: window.innerWidth,
      viewportHeight: window.innerHeight,
      textOverflow: node.scrollWidth - node.clientWidth
    };
  });
  assert.ok(layout.left >= 0 && layout.right <= layout.viewportWidth, `${selector} exceeded the viewport`);
  assert.ok(layout.top >= 0 && layout.top < layout.viewportHeight, `${selector} was outside the initial viewport`);
  assert.ok(layout.textOverflow <= 1, `${selector} content overflowed horizontally`);
}

try {
  const desktop = await context.newPage();
  await markSearchAsDegraded(desktop);
  await desktop.goto(`${frontendBaseUrl}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await desktop.getByRole('button', { name: 'Demo çalışma alanı oluştur' }).first().click();
  await desktop.locator('.task').first().waitFor({ timeout: 30_000 });
  await assertVisibleWithoutOverflow(desktop, '.status-banner.warning');
  assert.match(
    await desktop.locator('.status-banner.warning').innerText(),
    /güvenli yedek görünümden gösteriliyor/i
  );
  await desktop.screenshot({
    path: resolve(outputDirectory, 'desktop-degraded.png'),
    fullPage: true
  });

  const authState = await desktop.evaluate(() => ({
    currentUser: localStorage.getItem('zumbo.currentUser'),
    csrfToken: sessionStorage.getItem('zumbo.csrfToken')
  }));
  assert.ok(authState.currentUser && authState.csrfToken, 'Desktop session was not established');
  await context.addInitScript(state => {
    localStorage.setItem('zumbo.currentUser', state.currentUser);
    sessionStorage.setItem('zumbo.csrfToken', state.csrfToken);
  }, authState);

  const mobile = await context.newPage();
  await mobile.setViewportSize({ width: 390, height: 844 });
  await markSearchAsDegraded(mobile);
  await mobile.goto(`${frontendBaseUrl}/mobile-ionic/index.html`, { waitUntil: 'networkidle' });
  await mobile.locator('.mobile-degraded-state').first().waitFor({ state: 'visible', timeout: 30_000 });
  await assertVisibleWithoutOverflow(mobile, '.mobile-degraded-state:visible');
  assert.match(
    await mobile.locator('.mobile-degraded-state:visible').innerText(),
    /güvenli yedek görünümden gösteriliyor/i
  );
  assert.ok(
    await mobile.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    'Mobile degraded state introduced horizontal overflow'
  );
  await mobile.screenshot({
    path: resolve(outputDirectory, 'mobile-degraded.png'),
    fullPage: true
  });

  console.log('OPS-003 degraded browser QA passed for desktop and mobile.');
} finally {
  await context.close();
  await browser.close();
}
