import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { buildFrontend } from './build-frontend.mjs';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';
import { startStaticServer } from './static-server.mjs';

const password = 'P@ssword123';
const stamp = Date.now().toString(36);
const email = `fe002-${stamp}@zumbo.local`;
const organizationId = `fe002-org-${stamp}`;
const outputDirectory = resolve(import.meta.dirname, '../../artifacts/runtime/fe002-browser');
await mkdir(outputDirectory, { recursive: true });
await buildFrontend();

async function api(path, method = 'GET', body, token) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json();
  assert.ok(response.ok, payload.error?.message || `${method} ${path} failed with ${response.status}`);
  return payload.data;
}

const owner = await api('/api/auth/register', 'POST', {
  username: `fe002-${stamp}`,
  email,
  password,
  organizationId
});
await api('/api/organizations', 'POST', {
  name: `FE-002 browser organization ${stamp}`,
  tenantKey: organizationId
}, owner.accessToken);
const project = await api('/api/projects', 'POST', {
  organizationId,
  key: `F${stamp.slice(-6).toUpperCase()}`,
  name: `Shared client ${stamp}`,
  ownerUserId: owner.user.id
}, owner.accessToken);
await api('/api/boards', 'POST', {
  projectId: project.id,
  name: 'Shared client board',
  type: 'Kanban'
}, owner.accessToken);

const frontendUrl = new URL(frontendBaseUrl);
const staticServer = await startStaticServer(resolve(import.meta.dirname, '../dist'), {
  host: frontendUrl.hostname,
  port: Number(frontendUrl.port)
});
const browser = await chromium.launch({
  headless: true,
  ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {})
});
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();
const failures = [];
const refreshRequests = [];
let desktopHorizontalOverflow = null;
let mobileHorizontalOverflow = null;
page.on('pageerror', error => failures.push(`page: ${error.message}`));
page.on('console', message => {
  if (message.type() === 'error' && !message.text().includes('Failed to load resource')) {
    failures.push(`console: ${message.text()}`);
  }
});
page.on('request', request => {
  if (request.url() === `${apiBaseUrl}/api/browser-auth/refresh`) refreshRequests.push(request.url());
});

const corsHeaders = {
  'Access-Control-Allow-Origin': staticServer.origin,
  'Access-Control-Allow-Credentials': 'true',
  'Access-Control-Allow-Headers': 'Content-Type, X-CSRF-Token, X-Correlation-Id, Idempotency-Key',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Content-Type': 'application/json'
};
function envelope(data, correlationId = 'fe002-browser') {
  return JSON.stringify({ success: true, data, error: null, correlationId });
}
function failureEnvelope(status, code, message, extra) {
  return JSON.stringify({
    success: false,
    data: null,
    error: { code, message },
    correlationId: `fe002-${status}`,
    ...(extra || {})
  });
}
async function fulfillPreflight(route) {
  if (route.request().method() !== 'OPTIONS') return false;
  await route.fulfill({ status: 204, headers: corsHeaders, body: '' });
  return true;
}
async function angularApi(expression, argument) {
  return page.evaluate(async ({ expression, argument }) => {
    const injector = window.angular.element(document.body).injector();
    const client = injector.get('apiClient');
    return Function('client', 'argument', `return (${expression});`)(client, argument);
  }, { expression, argument });
}

try {
  await page.goto(`${staticServer.origin}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await page.locator('input[autocomplete="username"]').fill(email);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.locator('form').getByRole('button', { name: /giri/i }).click();
  await page.locator('.side-nav').waitFor({ state: 'visible' });
  await page.locator('.board-empty, .board-shell').first().waitFor({ state: 'visible' });

  await page.route('**/api/browser-auth/refresh', async route => {
    await new Promise(resolveWait => setTimeout(resolveWait, 200));
    await route.continue();
  });

  const safeCalls = { organizations: [], projects: [] };
  let organizationsFirst = true;
  let projectsFirst = true;
  await page.route('**/api/organizations', async route => {
    if (await fulfillPreflight(route)) return;
    safeCalls.organizations.push(route.request().headers()['x-correlation-id']);
    if (organizationsFirst) {
      organizationsFirst = false;
      await route.fulfill({
        status: 401,
        headers: corsHeaders,
        body: failureEnvelope(401, 'AUTHENTICATION_REQUIRED', 'Refresh required.')
      });
      return;
    }
    await route.continue();
  });
  await page.route('**/api/projects?*', async route => {
    if (await fulfillPreflight(route)) return;
    safeCalls.projects.push(route.request().headers()['x-correlation-id']);
    if (projectsFirst) {
      projectsFirst = false;
      await route.fulfill({
        status: 401,
        headers: corsHeaders,
        body: failureEnvelope(401, 'AUTHENTICATION_REQUIRED', 'Refresh required.')
      });
      return;
    }
    await route.continue();
  });

  const refreshBeforeSafe = refreshRequests.length;
  const safeResult = await angularApi(`Promise.all([
    client.get('/api/organizations'),
    client.get('/api/projects?organizationId=' + encodeURIComponent(argument))
  ]).then(function(values) { return { organizations: values[0].length, projects: values[1].length }; })`, organizationId);
  assert.ok(safeResult.organizations >= 1);
  assert.ok(safeResult.projects >= 1);
  assert.equal(refreshRequests.length - refreshBeforeSafe, 1);
  assert.equal(safeCalls.organizations.length, 2);
  assert.equal(safeCalls.projects.length, 2);
  assert.ok(safeCalls.organizations[0]);
  assert.equal(safeCalls.organizations[0], safeCalls.organizations[1]);
  assert.equal(safeCalls.projects[0], safeCalls.projects[1]);
  await page.unroute('**/api/organizations');
  await page.unroute('**/api/projects?*');

  let unsafeCalls = 0;
  await page.route('**/api/fe002/unsafe', async route => {
    if (await fulfillPreflight(route)) return;
    unsafeCalls += 1;
    await route.fulfill({
      status: 401,
      headers: corsHeaders,
      body: failureEnvelope(401, 'AUTHENTICATION_REQUIRED', 'Refresh required.')
    });
  });
  const unsafeError = await angularApi(`client.post('/api/fe002/unsafe', { value: 1 })
    .then(function() { return null; }, function(error) {
      return { code: error.code, status: error.status, correlationId: error.correlationId };
    })`);
  assert.deepEqual(unsafeError.code, 'REQUEST_REPLAY_REQUIRED');
  assert.equal(unsafeError.status, 409);
  assert.equal(unsafeCalls, 1);
  await page.unroute('**/api/fe002/unsafe');

  const idempotentCalls = [];
  await page.route('**/api/fe002/idempotent', async route => {
    if (await fulfillPreflight(route)) return;
    idempotentCalls.push({
      correlation: route.request().headers()['x-correlation-id'],
      idempotency: route.request().headers()['idempotency-key']
    });
    if (idempotentCalls.length === 1) {
      await route.fulfill({
        status: 401,
        headers: corsHeaders,
        body: failureEnvelope(401, 'AUTHENTICATION_REQUIRED', 'Refresh required.')
      });
      return;
    }
    await route.fulfill({ status: 200, headers: corsHeaders, body: envelope({ accepted: true }) });
  });
  const idempotencyKey = `fe002-${stamp}`;
  const idempotentResult = await angularApi(`client.post('/api/fe002/idempotent', { value: 2 }, { idempotencyKey: argument })`, idempotencyKey);
  assert.equal(idempotentResult.accepted, true);
  assert.equal(idempotentCalls.length, 2);
  assert.equal(idempotentCalls[0].idempotency, idempotencyKey);
  assert.equal(idempotentCalls[0].idempotency, idempotentCalls[1].idempotency);
  assert.equal(idempotentCalls[0].correlation, idempotentCalls[1].correlation);
  await page.unroute('**/api/fe002/idempotent');

  await page.route('**/api/fe002/failure', async route => {
    if (await fulfillPreflight(route)) return;
    await route.fulfill({
      status: 503,
      headers: corsHeaders,
      body: failureEnvelope(503, 'DEPENDENCY_UNAVAILABLE', 'Service temporarily unavailable.', {
        refreshToken: 'must-not-leak'
      })
    });
  });
  const normalized = await angularApi(`client.get('/api/fe002/failure')
    .then(function() { return null; }, function(error) { return JSON.parse(JSON.stringify(error)); })`);
  assert.equal(normalized.code, 'DEPENDENCY_UNAVAILABLE');
  assert.equal(normalized.status, 503);
  assert.equal(normalized.retryable, true);
  assert.doesNotMatch(JSON.stringify(normalized), /must-not-leak|refreshToken/);
  await page.unroute('**/api/fe002/failure');

  await page.route('**/api/fe002/slow', async route => {
    if (await fulfillPreflight(route)) return;
    await new Promise(resolveWait => setTimeout(resolveWait, 600));
    await route.fulfill({ status: 200, headers: corsHeaders, body: envelope({ stale: true }) }).catch(() => {});
  });
  const stale = await angularApi(`(function() {
    var pending = client.get('/api/fe002/slow', { scope: 'context-probe' });
    setTimeout(function() { client.transitionContext('project:' + argument); }, 30);
    return pending.then(function() { return null; }, function(error) {
      return { code: error.code, stale: error.stale, canceled: error.canceled };
    });
  })()`, 'next');
  assert.equal(stale.code, 'STALE_RESPONSE');
  assert.equal(stale.stale, true);
  await page.unroute('**/api/fe002/slow');

  await page.locator('.board-skeleton').waitFor({ state: 'hidden' });
  await page.locator('.board-empty, .board-shell').first().waitFor({ state: 'visible' });
  desktopHorizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  assert.ok(desktopHorizontalOverflow <= 1, `Desktop has ${desktopHorizontalOverflow}px horizontal overflow.`);
  await page.screenshot({ path: resolve(outputDirectory, 'desktop-workspace.png'), fullPage: true });

  await page.evaluate(() => {
    const localKeys = [
      'zumbo.projectId', 'zumbo.recentProjects', 'zumbo.favoriteProjects',
      'zumbo.collapsedColumns', 'zumbo.cardFields', 'zumbo.accessToken', 'zumbo.refreshToken'
    ];
    localKeys.forEach(key => localStorage.setItem(key, 'tenant-value'));
    localStorage.setItem('zumbo.theme', 'dark');
    sessionStorage.setItem('zumbo.csrfToken', sessionStorage.getItem('zumbo.csrfToken') || 'csrf-value');
  });
  await page.getByRole('button', { name: 'Çıkış', exact: true }).first().click();
  await page.locator('input[autocomplete="username"]').waitFor({ state: 'visible' });
  const cleanup = await page.evaluate(() => ({
    tenantLocalValues: [
      'zumbo.currentUser', 'zumbo.projectId', 'zumbo.recentProjects', 'zumbo.favoriteProjects',
      'zumbo.collapsedColumns', 'zumbo.cardFields', 'zumbo.accessToken', 'zumbo.refreshToken'
    ].map(key => localStorage.getItem(key)),
    csrf: sessionStorage.getItem('zumbo.csrfToken'),
    theme: localStorage.getItem('zumbo.theme')
  }));
  assert.equal(cleanup.tenantLocalValues.every(value => value === null), true);
  assert.equal(cleanup.csrf, null);
  assert.equal(cleanup.theme, 'dark');

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${staticServer.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'networkidle' });
  await page.locator('input[type="text"]').first().fill(email);
  await page.locator('input[type="password"]').first().fill(password);
  await page.getByRole('button', { name: /giri/i }).click();
  await page.waitForURL(/#\/app\/dashboard/);
  await page.waitForFunction(() => {
    const injector = window.angular.element(document.body).injector();
    return !!injector.get('sessionStore').state.project;
  });
  await page.locator('ion-view[view-title="Özet"]').waitFor({ state: 'visible' });
  const mobileProbe = await page.evaluate(async () => {
    const injector = window.angular.element(document.body).injector();
    const client = injector.get('apiClient');
    const organizations = await client.get('/api/organizations');
    return {
      baseUrl: client.baseUrl,
      organizations: organizations.length,
      sharedModule: injector.has('apiClient') && injector.has('sessionStore')
    };
  });
  assert.equal(mobileProbe.baseUrl, apiBaseUrl);
  assert.ok(mobileProbe.organizations >= 1);
  assert.equal(mobileProbe.sharedModule, true);
  mobileHorizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  assert.ok(mobileHorizontalOverflow <= 1, `Mobile has ${mobileHorizontalOverflow}px horizontal overflow.`);
  await page.screenshot({ path: resolve(outputDirectory, 'mobile-dashboard.png'), fullPage: true });

  assert.deepEqual(failures, []);
  const result = {
    passed: true,
    browser: 'chromium',
    safeConcurrentRequests: 2,
    coordinatedRefreshes: 1,
    unsafeMutationCalls: unsafeCalls,
    unsafeMutationCode: unsafeError.code,
    idempotentMutationCalls: idempotentCalls.length,
    idempotencyHeaderPreserved: idempotentCalls[0].idempotency === idempotentCalls[1].idempotency,
    correlationHeaderPreserved: idempotentCalls[0].correlation === idempotentCalls[1].correlation,
    staleResponseCode: stale.code,
    normalizedErrorCode: normalized.code,
    logoutTenantStateCleared: cleanup.tenantLocalValues.every(value => value === null) && cleanup.csrf === null,
    devicePreferencePreserved: cleanup.theme === 'dark',
    desktopBaseUrl: apiBaseUrl,
    mobileBaseUrl: mobileProbe.baseUrl,
    desktopHorizontalOverflow,
    mobileHorizontalOverflow,
    consoleFailures: failures.length
  };
  await writeFile(resolve(outputDirectory, 'result.json'), `${JSON.stringify(result, null, 2)}\n`, 'utf8');
  console.log('FE-002 Chromium shared API client workflow passed.');
} finally {
  await context.close();
  await browser.close();
  await staticServer.close();
}
