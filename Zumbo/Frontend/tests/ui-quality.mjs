import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium, firefox, webkit } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const baseUrl = frontendBaseUrl;
const frontendOrigin = new URL(baseUrl).origin;
const browserArgumentIndex = process.argv.indexOf('--browser');
const browserName = browserArgumentIndex >= 0 ? process.argv[browserArgumentIndex + 1] : 'chromium';
const browserType = { chromium, firefox, webkit }[browserName];
if (!browserType) throw new Error(`Unsupported E2E browser: ${browserName}`);
const outputDir = resolve(import.meta.dirname, `../../artifacts/ui/playwright/${browserName}`);
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for the role-management E2E');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for the role-management E2E');
const runContext = createRunContext('FE-008', browserName);
const cleanupLedger = createCleanupLedger();
const failures = [];
const diagnostics = [];
const consoleFailures = [];
const networkFailures = [];
const expectedHttpResponses = [];
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
let diagnosticPage = null;
let sessionRotationInProgress = false;
let cleanupAdminTokenPromise = null;
await mkdir(outputDir, { recursive: true });

function writeMachineResult(result) {
  writeFileSync(resolve(outputDir, 'result.json'), JSON.stringify({
    ...result,
    browser: browserName,
    runId: runContext.runId,
    tenants: runContext.tenants,
    cleanup: cleanupResult,
    diagnostics,
    consoleFailures,
    networkFailures
  }, null, 2));
}

process.on('uncaughtException', error => {
  writeMachineResult({ passed: false, error: error.stack || error.message });
  process.exit(1);
});
process.on('unhandledRejection', error => {
  writeMachineResult({ passed: false, error: error.stack || String(error) });
  process.exit(1);
});

const browser = await browserType.launch({
  headless: true,
  ...(browserName === 'chromium' && process.env.CHROME_PATH
    ? { executablePath: process.env.CHROME_PATH }
    : {})
});

async function attachDiagnostics(page, name) {
  page.on('pageerror', error => {
    const detail = `${name} page error: ${error.message}`;
    diagnostics.push({ surface: name, type: 'pageerror', expected: false, message: error.message });
    consoleFailures.push(detail);
    failures.push(detail);
  });
  page.on('response', response => {
    if (sessionRotationInProgress
      && response.status() === 401
      && response.url().startsWith(apiBaseUrl)) {
      diagnostics.push({ surface: name, type: 'http', expected: true, status: response.status(), url: response.url(), reason: 'session-rotation' });
      return;
    }
    if ((response.status() === 401 && response.url().endsWith('/api/browser-auth/session'))
      || (response.status() === 403 && response.url().endsWith('/api/browser-auth/refresh'))) {
      diagnostics.push({ surface: name, type: 'http', expected: true, status: response.status(), url: response.url(), reason: 'anonymous-session-probe' });
      return;
    }
    const expected = expectedHttpResponses.find(item =>
      !item.seen && item.status === response.status() && response.url().includes(item.urlPart));
    if (expected) {
      expected.seen = true;
      diagnostics.push({ surface: name, type: 'http', expected: true, status: response.status(), url: response.url(), reason: 'declared-negative-case' });
      return;
    }
    if (response.status() >= 400) {
      const detail = `${name} HTTP ${response.status()}: ${response.url()}`;
      diagnostics.push({ surface: name, type: 'http', expected: false, status: response.status(), url: response.url() });
      networkFailures.push(detail);
      failures.push(detail);
    }
  });
  page.on('requestfailed', request => {
    const errorText = request.failure()?.errorText || 'unknown';
    const expectedClientCancellation = request.method() === 'GET' && errorText === 'net::ERR_ABORTED';
    const detail = `${name} request failed: ${request.method()} ${request.url()} (${errorText})`;
    diagnostics.push({
      surface: name,
      type: 'requestfailed',
      expected: expectedClientCancellation,
      method: request.method(),
      url: request.url(),
      error: errorText,
      reason: expectedClientCancellation ? 'client-cancelled-get' : null
    });
    if (expectedClientCancellation) return;
    networkFailures.push(detail);
    failures.push(detail);
  });
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const location = message.location();
    const expectedProbeError = message.text().includes('Failed to load resource')
      && (location.url.endsWith('/api/browser-auth/session')
        || location.url.endsWith('/api/browser-auth/refresh'));
    const expectedResourceError = message.text().includes('Failed to load resource')
      && expectedHttpResponses.some(item => item.seen && location.url.includes(item.urlPart));
    diagnostics.push({
      surface: name,
      type: 'console',
      expected: expectedProbeError || expectedResourceError,
      message: message.text(),
      url: location.url || null
    });
    if (expectedProbeError || expectedResourceError) return;
    const detail = `${name} console error: ${message.text()}${location.url ? ` (${location.url})` : ''}`;
    consoleFailures.push(detail);
    failures.push(detail);
  });
}

function expectHttpResponse(status, urlPart) {
  const expected = { status, urlPart, seen: false };
  expectedHttpResponses.push(expected);
  return expected;
}

async function assertNoOverflow(page, selector = 'body') {
  const overflow = await page.locator(selector).evaluate(element => ({
    horizontal: element.scrollWidth - element.clientWidth,
    vertical: element.scrollHeight - element.clientHeight
  }));
  assert.ok(overflow.horizontal <= 1, `${selector} has ${overflow.horizontal}px horizontal overflow`);
}

async function assertAccessibleSurface(page) {
  const report = await page.evaluate(() => {
    const visible = element => {
      const style = window.getComputedStyle(element);
      return style.visibility !== 'hidden' && style.display !== 'none' && element.getClientRects().length > 0;
    };
    const accessibleName = element => {
      const labelledBy = element.getAttribute('aria-labelledby');
      if (labelledBy) {
        const text = labelledBy.split(/\s+/).map(id => document.getElementById(id)?.textContent || '').join(' ').trim();
        if (text) return text;
      }
      const idLabel = element.id ? document.querySelector(`label[for="${window.CSS.escape(element.id)}"]`)?.textContent.trim() : '';
      const wrappingLabel = element.closest('label')?.textContent.trim();
      return element.getAttribute('aria-label')
        || idLabel
        || wrappingLabel
        || element.getAttribute('title')
        || element.getAttribute('placeholder')
        || element.textContent.trim();
    };
    const controls = Array.from(document.querySelectorAll('a[href], button, input:not([type="hidden"]), select, textarea, [role="tab"]'))
      .filter(element => visible(element) && !element.disabled);
    return {
      mainCount: Array.from(document.querySelectorAll('main, [role="main"]')).filter(visible).length,
      missingNames: controls.filter(element => !accessibleName(element)).map(element => element.outerHTML.slice(0, 240))
    };
  });
  assert.equal(report.mainCount, 1, `Expected one visible main landmark, received ${report.mainCount}`);
  assert.deepEqual(report.missingNames, [], `Visible controls without an accessible name: ${report.missingNames.join(' | ')}`);
}

async function assertTextContrast(page) {
  const violations = await page.evaluate(() => {
    function rgba(value) {
      const match = value.match(/rgba?\(([^)]+)\)/);
      if (!match) return null;
      const parts = match[1].split(/[\s,/]+/).filter(Boolean).map(Number);
      return { red: parts[0], green: parts[1], blue: parts[2], alpha: parts.length > 3 ? parts[3] : 1 };
    }
    function blend(foreground, background) {
      const alpha = foreground.alpha + background.alpha * (1 - foreground.alpha);
      return {
        red: (foreground.red * foreground.alpha + background.red * background.alpha * (1 - foreground.alpha)) / alpha,
        green: (foreground.green * foreground.alpha + background.green * background.alpha * (1 - foreground.alpha)) / alpha,
        blue: (foreground.blue * foreground.alpha + background.blue * background.alpha * (1 - foreground.alpha)) / alpha,
        alpha
      };
    }
    function background(element) {
      let result = { red: 255, green: 255, blue: 255, alpha: 1 };
      const layers = [];
      for (let current = element; current; current = current.parentElement) {
        const color = rgba(window.getComputedStyle(current).backgroundColor);
        if (color && color.alpha > 0) layers.push(color);
      }
      for (const layer of layers.reverse()) result = blend(layer, result);
      return result;
    }
    function luminance(color) {
      return ['red', 'green', 'blue'].map(key => {
        const ratio = color[key] / 255;
        return ratio <= 0.04045 ? ratio / 12.92 : ((ratio + 0.055) / 1.055) ** 2.4;
      }).reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0);
    }
    function ratio(left, right) {
      const values = [luminance(left), luminance(right)].sort((a, b) => b - a);
      return (values[0] + 0.05) / (values[1] + 0.05);
    }
    return Array.from(document.querySelectorAll('body *')).filter(element => {
      const style = window.getComputedStyle(element);
      return element.getClientRects().length > 0
        && style.visibility !== 'hidden'
        && style.opacity !== '0'
        && !element.matches(':disabled, [aria-disabled="true"]')
        && Array.from(element.childNodes).some(node => node.nodeType === window.Node.TEXT_NODE && node.textContent.trim());
    }).map(element => {
      const style = window.getComputedStyle(element);
      const foreground = rgba(style.color);
      const backdrop = background(element);
      const fontSize = Number.parseFloat(style.fontSize) || 16;
      const fontWeight = Number.parseInt(style.fontWeight, 10) || 400;
      const threshold = fontSize >= 24 || (fontSize >= 18.66 && fontWeight >= 700) ? 3 : 4.5;
      return {
        text: Array.from(element.childNodes).filter(node => node.nodeType === window.Node.TEXT_NODE).map(node => node.textContent).join(' ').trim().slice(0, 80),
        ratio: foreground ? ratio(foreground, backdrop) : 21,
        threshold
      };
    }).filter(item => item.ratio + 0.01 < item.threshold).slice(0, 20);
  });
  assert.deepEqual(violations, [], `WCAG text contrast violations: ${JSON.stringify(violations)}`);
}

async function assertAccessibilityPreferences(page, screenshotName) {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const motion = await page.evaluate(() => Array.from(document.querySelectorAll('*'))
    .filter(element => element.getClientRects().length > 0)
    .map(element => {
      const style = window.getComputedStyle(element);
      return [style.animationDuration, style.transitionDuration].flatMap(value => value.split(',')).map(part => Number.parseFloat(part) || 0);
    }).flat().reduce((maximum, value) => Math.max(maximum, value), 0));
  assert.ok(motion <= 0.01, `Reduced-motion surface retained ${motion}s motion`);

  await page.emulateMedia({ reducedMotion: 'reduce', forcedColors: 'active' });
  assert.ok(await page.evaluate(() => window.matchMedia('(forced-colors: active)').matches), 'Forced-colors emulation was not active');
  const focusTarget = page.locator('.skip-link');
  await focusTarget.focus();
  const outline = await focusTarget.evaluate(element => window.getComputedStyle(element).outlineStyle);
  assert.notEqual(outline, 'none', 'Forced-colors focus indicator disappeared');
  await page.screenshot({ path: resolve(outputDir, screenshotName), fullPage: true });
  await page.emulateMedia({ reducedMotion: 'no-preference', forcedColors: 'none' });
}

async function assertZoomReflow(page, screenshotName) {
  const viewport = page.viewportSize();
  assert.ok(viewport, 'Zoom reflow requires a fixed viewport');
  await page.setViewportSize({ width: Math.max(320, Math.floor(viewport.width / 2)), height: viewport.height });
  await assertNoOverflow(page);
  await page.screenshot({ path: resolve(outputDir, screenshotName), fullPage: true });
  await page.setViewportSize(viewport);
}

async function assertVisibleFocus(page) {
  await page.mouse.click(1, 1);
  await page.keyboard.press('Tab');
  const focused = page.locator(':focus');
  await focused.waitFor();
  const state = await focused.evaluate(element => ({
    className: element.className,
    outlineWidth: Number.parseFloat(window.getComputedStyle(element).outlineWidth) || 0
  }));
  assert.ok(state.outlineWidth >= 3, `Keyboard focus indicator was ${state.outlineWidth}px on ${state.className}`);
}

async function assertPwa(page) {
  const manifest = await page.locator('link[rel="manifest"]').getAttribute('href');
  assert.ok(manifest, 'manifest link is missing');
  const manifestPayload = await page.evaluate(async href => (await fetch(href)).json(), manifest);
  assert.equal(manifestPayload.display, 'standalone');
  assert.ok(manifestPayload.icons.some(icon => icon.sizes === '192x192' && icon.type === 'image/png'));
  assert.ok(manifestPayload.icons.some(icon => icon.sizes === '512x512' && icon.type === 'image/png'));
  assert.ok(manifestPayload.icons.some(icon => icon.purpose.includes('maskable')));
  await page.waitForFunction(async () => {
    if (!navigator.serviceWorker) return false;
    const registration = await navigator.serviceWorker.getRegistration();
    return Boolean(registration?.active);
  });
}

async function cachedUrls(page) {
  return page.evaluate(async () => {
    const urls = [];
    for (const cacheName of await caches.keys()) {
      const cache = await caches.open(cacheName);
      urls.push(...(await cache.keys()).map(request => request.url));
    }
    return urls;
  });
}

async function apiRequest(path, method, body, token, extraHeaders = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...extraHeaders
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json();
  return { response, payload, data: payload.data };
}

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      let authentication = await apiRequest('/api/auth/register', 'POST', {
        username: 'local-system-admin',
        email: adminEmail,
        password: 'P@ssword123',
        organizationId: 'local-system-administration',
        bootstrapToken: adminBootstrapToken
      });
      if (authentication.response.status === 409) {
        authentication = await apiRequest('/api/auth/login', 'POST', {
          usernameOrEmail: adminEmail,
          password: 'P@ssword123'
        });
      }
      if (!authentication.response.ok) {
        throw new Error(authentication.payload.error?.message || 'Cleanup administrator authentication failed');
      }
      return authentication.data.accessToken;
    })();
  }
  return cleanupAdminTokenPromise;
}

async function archiveTenant(tenantId) {
  const token = await cleanupAdminToken();
  const response = await fetch(`${apiBaseUrl}/api/organizations/${encodeURIComponent(tenantId)}/archive`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`
    }
  });
  if (response.ok) return { tenantId, outcome: 'archived', status: response.status };
  if (response.status === 404) return { tenantId, outcome: 'not-created', status: response.status };
  const payload = await response.json().catch(() => ({}));
  throw new Error(payload.error?.message || `Tenant cleanup failed with HTTP ${response.status}`);
}

async function routeDemoRegistration(page, tenantId, surface) {
  await page.route(`${apiBaseUrl}/api/browser-auth/register`, async route => {
    const request = route.request();
    if (request.method() !== 'POST') {
      await route.continue();
      return;
    }
    const body = request.postDataJSON();
    body.organizationId = tenantId;
    cleanupLedger.add(`archive:${tenantId}`, () => archiveTenant(tenantId));
    diagnostics.push({
      surface,
      type: 'fixture',
      expected: true,
      action: 'tenant-registration-rewrite',
      tenantId
    });
    await route.continue({
      headers: { ...request.headers(), 'content-type': 'application/json' },
      postData: JSON.stringify(body)
    });
  });
}

async function browserLogin(page, usernameOrEmail, password) {
  return page.evaluate(async ({ apiUrl, username, secret }) => {
    const csrfToken = sessionStorage.getItem('zumbo.csrfToken');
    const response = await fetch(`${apiUrl}/api/browser-auth/login`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        ...(csrfToken ? { 'X-CSRF-Token': csrfToken } : {})
      },
      body: JSON.stringify({ usernameOrEmail: username, password: secret })
    });
    const payload = await response.json();
    if (!response.ok) throw new Error(payload.error?.message || 'Browser login failed');
    localStorage.setItem('zumbo.currentUser', JSON.stringify(payload.data.user));
    sessionStorage.setItem('zumbo.csrfToken', payload.data.csrfToken);
    return payload.data;
  }, { apiUrl: apiBaseUrl, username: usernameOrEmail, secret: password });
}

async function browserContextLogin(context, usernameOrEmail, password) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin },
    data: { usernameOrEmail, password }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser context login failed');
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, payload.data);
  return payload.data;
}

async function assertBrowserSecretsAreInaccessible(page) {
  const state = await page.evaluate(() => ({
    accessToken: localStorage.getItem('zumbo.accessToken') || sessionStorage.getItem('zumbo.accessToken'),
    refreshToken: localStorage.getItem('zumbo.refreshToken') || sessionStorage.getItem('zumbo.refreshToken'),
    visibleCookies: document.cookie
  }));
  assert.equal(state.accessToken, null, 'Access token was exposed through Web Storage');
  assert.equal(state.refreshToken, null, 'Refresh token was exposed through Web Storage');
  assert.doesNotMatch(state.visibleCookies, /zumbo-(?:access|refresh)=/i, 'HttpOnly auth cookie was visible to JavaScript');
}

try {
  const fixtureStamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const desktop = await browser.newContext({ viewport: { width: 1440, height: 1000 }, colorScheme: 'light' });
  const page = await desktop.newPage();
  diagnosticPage = page;
  await attachDiagnostics(page, 'desktop');
  await routeDemoRegistration(page, runContext.tenants.desktop, 'desktop');
  await page.goto(`${baseUrl}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  const desktopRegistrationRequest = page.waitForRequest(request =>
    request.method() === 'POST' && request.url().endsWith('/api/browser-auth/register'));
  await page.locator('.desktop-login form').getByRole('button', { name: 'Demo çalışma alanı oluştur' }).click();
  const desktopDemoPassword = (await desktopRegistrationRequest).postDataJSON().password;
  assert.ok(desktopDemoPassword, 'Desktop demo registration did not include a generated password');
  await page.locator('.task').first().waitFor({ timeout: 30_000 });
  await assertBrowserSecretsAreInaccessible(page);
  const originalProjectId = await page.locator('.task').first().getAttribute('data-project-id');
  assert.ok(originalProjectId, 'Initial task did not expose its project relationship');
  const workspaceUser = await page.evaluate(() => JSON.parse(localStorage.getItem('zumbo.currentUser')));
  assert.equal(workspaceUser.organizationId, runContext.tenants.desktop, 'Desktop fixture escaped its controlled tenant');
  const collaboratorRegistration = await apiRequest('/api/auth/register', 'POST', {
    username: `collaborator${fixtureStamp}`,
    email: `collaborator${fixtureStamp}@zumbo.local`,
    password: 'P@ssword123',
    organizationId: workspaceUser.organizationId
  });
  assert.ok(collaboratorRegistration.response.ok, collaboratorRegistration.payload.error?.message || 'Collaborator registration failed');
  const collaborator = collaboratorRegistration.data.user;
  await page.waitForLoadState('networkidle');
  let adminAuth = await apiRequest('/api/auth/register', 'POST', {
    username: 'local-system-admin',
    email: adminEmail,
    password: 'P@ssword123',
    organizationId: 'local-system-administration',
    bootstrapToken: adminBootstrapToken
  });
  if (adminAuth.response.status === 409) {
    adminAuth = await apiRequest('/api/auth/login', 'POST', {
      usernameOrEmail: adminEmail,
      password: 'P@ssword123'
    });
  }
  assert.ok(adminAuth.response.ok, adminAuth.payload.error?.message || 'SystemAdmin authentication failed');
  sessionRotationInProgress = true;
  const roleGrant = await apiRequest(`/api/auth/users/${workspaceUser.id}/roles`, 'PUT', {
    roles: ['User', 'OrganizationAdmin']
  }, adminAuth.data.accessToken);
  assert.ok(roleGrant.response.ok, roleGrant.payload.error?.message || 'OrganizationAdmin grant failed');
  const elevatedSession = await apiRequest('/api/auth/login', 'POST', {
    usernameOrEmail: workspaceUser.username,
    password: desktopDemoPassword
  });
  assert.ok(elevatedSession.response.ok, elevatedSession.payload.error?.message || 'Elevated user login failed');
  await browserLogin(page, workspaceUser.username, desktopDemoPassword);
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('.task').first().waitFor({ timeout: 30_000 });
  await assertBrowserSecretsAreInaccessible(page);
  sessionRotationInProgress = false;
  await page.locator('.side-nav').waitFor();
  assert.ok(await page.locator('.side-nav svg').count() >= 5, 'Lucide navigation icons did not render');
  await assertNoOverflow(page);
  await assertPwa(page);
  await page.waitForFunction(() => Number(document.querySelector('.summary-strip strong')?.textContent || 0) >= 1);
  await assertAccessibleSurface(page);
  await assertTextContrast(page);
  await assertVisibleFocus(page);
  await assertAccessibilityPreferences(page, 'desktop-forced-colors.png');
  await assertZoomReflow(page, 'desktop-zoom-200.png');
  const desktopVisibleText = await page.locator('body').innerText();
  assert.doesNotMatch(desktopVisibleText, new RegExp(workspaceUser.organizationId), 'Desktop exposed the opaque organization identifier');
  await page.screenshot({ path: resolve(outputDir, 'desktop-light.png'), fullPage: true });

  const epicTitle = `UI Epic ${String(Date.now()).slice(-6)}`;
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).click();
  await page.waitForFunction(() => document.activeElement?.id === 'new-task-title');
  await page.locator('#new-task-title').fill(epicTitle);
  await page.locator('#new-task-type').selectOption('Epic');
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev oluşturuldu.' }).waitFor();
  await page.waitForFunction(() => document.activeElement?.classList.contains('create-button'));
  await page.locator('.task').filter({ hasText: epicTitle }).waitFor();

  const storedXssTitle = '<img src=x onerror="window.__zumboStoredXss=1">';
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).click();
  await page.locator('#new-task-title').fill(storedXssTitle);
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev oluşturuldu.' }).waitFor();
  const storedXssTask = page.locator('.task').filter({ hasText: storedXssTitle });
  await storedXssTask.waitFor();
  assert.equal(await storedXssTask.locator('h2').textContent(), storedXssTitle, 'Stored XSS payload was not rendered as text');
  assert.equal(await storedXssTask.locator('img').count(), 0, 'Stored XSS payload created an executable DOM node');
  assert.equal(await page.evaluate(() => window.__zumboStoredXss), undefined, 'Stored XSS payload executed');

  const firstTask = page.locator('.task').first();
  const firstTitle = await firstTask.locator('h2').innerText();
  await firstTask.locator('input[type="checkbox"]').click();
  await page.locator('.bulk-toolbar').waitFor();
  await firstTask.press('Alt+ArrowRight');
  await page.waitForFunction(title => {
    const lanes = Array.from(document.querySelectorAll('.column-lane'));
    return lanes.length > 1 && lanes[1].textContent.includes(title);
  }, firstTitle, { timeout: 15_000 });

  await page.locator('.task').filter({ hasText: firstTitle }).click();
  await page.locator('.inspector').waitFor();
  assert.match(page.url(), /#section=board(?:&|$)/, 'Board section deep link was not written');
  assert.match(page.url(), /[?&]task=/, 'Task detail deep link was not written');
  await page.locator('#task-description').fill('Playwright yaşam döngüsü doğrulaması');
  await page.locator('#task-parent').selectOption({ label: epicTitle });
  const calendarDate = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 10);
  await page.locator('#task-due-date').fill(calendarDate);
  const desktopVersionedMutation = page.waitForRequest(request =>
    request.method() === 'PUT' && /\/api\/work-items\/[^/]+$/.test(request.url()));
  await page.getByRole('button', { name: 'Ayrıntıları kaydet' }).click();
  assert.match(
    (await desktopVersionedMutation).headers()['if-match'] || '',
    /^"\d+"$/,
    'Desktop work-item mutation did not send If-Match');
  await page.locator('.toast.success').filter({ hasText: 'Görev ayrıntıları kaydedildi.' }).waitFor();
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('.inspector').waitFor({ timeout: 15_000 });
  assert.equal(await page.locator('#task-description').inputValue(), 'Playwright yaşam döngüsü doğrulaması');
  assert.equal((await page.locator('#task-parent option:checked').innerText()).trim(), epicTitle);
  await page.getByLabel('İlişki türü').selectOption('RelatesTo');
  await page.getByLabel('İlişkili görev').selectOption({ label: epicTitle });
  await page.getByRole('button', { name: 'Görev ilişkisi ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev ilişkisi eklendi.' }).waitFor();
  await page.getByRole('button', { name: 'Görev ilişkisini kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev ilişkisi kaldırıldı.' }).waitFor();
  await page.locator('#task-parent').selectOption('');
  await page.getByRole('button', { name: 'Ayrıntıları kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Görev ayrıntıları kaydedildi.' }).waitFor();
  await page.getByLabel('Yeni etiket').fill('playwright');
  await page.getByRole('button', { name: 'Etiket ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Etiket eklendi.' }).waitFor();
  await page.locator('.editable-labels').getByRole('button', { name: 'Etiketi kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Etiket kaldırıldı.' }).waitFor();
  await page.locator('.inspector-section').filter({ hasText: 'Yorumlar' }).locator('textarea').fill('Yaşam döngüsü yorumu');
  await page.getByRole('button', { name: 'Yorum ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Yorum eklendi.' }).waitFor();
  const commentRow = page.locator('.comment-row').last();
  await commentRow.getByRole('button', { name: 'Yorumu düzenle' }).click();
  await commentRow.getByLabel('Yorum metni').fill('Güncellenmiş yaşam döngüsü yorumu');
  await commentRow.getByRole('button', { name: 'Yorumu kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Yorum güncellendi.' }).waitFor();
  await page.locator('.comment-row').last().getByRole('button', { name: 'Yorumu sil' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Yorum silindi.' }).waitFor();
  await page.getByLabel('İş günlüğü saati').fill('1.5');
  await page.getByLabel('İş günlüğü notu').fill('Playwright doğrulaması');
  await page.getByRole('button', { name: 'İş günlüğü ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'İş günlüğü eklendi.' }).waitFor();
  await assertAccessibleSurface(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-task-detail.png'), fullPage: true });
  await page.getByRole('button', { name: 'Görev detayını kapat' }).click();

  await page.getByRole('button', { name: 'Kart alanlarını yapılandır' }).click();
  await page.locator('.card-config-menu').waitFor();
  await page.locator('.card-config-menu').getByText('Bitiş tarihi').click();
  await page.getByRole('button', { name: 'Kart alanlarını yapılandır' }).click();
  await page.getByRole('button', { name: 'Kolonu daralt' }).first().click();
  await page.locator('.column-lane.collapsed').first().waitFor();
  await page.getByRole('button', { name: 'Kolonu genişlet' }).first().click();

  await page.locator('.task').filter({ hasText: firstTitle }).click();
  await page.getByRole('button', { name: 'Görevi arşivle' }).click();
  await page.locator('.inspector').waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Arşiv' }).click();
  const archivedRow = page.locator('.archive-list article').filter({ hasText: firstTitle });
  await archivedRow.waitFor();
  await archivedRow.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedRow.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Pano' }).click();
  await page.locator('.task').filter({ hasText: firstTitle }).waitFor();

  const managementStamp = fixtureStamp.slice(-6);
  const teamName = `UI Ekip ${managementStamp}`;
  const renamedTeam = `${teamName} Güncel`;
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Ekip', exact: true }).click();
  await page.locator('#new-team-name').fill(teamName);
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip oluşturuldu.' }).waitFor();
  await page.getByRole('button', { name: 'Ekipler', exact: true }).click();
  await page.locator('.entity-list').getByText(teamName, { exact: true }).click();
  assert.match(page.url(), /[?&]team=/, 'Team deep link was not written');
  const teamDeepLink = page.url();
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('#team-name').waitFor();
  assert.equal(await page.locator('#team-name').inputValue(), teamName, 'Team deep link did not survive reload');
  assert.equal(page.url(), teamDeepLink, 'Team deep link changed during reload');
  await page.locator('.invite-row input[type="email"]').fill(collaborator.email);
  await page.locator('.invite-row').getByRole('button', { name: 'Davet et' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip daveti oluşturuldu.' }).waitFor();
  const invitedMember = page.locator('.member-manage-row').filter({ hasText: collaborator.email });
  const teamMemberRemoval = page.waitForResponse(response =>
    response.url().includes('/members/') && response.request().method() === 'DELETE');
  await invitedMember.getByRole('button', { name: 'Ekip üyesini kaldır' }).click();
  assert.equal((await teamMemberRemoval).status(), 200, 'Team member removal failed');
  await page.locator('.timeline').filter({ hasText: 'TeamMemberRemoved' }).waitFor();
  await page.locator('#team-name').fill(renamedTeam);
  await page.locator('.entity-detail').getByRole('button', { name: 'Kaydet', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip kaydedildi.' }).waitFor();
  await page.locator('.entity-detail').getByRole('button', { name: 'Arşivle', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Ekip arşivlendi.' }).waitFor();
  await page.getByRole('button', { name: 'Arşiv', exact: true }).click();
  const archivedTeam = page.locator('.archive-group').filter({ hasText: 'Ekipler' }).locator('article').filter({ hasText: renamedTeam });
  await archivedTeam.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedTeam.waitFor({ state: 'detached' });

  const projectName = `UI Proje ${managementStamp}`;
  const renamedProject = `${projectName} Güncel`;
  const boardName = `UI Pano ${managementStamp}`;
  const renamedBoard = `${boardName} Güncel`;
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Proje', exact: true }).click();
  await page.locator('#new-project-key').fill(`UI${managementStamp}`);
  await page.locator('#new-project-name').fill(projectName);
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje oluşturuldu.' }).waitFor();
  assert.match(page.url(), /[?&]project=/, 'Project deep link was not written');
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('#project-name').waitFor();
  assert.equal(await page.locator('#project-name').inputValue(), projectName, 'Project deep link did not survive reload');
  await page.locator('.create-context > button').click();
  await page.locator('.create-menu').getByRole('button', { name: 'Pano', exact: true }).click();
  await page.locator('#new-board-name').fill(boardName);
  await page.locator('.entity-modal').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano oluşturuldu.' }).waitFor();
  assert.match(page.url(), /[?&]board=/, 'Board deep link was not written');
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('#board-name').waitFor();
  assert.equal(await page.locator('#board-name').inputValue(), boardName, 'Board deep link did not survive reload');
  await page.locator('#board-name').fill(renamedBoard);
  await page.getByRole('button', { name: 'Panoyu kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano kaydedildi.' }).waitFor();
  await page.getByRole('button', { name: 'Panoyu arşivle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano arşivlendi.' }).waitFor();
  await page.getByRole('button', { name: 'Arşiv', exact: true }).click();
  const archivedBoard = page.locator('.archive-group').filter({ hasText: 'Panolar' }).locator('article').filter({ hasText: renamedBoard });
  await archivedBoard.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedBoard.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Projeler', exact: true }).click();
  await page.locator('#project-name').fill(renamedProject);
  await page.getByLabel('Proje üyesi').locator('option').filter({ hasText: collaborator.email }).waitFor({ state: 'attached' });
  await page.getByLabel('Proje üyesi').selectOption({ label: `${collaborator.username} · ${collaborator.email}` });
  await page.getByRole('button', { name: 'Üye ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje üyesi eklendi.' }).waitFor();
  let projectMember = page.locator('.member-manage-row').filter({ hasText: collaborator.username });
  const currentProjectRole = await projectMember.getByLabel('Proje üyesi rolü').inputValue();
  const nextProjectRole = currentProjectRole === 'Viewer' ? 'Developer' : 'Viewer';
  const projectRoleResponse = page.waitForResponse(response =>
    response.url().includes(`/members/${collaborator.id}/role`) && response.request().method() === 'PATCH');
  await projectMember.getByLabel('Proje üyesi rolü').selectOption(nextProjectRole);
  assert.equal((await projectRoleResponse).status(), 200, 'Project member role update failed');
  projectMember = page.locator('.member-manage-row').filter({ hasText: collaborator.username });
  const projectMemberRemoval = page.waitForResponse(response =>
    response.url().includes(`/members/${collaborator.id}`) && response.request().method() === 'DELETE');
  await projectMember.getByRole('button', { name: 'Proje üyesini kaldır' }).click();
  assert.equal((await projectMemberRemoval).status(), 200, 'Project member removal failed');
  await page.locator('.timeline').filter({ hasText: 'ProjectMemberRemoved' }).waitFor();
  await page.locator('.entity-detail').getByRole('button', { name: 'Kaydet', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje kaydedildi.' }).waitFor();
  await page.locator('.entity-detail').getByRole('button', { name: 'Arşivle', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Proje arşivlendi.' }).waitFor();
  await page.getByRole('button', { name: 'Arşiv', exact: true }).click();
  const archivedProject = page.locator('.archive-group').filter({ hasText: 'Projeler' }).locator('article').filter({ hasText: renamedProject });
  await archivedProject.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedProject.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Projeler', exact: true }).click();
  await page.locator(`[data-project-id="${originalProjectId}"]`).click();
  const inProgressColumn = page.locator('.configuration-row').filter({ has: page.getByLabel('Kolon adı', { exact: true }) }).nth(1);
  await inProgressColumn.locator('input[type="number"]').fill('1');
  await inProgressColumn.getByRole('button', { name: 'Kolonu kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kolon ayarları kaydedildi.' }).waitFor();
  await page.getByRole('button', { name: 'Workflow durumu ekle' }).click();
  const reviewStatus = page.locator('.workflow-row.status-row').last();
  await reviewStatus.getByLabel('Workflow durum adı').fill('UI Review');
  await reviewStatus.getByLabel('Workflow durum kategorisi').selectOption('InProgress');
  await page.getByRole('button', { name: 'Workflow geçişi ekle' }).click();
  const reviewTransition = page.locator('.workflow-row.transition-row').last();
  await reviewTransition.getByLabel('Kaynak durum').selectOption({ label: 'To Do' });
  await reviewTransition.getByLabel('Hedef durum').selectOption({ label: 'UI Review' });
  await page.getByRole('button', { name: 'Workflow geçişi ekle' }).click();
  const reviewDoneTransition = page.locator('.workflow-row.transition-row').last();
  await reviewDoneTransition.getByLabel('Kaynak durum').selectOption({ label: 'UI Review' });
  await reviewDoneTransition.getByLabel('Hedef durum').selectOption({ label: 'Done' });
  const draftSave = await page.evaluate(() => {
    const vm = window.angular.element(document.body).scope().vm;
    const apiClient = window.angular.element(document.documentElement).injector().get('apiClient');
    return apiClient.put(`/api/workflows/${vm.project.id}/draft`, {
        projectId: vm.project.id,
        statuses: vm.workflowDraft.statuses,
        transitions: vm.workflowDraft.transitions
      }).then(
        () => ({ status: 200 }),
        error => ({ status: error.status, error: error.data?.error?.message || error.message })
      );
  });
  assert.equal(draftSave.status, 200, draftSave.error || 'Workflow draft save failed');
  const newColumnForm = page.locator('form.configuration-row');
  await newColumnForm.getByLabel('Yeni kolon adı').fill('UI Review');
  await newColumnForm.getByLabel('Yeni kolon WIP limiti').fill('4');
  await newColumnForm.getByRole('button', { name: 'Kolon ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano kolonu eklendi.' }).waitFor();
  await page.getByRole('button', { name: "Workflow'u kaydet" }).click();
  await page.locator('.toast.success').filter({ hasText: 'Workflow kaydedildi.' }).waitFor();
  await page.locator('.workflow-row.transition-row').first().waitFor();
  await page.locator('.workflow-row.transition-row').nth(1).locator('input[type="checkbox"]').nth(2).check();
  await page.getByRole('button', { name: "Workflow'u kaydet" }).click();
  await page.locator('.toast.success').filter({ hasText: 'Workflow kaydedildi.' }).waitFor();
  const customColumn = page.locator('.configuration-row').filter({ has: page.getByLabel('Kolon adı', { exact: true }) }).last();
  await customColumn.getByRole('button', { name: 'Kolonu kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Pano kolonu kaldırıldı.' }).waitFor();
  await page.locator('.timeline').filter({ hasText: 'BoardColumnDeleted' }).waitFor();
  await page.locator('.timeline').filter({ hasText: 'WorkflowUpdated' }).waitFor();
  await assertNoOverflow(page);
  await assertAccessibleSurface(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-project-management.png'), fullPage: true });
  await page.getByRole('button', { name: 'Pano', exact: true }).click();
  await page.locator('.task').first().waitFor();
  const wipConflict = expectHttpResponse(409, `/api/work-items/`);
  const wipResponse = page.waitForResponse(response =>
    response.status() === 409 && response.url().includes('/api/work-items/') && response.url().endsWith('/status'));
  await page.locator('.task').filter({ hasText: epicTitle }).press('Alt+ArrowRight');
  await wipResponse;
  await page.locator('.toast.error').filter({ hasText: 'WIP limiti dolu' }).waitFor();
  assert.ok(wipConflict.seen, 'Expected WIP conflict response was not observed');
  assert.ok(await page.locator('.column-lane').first().locator('.task').filter({ hasText: epicTitle }).isVisible(), 'Optimistic WIP move was not rolled back');
  const viewName = `UI Görünüm ${managementStamp}`;
  const renamedView = `${viewName} Güncel`;
  await page.getByPlaceholder('Görünüm adı').fill(viewName);
  await page.locator('.save-view').getByRole('button', { name: 'Kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kayıtlı görünüm kaydedildi.' }).waitFor();
  await page.locator('#saved-view').selectOption({ label: viewName });
  await page.getByPlaceholder('Görünüm adı').fill(renamedView);
  await page.locator('.save-view').getByRole('button', { name: 'Kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kayıtlı görünüm kaydedildi.' }).waitFor();
  await page.locator('#saved-view').selectOption({ label: renamedView });
  await page.getByRole('button', { name: 'Kayıtlı görünümü sil' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kayıtlı görünüm silindi.' }).waitFor();

  const sprintName = `UI Sprint ${managementStamp}`;
  await page.getByRole('tab', { name: 'Liste', exact: true }).click();
  await page.locator('.work-table-row').filter({ hasText: firstTitle }).waitFor();
  await assertAccessibleSurface(page);
  await page.getByRole('tab', { name: 'Sprint', exact: true }).click();
  const sprintCreate = page.locator('.sprint-create');
  await sprintCreate.getByText('Ad', { exact: true }).locator('..').getByRole('textbox').fill(sprintName);
  await sprintCreate.getByText('Hedef', { exact: true }).locator('..').getByRole('textbox').fill('FE-006 ileri yönetim kanıtı');
  const sprintDates = sprintCreate.locator('input[type="date"]');
  const sprintStart = new Date().toISOString().slice(0, 10);
  const sprintEnd = new Date(Date.now() + 13 * 86_400_000).toISOString().slice(0, 10);
  await sprintDates.nth(0).fill(sprintStart);
  await sprintDates.nth(1).fill(sprintEnd);
  const sprintCreateResponse = page.waitForResponse(response => response.url().endsWith('/api/sprints') && response.request().method() === 'POST');
  await sprintCreate.getByRole('button', { name: 'Sprint oluştur' }).click();
  assert.equal((await sprintCreateResponse).status(), 201, 'Sprint create failed');
  await page.locator('.toast.success').filter({ hasText: 'Sprint oluşturuldu.' }).waitFor();
  await page.getByLabel('Sprint seç').selectOption({ label: `${sprintName} · Planned` });

  await page.getByRole('tab', { name: 'Backlog', exact: true }).click();
  const backlogRow = page.locator('.planning-list article').filter({ hasText: epicTitle });
  await backlogRow.waitFor();
  const sprintPlanResponse = page.waitForResponse(response => response.url().includes('/api/sprints/') && response.url().includes('/items/') && response.request().method() === 'PUT');
  await backlogRow.getByRole('button', { name: "Sprint'e al" }).click();
  assert.equal((await sprintPlanResponse).status(), 200, 'Backlog planning failed');
  await page.locator('.toast.success').filter({ hasText: 'İş sprint kapsamına alındı.' }).waitFor();

  await page.getByRole('tab', { name: 'Sprint', exact: true }).click();
  await page.getByLabel('Sprint seç').selectOption({ label: `${sprintName} · Planned` });
  await page.locator('.planning-list').getByText(epicTitle, { exact: true }).waitFor();
  const sprintStartResponse = page.waitForResponse(response => response.url().endsWith('/start') && response.request().method() === 'POST');
  await page.getByRole('button', { name: "Sprint'i başlat" }).click();
  assert.equal((await sprintStartResponse).status(), 200, 'Sprint start failed');
  await page.locator('.toast.success').filter({ hasText: 'Sprint başlatıldı.' }).waitFor();
  const sprintCompleteResponse = page.waitForResponse(response => response.url().endsWith('/complete') && response.request().method() === 'POST');
  await page.getByRole('button', { name: "Sprint'i tamamla" }).click();
  assert.equal((await sprintCompleteResponse).status(), 200, 'Sprint completion failed');
  await page.locator('.toast.success').filter({ hasText: 'Sprint tamamlandı.' }).waitFor();

  await page.getByRole('tab', { name: 'Takvim', exact: true }).click();
  await page.locator('.calendar-grid').getByText(firstTitle, { exact: true }).waitFor();
  await page.getByRole('tab', { name: 'Yol haritası', exact: true }).click();
  await page.locator('.roadmap-list').filter({ hasText: sprintName }).getByText('Completed', { exact: true }).waitFor();
  await page.setViewportSize({ width: 720, height: 1000 });
  await assertNoOverflow(page);
  await assertAccessibleSurface(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-advanced-responsive.png'), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole('tab', { name: 'Zaman çizelgesi', exact: true }).click();
  const timelineEntry = page.locator('.project-timeline li').first();
  await timelineEntry.waitFor();
  assert.match(await timelineEntry.innerText(), /(Project|Board|Sprint|WorkItem)/, 'Timeline did not expose an authorized entity audit record');
  await assertNoOverflow(page);
  await assertAccessibleSurface(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-advanced-work.png'), fullPage: true });
  await page.getByRole('tab', { name: 'Pano', exact: true }).click();
  await page.locator('.task').filter({ hasText: firstTitle }).click();
  await page.getByRole('button', { name: 'Onay iste' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Geçiş onayı istendi.' }).waitFor();
  await page.locator('.approval-row').filter({ hasText: 'Pending' }).waitFor();
  await page.getByRole('button', { name: 'Görev detayını kapat' }).click();

  await page.getByRole('button', { name: 'Ayarlar', exact: true }).click();
  await page.locator('.settings-view[data-settings-ready="true"]').waitFor();
  await page.getByRole('tab', { name: 'Organizasyon' }).click();
  await page.getByLabel('Organizasyon adı').fill('UI Organizasyonu');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await page.locator('.toast.success').filter({ hasText: 'Organizasyon kaydedildi.' }).waitFor();
  await page.getByLabel('Yeni departman adı').fill('Platform');
  await page.getByRole('button', { name: 'Departman ekle' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Departman eklendi.' }).waitFor();
  await page.getByLabel('Departman', { exact: true }).selectOption({ label: 'Platform' });
  await page.getByLabel('Departman üyesi').selectOption({ label: `${collaborator.username} · ${collaborator.email}` });
  await page.getByLabel('Departman pozisyonu').fill('Developer');
  await page.getByRole('button', { name: 'Üye ata' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Departman üyesi atandı.' }).waitFor();
  const departmentMemberRemoval = page.waitForResponse(response =>
    response.url().includes('/departments/') && response.url().includes('/members/') && response.request().method() === 'DELETE');
  await page.getByRole('button', { name: 'Departman üyesini kaldır' }).click();
  assert.equal((await departmentMemberRemoval).status(), 200, 'Department member removal failed');
  await page.locator('.timeline').filter({ hasText: 'DepartmentMemberRemoved' }).waitFor();
  await page.getByRole('tab', { name: 'Rol ve izinler' }).click();
  const roleName = `UI Reviewer ${managementStamp}`;
  const renamedRole = `${roleName} Lead`;
  const roleCreateForm = page.locator('.role-create-form');
  await roleCreateForm.getByLabel('Yeni rol adı').fill(roleName);
  await roleCreateForm.getByText('AuditReadAll', { exact: true }).click();
  await roleCreateForm.getByRole('button', { name: 'Rol oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Özel rol oluşturuldu.' }).waitFor();
  let roleRow = page.locator(`.role-definition[data-role-name="${roleName}"]`);
  await roleRow.getByLabel('Rol adı').fill(renamedRole);
  await roleRow.getByText('BoardView', { exact: true }).click();
  await roleRow.getByRole('button', { name: 'Rolü kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Rol güncellendi.' }).waitFor();
  const collaboratorRoleRow = page.locator('.user-role-row').filter({ hasText: collaborator.email });
  await collaboratorRoleRow.getByText(renamedRole, { exact: true }).click();
  await collaboratorRoleRow.getByRole('button', { name: 'Rolleri kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kullanıcı rolleri güncellendi.' }).waitFor();
  await collaboratorRoleRow.getByText(renamedRole, { exact: true }).click();
  await collaboratorRoleRow.getByRole('button', { name: 'Rolleri kaydet' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Kullanıcı rolleri güncellendi.' }).waitFor();
  roleRow = page.locator(`.role-definition[data-role-name="${renamedRole}"]`);
  await roleRow.getByRole('button', { name: 'Rolü kaldır' }).click();
  await page.locator('.toast.success').filter({ hasText: 'Rol kaldırıldı.' }).waitFor();
  await roleRow.waitFor({ state: 'detached' });
  await assertNoOverflow(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-access-management.png'), fullPage: true });
  await page.getByRole('tab', { name: 'Hesap ve güvenlik' }).click();
  await page.getByLabel('API anahtarı adı').fill('Playwright');
  await page.getByLabel('API anahtarı parolası').fill(desktopDemoPassword);
  await page.locator('.api-key-form').getByRole('button', { name: 'Oluştur' }).click();
  await page.locator('.toast.success').filter({ hasText: 'API anahtarı oluşturuldu' }).waitFor();
  assert.match(await page.locator('.secret-output').filter({ hasText: 'Yeni API anahtarı' }).locator('code').innerText(), /^zmb_/);
  await page.locator('.settings-band').filter({ hasText: 'API anahtarları' }).getByRole('button', { name: 'API anahtarını iptal et' }).click();
  await page.locator('.toast.success').filter({ hasText: 'API anahtarı iptal edildi.' }).waitFor();
  await page.locator('.secret-output').filter({ hasText: 'Yeni API anahtarı' }).waitFor({ state: 'detached' });
  await assertNoOverflow(page);
  await assertAccessibleSurface(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-settings.png'), fullPage: true });
  await page.getByRole('button', { name: 'Pano', exact: true }).click();
  await page.locator('.task').first().waitFor();

  await page.keyboard.press('Control+K');
  await page.locator('.command-palette').waitFor();
  await page.waitForTimeout(250);
  await page.screenshot({ path: resolve(outputDir, 'desktop-command.png') });
  await page.keyboard.press('Escape');
  await page.locator('.command-palette').waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Temayı değiştir', exact: true }).click();
  await page.locator('body.theme-dark').waitFor();
  await assertTextContrast(page);
  await page.screenshot({ path: resolve(outputDir, 'desktop-dark.png'), fullPage: true });

  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('body.theme-dark').waitFor();
  await assertBrowserSecretsAreInaccessible(page);
  const ownerAccessToken = elevatedSession.data.accessToken;
  const viewerGrant = await apiRequest(`/api/projects/${originalProjectId}/members`, 'POST', {
    userId: collaborator.id,
    role: 'Viewer'
  }, ownerAccessToken);
  assert.ok(viewerGrant.response.ok, viewerGrant.payload.error?.message || 'Viewer project grant failed');
  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  await browserContextLogin(viewerContext, collaborator.username, 'P@ssword123');
  const viewerPage = await viewerContext.newPage();
  await attachDiagnostics(viewerPage, 'viewer-permission');
  await viewerPage.goto(`${baseUrl}/desktop-bulma/index.html#section=projects&project=${originalProjectId}`, { waitUntil: 'networkidle' });
  await assertBrowserSecretsAreInaccessible(viewerPage);
  await viewerPage.getByText('Viewer rolüyle bu proje salt okunur görüntüleniyor.').waitFor();
  assert.ok(await viewerPage.locator('#project-name').isDisabled(), 'Viewer could edit the project name');
  assert.equal(await viewerPage.locator('.entity-detail').getByRole('button', { name: 'Kaydet', exact: true }).count(), 0, 'Viewer received a project save command');
  await viewerPage.getByRole('button', { name: 'Pano', exact: true }).click();
  await viewerPage.getByRole('tab', { name: 'Sprint', exact: true }).click();
  assert.equal(await viewerPage.locator('.sprint-create').count(), 0, 'Viewer received sprint create controls');
  assert.equal(await viewerPage.getByRole('button', { name: /Sprint'i (başlat|tamamla)/ }).count(), 0, 'Viewer received sprint lifecycle controls');
  const viewerRemoval = await apiRequest(`/api/projects/${originalProjectId}/members/${collaborator.id}`, 'DELETE', undefined, ownerAccessToken);
  assert.ok(viewerRemoval.response.ok, viewerRemoval.payload.error?.message || 'Viewer project removal failed');
  await viewerPage.reload({ waitUntil: 'networkidle' });
  await viewerPage.getByText('pano ve iş öğelerine erişmek için proje üyeliği gerekir').waitFor();
  assert.equal(await viewerPage.locator('.board-management button').count(), 0, 'Removed member retained board access');
  await viewerContext.close();
  await page.getByRole('button', { name: 'Çıkış', exact: true }).click();
  await page.locator('.desktop-login').waitFor();
  assert.equal(await page.evaluate(() => localStorage.getItem('zumbo.currentUser')), null, 'Logout retained the browser user state');
  assert.equal(await page.evaluate(() => sessionStorage.getItem('zumbo.csrfToken')), null, 'Logout retained the CSRF token');
  const remainingAuthCookies = (await desktop.cookies(apiBaseUrl))
    .filter(cookie => ['zumbo-access', 'zumbo-refresh', 'zumbo-csrf'].includes(cookie.name));
  assert.deepEqual(remainingAuthCookies, [], 'Logout retained browser session cookies');

  const reflectedXss = '<img data-zumbo-reflected-xss src=x onerror="window.__zumboReflectedXss=1">';
  await page.locator('.desktop-login input').nth(0).fill(reflectedXss);
  await page.locator('.desktop-login input').nth(1).fill('invalid-password');
  const expectedRejectedLogin = expectHttpResponse(401, '/api/browser-auth/login');
  await page.locator('.desktop-login').getByRole('button', { name: 'Giriş yap' }).click();
  await page.locator('.login-error').filter({ hasText: 'Giriş başarısız.' }).waitFor();
  assert.ok(expectedRejectedLogin.seen, 'Reflected XSS probe did not reach the login error contract');
  assert.equal(await page.locator('[data-zumbo-reflected-xss]').count(), 0, 'Reflected XSS payload created a DOM node');
  assert.equal(await page.evaluate(() => window.__zumboReflectedXss), undefined, 'Reflected XSS payload executed');

  const domXss = '<img data-zumbo-dom-xss src=x onerror="window.__zumboDomXss=1">';
  await page.evaluate(payload => { location.hash = `section=${encodeURIComponent(payload)}`; }, domXss);
  await page.waitForTimeout(150);
  assert.equal(await page.locator('[data-zumbo-dom-xss]').count(), 0, 'DOM XSS payload created a DOM node');
  assert.equal(await page.evaluate(() => window.__zumboDomXss), undefined, 'DOM XSS payload executed');
  await desktop.close();

  const offlineShell = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const offlinePage = await offlineShell.newPage();
  await offlinePage.goto(`${baseUrl}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await assertPwa(offlinePage);
  await offlinePage.reload({ waitUntil: 'networkidle' });
  await offlineShell.setOffline(true);
  await offlinePage.reload({ waitUntil: 'domcontentloaded' });
  assert.equal(await offlinePage.title(), 'Zumbo Desktop');
  await offlineShell.setOffline(false);
  await offlineShell.close();

  const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, colorScheme: 'light' });
  const mobilePage = await mobile.newPage();
  await attachDiagnostics(mobilePage, 'mobile');
  await routeDemoRegistration(mobilePage, runContext.tenants.mobile, 'mobile');
  await mobilePage.goto(`${baseUrl}/mobile-ionic/index.html`, { waitUntil: 'networkidle' });
  const mobileRegistrationRequest = mobilePage.waitForRequest(request =>
    request.method() === 'POST' && request.url().endsWith('/api/browser-auth/register'));
  await mobilePage.getByRole('button', { name: 'Demo kullanıcı oluştur' }).click();
  const mobileDemoPassword = (await mobileRegistrationRequest).postDataJSON().password;
  await mobilePage.locator('.metric-band').waitFor({ timeout: 30_000 });
  await assertBrowserSecretsAreInaccessible(mobilePage);
  const mobileWorkspaceUser = await mobilePage.evaluate(() => JSON.parse(localStorage.getItem('zumbo.currentUser')));
  assert.equal(mobileWorkspaceUser.organizationId, runContext.tenants.mobile, 'Mobile fixture escaped its controlled tenant');
  const mobileCollaboratorRegistration = await apiRequest('/api/auth/register', 'POST', {
    username: `mobilecollab${fixtureStamp}`,
    email: `mobilecollab${fixtureStamp}@zumbo.local`,
    password: 'P@ssword123',
    organizationId: mobileWorkspaceUser.organizationId
  });
  assert.ok(mobileCollaboratorRegistration.response.ok, mobileCollaboratorRegistration.payload.error?.message || 'Mobile collaborator registration failed');
  const mobileCollaborator = mobileCollaboratorRegistration.data.user;
  const mobileBearerSession = await apiRequest('/api/auth/login', 'POST', {
    usernameOrEmail: mobileWorkspaceUser.username,
    password: mobileDemoPassword
  });
  assert.ok(mobileBearerSession.response.ok, mobileBearerSession.payload.error?.message || 'Mobile bearer login failed');
  await assertNoOverflow(mobilePage);
  await assertPwa(mobilePage);
  await assertAccessibleSurface(mobilePage);
  await assertTextContrast(mobilePage);
  await assertVisibleFocus(mobilePage);
  await assertAccessibilityPreferences(mobilePage, 'mobile-forced-colors.png');
  await assertZoomReflow(mobilePage, 'mobile-zoom-200.png');
  const mobileVisibleText = await mobilePage.locator('body').innerText();
  assert.doesNotMatch(mobileVisibleText, new RegExp(mobileWorkspaceUser.organizationId), 'Mobile exposed the opaque organization identifier');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-light.png'), fullPage: true });
  await mobilePage.locator('a.tab-item').filter({ hasText: 'Görevlerim' }).click();
  await mobilePage.waitForURL(/#\/app\/tasks$/);
  const originalMobileTask = mobilePage.locator('.task-row:visible').first();
  await originalMobileTask.waitFor();
  const originalMobileTaskTitle = (await originalMobileTask.locator('h2').innerText()).trim();
  const mobileListTab = mobilePage.getByRole('tab', { name: 'Liste' });
  await mobileListTab.click();
  await mobilePage.locator('.task-row:visible').filter({ hasText: originalMobileTaskTitle }).waitFor();
  await mobileListTab.focus();
  await mobileListTab.press('ArrowLeft');
  assert.equal(await mobilePage.getByRole('tab', { name: 'Pano' }).getAttribute('aria-selected'), 'true');
  await mobilePage.locator('.mobile-board-lane:visible').first().waitFor();
  await mobilePage.locator('.mobile-board-task:visible').filter({ hasText: originalMobileTaskTitle }).waitFor();
  await assertNoOverflow(mobilePage);
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-work-modes.png'), fullPage: true });
  await mobilePage.getByRole('tab', { name: 'Backlog' }).click();
  await mobilePage.locator('.task-row:visible').filter({ hasText: originalMobileTaskTitle }).waitFor();
  await mobilePage.getByRole('tab', { name: 'Sprint' }).click();
  await mobilePage.getByText('Bu sprintte iş yok.').waitFor();
  await mobilePage.getByRole('tab', { name: 'Benim' }).click();
  await mobilePage.locator('.task-row:visible').filter({ hasText: originalMobileTaskTitle }).click();
  await mobilePage.waitForURL(/#\/tasks\//);
  const updatedMobileTaskTitle = `${originalMobileTaskTitle} Mobil`;
  await mobilePage.getByLabel('Mobil görev başlığı').fill(updatedMobileTaskTitle);
  const mobileTaskSave = mobilePage.waitForResponse(response =>
    response.url().includes('/api/work-items/') && response.request().method() === 'PUT');
  await mobilePage.getByRole('button', { name: 'Görevi kaydet' }).click();
  assert.equal((await mobileTaskSave).status(), 200, 'Mobile task update failed');
  await mobilePage.getByRole('heading', { name: updatedMobileTaskTitle }).waitFor();
  await mobilePage.getByLabel('Mobil çalışma saati').fill('1.25');
  await mobilePage.getByLabel('Mobil çalışma notu').fill('Mobil parity');
  const mobileWorkLogSave = mobilePage.waitForResponse(response =>
    response.url().endsWith('/worklogs') && response.request().method() === 'POST');
  await mobilePage.getByRole('button', { name: 'Çalışma kaydı ekle' }).click();
  assert.equal((await mobileWorkLogSave).status(), 200, 'Mobile worklog creation failed');
  await mobilePage.locator('.mobile-worklog').filter({ hasText: 'Mobil parity' }).waitFor();
  await assertNoOverflow(mobilePage);
  await assertAccessibleSurface(mobilePage);
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-task-worklog.png'), fullPage: true });
  await mobilePage.goto(`${baseUrl}/mobile-ionic/index.html#/app/projects`, { waitUntil: 'networkidle' });
  await mobilePage.locator('a.tab-item').filter({ hasText: 'Çalışma' }).click();
  await mobilePage.waitForURL(/#\/app\/projects$/);
  const mobileStamp = String(Date.now()).slice(-6);
  const mobileTeamName = `Mobil Ekip ${mobileStamp}`;
  const renamedMobileTeam = `${mobileTeamName} Güncel`;
  await mobilePage.locator('.workspace-segments').getByText('Ekipler', { exact: true }).click();
  await mobilePage.getByLabel('Yeni mobil ekip adı').fill(mobileTeamName);
  await mobilePage.getByRole('button', { name: 'Ekip oluştur' }).click();
  const mobileTeamItem = mobilePage.locator('ion-item').filter({ hasText: mobileTeamName });
  await mobileTeamItem.waitFor();
  await mobileTeamItem.click();
  await mobilePage.waitForURL(/#\/teams\//);
  const mobileTeamId = mobilePage.url().split('/teams/')[1].split(/[?#/]/)[0];
  const mobileAccessToken = mobileBearerSession.data.accessToken;
  const mobileTeamsBeforeConflict = await apiRequest(
    `/api/teams?organizationId=${encodeURIComponent(mobileCollaborator.organizationId)}`,
    'GET',
    undefined,
    mobileAccessToken);
  const mobileTeamBeforeConflict = mobileTeamsBeforeConflict.data.find(team => team.id === mobileTeamId);
  assert.ok(mobileTeamBeforeConflict?.version > 0, 'Mobile team did not expose a concurrency version');
  const externalTeamName = `${mobileTeamName} Dış Güncelleme`;
  const externalTeamUpdate = await apiRequest(
    `/api/teams/${mobileTeamId}`,
    'PUT',
    { name: externalTeamName },
    mobileAccessToken,
    { 'If-Match': `"${mobileTeamBeforeConflict.version}"` });
  assert.ok(externalTeamUpdate.response.ok, externalTeamUpdate.payload.error?.message || 'External team update failed');

  await mobilePage.getByLabel('Mobil ekip adı', { exact: true }).fill(renamedMobileTeam);
  const expectedMobileConflict = expectHttpResponse(409, `/api/teams/${mobileTeamId}`);
  const staleMobileMutation = mobilePage.waitForRequest(request =>
    request.method() === 'PUT' && request.url().endsWith(`/api/teams/${mobileTeamId}`));
  await mobilePage.getByRole('button', { name: 'Ekibi kaydet' }).click();
  assert.equal(
    (await staleMobileMutation).headers()['if-match'],
    `"${mobileTeamBeforeConflict.version}"`,
    'Mobile stale mutation did not send its loaded version');
  await mobilePage.locator('.mobile-feedback.error').filter({ hasText: 'başka bir kullanıcı tarafından değiştirildi' }).waitFor();
  assert.ok(expectedMobileConflict.seen, 'Mobile stale mutation did not receive the expected HTTP 409');
  await mobilePage.getByLabel('Mobil ekip adı', { exact: true }).waitFor();
  await mobilePage.waitForFunction(
    name => document.querySelector('[aria-label="Mobil ekip adı"]')?.value === name,
    externalTeamName);

  await mobilePage.getByLabel('Mobil ekip adı', { exact: true }).fill(renamedMobileTeam);
  const mobileTeamSave = mobilePage.waitForResponse(response => response.url().includes('/api/teams/') && response.request().method() === 'PUT');
  await mobilePage.getByRole('button', { name: 'Ekibi kaydet' }).click();
  assert.equal((await mobileTeamSave).status(), 200, 'Mobile team update failed');
  await mobilePage.getByLabel('Mobil ekip davet e-postası').fill(mobileCollaborator.email);
  await mobilePage.getByRole('button', { name: 'Davet et' }).click();
  const mobileInvitedMember = mobilePage.locator('.mobile-member-row').filter({ hasText: mobileCollaborator.email });
  await mobileInvitedMember.waitFor();
  await mobilePage.locator('[data-team-saving="false"]').waitFor();
  await mobileInvitedMember.getByRole('button', { name: 'Mobil ekip üyesini kaldır' }).click();
  await mobileInvitedMember.waitFor({ state: 'detached' });
  await mobilePage.getByRole('button', { name: 'Arşivle' }).click();
  await mobilePage.waitForURL(/#\/app\/projects$/);
  await mobilePage.locator('.workspace-segments').getByText('Arşiv', { exact: true }).click();
  const archivedMobileTeam = mobilePage.locator('.mobile-archive-row').filter({ hasText: renamedMobileTeam });
  await archivedMobileTeam.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedMobileTeam.waitFor({ state: 'detached' });

  const mobileProjectName = `Mobil Proje ${mobileStamp}`;
  const renamedMobileProject = `${mobileProjectName} Güncel`;
  const mobileBoardName = `Mobil Pano ${mobileStamp}`;
  await mobilePage.locator('.workspace-segments').getByText('Projeler', { exact: true }).click();
  await mobilePage.getByLabel('Yeni mobil proje anahtarı').fill(`M${mobileStamp}`);
  await mobilePage.getByLabel('Yeni mobil proje adı').fill(mobileProjectName);
  await mobilePage.getByRole('button', { name: 'Proje oluştur' }).click();
  const mobileProjectItem = mobilePage.locator('ion-item').filter({ hasText: mobileProjectName });
  await mobileProjectItem.waitFor();
  await mobileProjectItem.click();
  await mobilePage.waitForURL(/#\/projects\//);
  const mobileProjectMemberSelect = mobilePage.getByLabel('Yeni mobil proje üyesi', { exact: true });
  await mobileProjectMemberSelect.locator('option').filter({ hasText: mobileCollaborator.email }).waitFor({ state: 'attached' });
  await mobileProjectMemberSelect.selectOption({ label: `${mobileCollaborator.username} · ${mobileCollaborator.email}` });
  await mobilePage.getByLabel('Yeni mobil proje üyesi rolü', { exact: true }).selectOption('Developer');
  await mobilePage.getByRole('button', { name: 'Üye ekle' }).click();
  let mobileProjectMember = mobilePage.locator('.mobile-member-row').filter({ hasText: mobileCollaborator.email });
  await mobileProjectMember.waitFor();
  await mobileProjectMember.getByLabel('Mobil proje üyesi rolü', { exact: true }).selectOption('Viewer');
  const mobileProjectRoleSave = mobilePage.waitForResponse(response =>
    response.url().includes('/members/') && response.url().endsWith('/role') && response.request().method() === 'PATCH');
  await mobileProjectMember.getByRole('button', { name: 'Mobil proje üyesi rolünü kaydet' }).click();
  assert.equal((await mobileProjectRoleSave).status(), 200, 'Mobile project member role update failed');
  await mobilePage.locator('[data-project-saving="false"]').waitFor();
  mobileProjectMember = mobilePage.locator('.mobile-member-row').filter({ hasText: mobileCollaborator.email });
  const mobileProjectMemberRemoval = mobilePage.waitForResponse(response =>
    response.url().includes(`/members/${mobileCollaborator.id}`) && response.request().method() === 'DELETE');
  await mobileProjectMember.getByRole('button', { name: 'Mobil proje üyesini kaldır' }).click();
  assert.equal((await mobileProjectMemberRemoval).status(), 200, 'Mobile project member removal failed');
  await mobileProjectMember.waitFor({ state: 'detached' });
  await mobilePage.getByLabel('Mobil proje adı', { exact: true }).fill(renamedMobileProject);
  await mobilePage.getByRole('button', { name: 'Projeyi kaydet' }).click();
  await mobilePage.getByLabel('Yeni mobil pano adı').fill(mobileBoardName);
  await mobilePage.getByRole('button', { name: 'Pano oluştur' }).click();
  let mobileBoardRow = mobilePage.locator('.mobile-entity-row').filter({ has: mobilePage.getByLabel('Mobil pano adı', { exact: true }) }).first();
  await mobilePage.locator('[data-project-saving="false"]').waitFor();
  await mobileBoardRow.getByLabel('Mobil pano adı', { exact: true }).fill(`${mobileBoardName} Güncel`);
  await mobileBoardRow.getByRole('button', { name: 'Kaydet' }).click();
  mobileBoardRow = mobilePage.locator('.mobile-entity-row').filter({ has: mobilePage.getByLabel('Mobil pano adı', { exact: true }) }).last();
  await mobileBoardRow.getByRole('button', { name: 'Arşivle' }).click();
  const archivedMobileBoard = mobilePage.locator('.mobile-archive-row').filter({ hasText: `${mobileBoardName} Güncel` });
  await archivedMobileBoard.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedMobileBoard.waitFor({ state: 'detached' });
  await assertNoOverflow(mobilePage);
  await assertAccessibleSurface(mobilePage);
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-management.png'), fullPage: true });
  await mobilePage.getByRole('button', { name: 'Arşivle' }).first().click();
  await mobilePage.waitForURL(/#\/app\/projects$/);
  await mobilePage.locator('.workspace-segments').getByText('Arşiv', { exact: true }).click();
  const archivedMobileProject = mobilePage.locator('.mobile-archive-row').filter({ hasText: renamedMobileProject });
  await archivedMobileProject.getByRole('button', { name: 'Geri yükle' }).click();
  await archivedMobileProject.waitFor({ state: 'detached' });
  await mobilePage.locator('a.tab-item').filter({ hasText: 'Profil' }).click();
  await mobilePage.waitForURL(/#\/app\/profile$/);
  await mobilePage.getByRole('button', { name: 'Temayı değiştir' }).click();
  await mobilePage.waitForFunction(() => document.body.classList.contains('theme-dark'));
  await mobilePage.locator('.profile img').waitFor();
  await mobilePage.getByRole('button', { name: 'Çıkış yap' }).waitFor();
  await mobilePage.waitForTimeout(350);
  await assertTextContrast(mobilePage);
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-dark.png'), fullPage: true });
  await mobile.close();

  const mobileOfflineShell = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  const mobileOfflinePage = await mobileOfflineShell.newPage();
  const mobileOfflineErrors = [];
  mobileOfflinePage.on('pageerror', error => mobileOfflineErrors.push(error.message));
  mobileOfflinePage.on('console', message => {
    if (message.type() === 'error' && !message.text().includes('ERR_INTERNET_DISCONNECTED')) {
      mobileOfflineErrors.push(message.text());
    }
  });
  await mobileOfflinePage.goto(`${baseUrl}/mobile-ionic/index.html`, { waitUntil: 'networkidle' });
  await assertPwa(mobileOfflinePage);
  await mobileOfflinePage.reload({ waitUntil: 'networkidle' });
  const mobileCachedUrls = await cachedUrls(mobileOfflinePage);
  assert.ok(mobileCachedUrls.some(url => url.includes('ionic.bundle.min.js')), 'Ionic runtime was not cached');
  assert.ok(mobileCachedUrls.some(url => url.includes('ionic.min.css')), 'Ionic stylesheet was not cached');
  await mobileOfflineShell.setOffline(true);
  await mobileOfflinePage.reload({ waitUntil: 'domcontentloaded' });
  await mobileOfflinePage.waitForTimeout(2_000);
  assert.ok(
    await mobileOfflinePage.getByRole('button', { name: 'Demo kullanıcı oluştur' }).isVisible(),
    `Mobile offline shell did not render: ${mobileOfflineErrors.join(' | ')}`
  );
  await mobileOfflinePage.locator('.mobile-pwa-state.offline').waitFor();
  await assertNoOverflow(mobileOfflinePage);
  await mobileOfflineShell.setOffline(false);
  await mobileOfflineShell.close();
} catch (error) {
  if (diagnosticPage && !diagnosticPage.isClosed()) {
    await diagnosticPage.screenshot({ path: resolve(outputDir, 'failure.png'), fullPage: true }).catch(() => {});
    const state = await diagnosticPage.evaluate(() => ({
      url: location.href,
      lanes: Array.from(document.querySelectorAll('.column-lane')).map(lane => lane.innerText),
      toasts: Array.from(document.querySelectorAll('.toast')).map(toast => toast.innerText),
      feedback: document.querySelector('.status-banner')?.innerText || null
    })).catch(() => null);
    const stateDetail = `UI state: ${JSON.stringify(state)}`;
    error.message += `\n${stateDetail}`;
    error.stack += `\n${stateDetail}`;
  }
  if (failures.length) {
    const failureDetail = `Diagnostics: ${failures.join(' | ')}`;
    error.message += `\n${failureDetail}`;
    error.stack += `\n${failureDetail}`;
  }
  throw error;
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser.close();
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.filter(result => !result.passed).map(result => `${result.key}: ${result.error}`).join(' | ')}`);
assert.deepEqual(failures, [], failures.join('\n'));
writeMachineResult({ passed: true, checks: ['desktop', 'mobile', 'themes', 'command', 'keyboard', 'deep-link', 'team-deep-link-reload', 'project-deep-link-reload', 'board-deep-link-reload', 'permission-loss-state', 'optimistic-concurrency-conflict', 'card-config', 'column-collapse', 'column-management', 'wip-conflict-rollback', 'workflow-management', 'saved-view-lifecycle', 'task-lifecycle', 'task-hierarchy-lifecycle', 'task-relation-lifecycle', 'task-approval-request', 'comment-lifecycle', 'label-lifecycle', 'worklog-lifecycle', 'team-lifecycle', 'team-invite-lifecycle', 'project-lifecycle', 'project-member-lifecycle', 'board-lifecycle', 'audit-timeline', 'organization-lifecycle', 'department-member-lifecycle', 'role-permission-lifecycle', 'mobile-team-lifecycle', 'mobile-team-invite-lifecycle', 'mobile-project-lifecycle', 'mobile-project-member-lifecycle', 'mobile-board-lifecycle', 'mobile-work-modes', 'mobile-task-edit', 'mobile-worklog', 'mobile-offline-state', 'accessibility-names-landmarks', 'keyboard-visible-focus', 'zoom-200-reflow', 'reduced-motion', 'forced-colors', 'display-name-resolution', 'semantic-token-contrast', 'advanced-work-modes', 'sprint-lifecycle', 'backlog-planning', 'calendar-view', 'project-timeline', 'roadmap-view', 'advanced-responsive', 'advanced-permission-boundary', 'api-key-lifecycle', 'browser-cookie-session', 'http-only-token-isolation', 'stored-xss', 'reflected-xss', 'dom-xss', 'session-revocation', 'pwa'] });
console.log(`UI quality checks passed on ${browserName}: desktop/mobile, themes, keyboard, lifecycle, board configuration and PWA shell.`);
