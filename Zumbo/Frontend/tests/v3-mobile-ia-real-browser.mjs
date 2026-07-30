import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-mobile-001-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-MOBILE-001', 'chromium');
const ownerTenant = runContext.tenants.mobile;
const outsiderTenant = `${ownerTenant}-outside`;
const password = 'P@ssword123';
const checks = [];
const failures = [];
const cleanup = [];
const cleanupTargets = [];
let browser;

await mkdir(output, { recursive: true });
await buildFrontend();

async function apiRequest(path, method = 'GET', body, token) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label) {
  const result = await apiRequest(path, method, body, token);
  assert.ok(result.response.ok, result.payload.error?.message || `${label} failed with HTTP ${result.response.status}`);
  return result.data;
}

async function register(tenantId, prefix) {
  const suffix = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const registration = await requireApi('/api/auth/register', 'POST', {
    username: `${prefix}${suffix}`,
    email: `${prefix}${suffix}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, `${prefix} registration`);
  cleanupTargets.push({ tenantId, token: registration.accessToken });
  return registration;
}

async function browserLogin(context, usernameOrEmail) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin },
    data: { usernameOrEmail, password }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser login failed');
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, payload.data);
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (/WebSocket|signalr|Failed to start the connection|Failed to load resource/.test(detail)) return;
    failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

async function assertNoOverflow(page) {
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
}

try {
  const ownerRegistration = await register(ownerTenant, 'mobileowner');
  const owner = ownerRegistration.user;
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Mobil Çalışma Alanı',
    tenantKey: ownerTenant
  }, ownerRegistration.accessToken, 'Owner organization creation');
  const project = await requireApi('/api/projects', 'POST', {
    organizationId: ownerTenant,
    key: 'MOB',
    name: 'Mobil Teslimat',
    ownerUserId: owner.id
  }, ownerRegistration.accessToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Mobil Teslimat Panosu',
    type: 'Kanban'
  }, ownerRegistration.accessToken, 'Board creation');
  const fixtureTitle = `Mobil IA kanıtı ${runContext.runId.slice(-6)}`;
  await requireApi('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: fixtureTitle,
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id
  }, ownerRegistration.accessToken, 'Work item creation');

  const outsiderRegistration = await register(outsiderTenant, 'mobileoutside');
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Ayrı Mobil Alan',
    tenantKey: outsiderTenant
  }, outsiderRegistration.accessToken, 'Outsider organization creation');
  const denied = await apiRequest('/api/work-items/search', 'POST', {
    projectId: project.id,
    text: fixtureTitle,
    page: 1,
    pageSize: 50
  }, outsiderRegistration.accessToken);
  assert.ok([403, 404].includes(denied.response.status), `Cross-tenant search returned HTTP ${denied.response.status}`);
  checks.push('cross-tenant-project-search-denied');

  browser = await chromium.launch({ headless: true });
  for (const width of [360, 390, 430]) {
    const context = await browser.newContext({
      viewport: { width, height: width === 360 ? 780 : 844 },
      reducedMotion: 'reduce',
      timezoneId: 'Europe/Istanbul'
    });
    await browserLogin(context, owner.username);
    const page = await context.newPage();
    diagnostics(page, `real-home-${width}`);
    await page.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/app/dashboard`, {
      waitUntil: 'domcontentloaded'
    });
    await page.getByText(fixtureTitle, { exact: true }).waitFor({ timeout: 45_000 });
    const tabs = page.locator('.zumbo-primary-tabs .tab-item');
    assert.equal(await tabs.count(), 5);
    assert.deepEqual(
      await tabs.locator('.tab-title').allTextContents(),
      ['Ana sayfa', 'İşlerim', 'Oluştur', 'Gelen kutusu', 'Daha fazla']
    );
    await assertNoOverflow(page);
    await page.screenshot({ path: resolve(output, `home-${width}.png`), fullPage: true });
    checks.push(`real-home-${width}`);
    await context.close();
  }

  const ownerContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'real-owner-flow');
  const searchBodies = [];
  ownerPage.on('request', request => {
    if (new URL(request.url()).pathname !== '/api/work-items/search') return;
    const body = request.postDataJSON();
    if (body.text) searchBodies.push(body);
  });
  await ownerPage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/app/more`, {
    waitUntil: 'domcontentloaded'
  });
  await ownerPage.getByRole('heading', { name: 'Daha fazla' }).waitFor({ timeout: 45_000 });
  assert.equal(await ownerPage.locator('.mobile-more-nav > button').count(), 3);
  await assertNoOverflow(ownerPage);
  checks.push('real-more-secondary-navigation');
  await ownerPage.getByRole('button', { name: /Arama/ }).click();
  const searchInput = ownerPage.getByPlaceholder('Başlık veya içerik ara');
  await searchInput.waitFor({ timeout: 45_000 });
  assert.match(await ownerPage.getByLabel('Arama projesi').locator('option:checked').innerText(), /MOB/);
  await searchInput.fill(fixtureTitle);
  await ownerPage.getByRole('button', { name: 'Ara', exact: true }).click();
  await ownerPage.getByText(fixtureTitle, { exact: true }).waitFor();
  assert.equal(searchBodies.at(-1).projectId, project.id);
  await assertNoOverflow(ownerPage);
  await ownerPage.screenshot({ path: resolve(output, 'search-project-scoped-390.png'), fullPage: true });
  checks.push('real-project-scoped-search');

  await ownerPage.locator('.zumbo-primary-tabs .tab-item').filter({ hasText: 'Oluştur' }).click();
  await ownerPage.getByRole('heading', { name: 'Görev oluştur' }).waitFor();
  await ownerPage.getByRole('button', { name: 'Görev ayrıntılarına geç' }).click();
  const createPopup = ownerPage.locator('.popup-container');
  await createPopup.waitFor({ timeout: 45_000 });
  const createdTitle = `Mobil oluşturma ${runContext.runId.slice(-6)}`;
  await createPopup.locator('input[type="text"]').first().fill(createdTitle);
  await createPopup.locator('.popup-buttons .button-positive').click();
  await createPopup.waitFor({ state: 'hidden' });
  await ownerPage.locator('.zumbo-primary-tabs .tab-item').filter({ hasText: 'Daha fazla' }).click();
  const activeSearch = ownerPage.locator('.mobile-global-search:visible');
  await activeSearch.waitFor();
  await activeSearch.getByPlaceholder('Başlık veya içerik ara').fill(createdTitle);
  await activeSearch.getByRole('button', { name: 'Ara', exact: true }).click();
  await ownerPage.locator('.mobile-search-results:visible').getByText(createdTitle, { exact: true }).waitFor();
  checks.push('real-create-through-existing-form');

  await ownerPage.locator('.zumbo-primary-tabs .tab-item').filter({ hasText: 'Gelen kutusu' }).click();
  await ownerPage.locator('p:visible').filter({ hasText: fixtureTitle }).waitFor();
  checks.push('real-inbox-primary-route');
  await ownerContext.close();

  const outsiderContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce'
  });
  await browserLogin(outsiderContext, outsiderRegistration.user.username);
  const outsiderPage = await outsiderContext.newPage();
  diagnostics(outsiderPage, 'real-outsider');
  await outsiderPage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/app/search`, {
    waitUntil: 'domcontentloaded'
  });
  await outsiderPage.getByRole('heading', { name: 'Aranabilir proje yok' }).waitFor({ timeout: 45_000 });
  assert.equal((await outsiderPage.locator('body').innerText()).includes(fixtureTitle), false);
  await assertNoOverflow(outsiderPage);
  checks.push('real-outsider-empty-authorized-scope');
  await outsiderContext.close();

  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  for (const target of cleanupTargets.reverse()) {
    const result = await apiRequest(
      `/api/organizations/${encodeURIComponent(target.tenantId)}/archive`,
      'POST',
      undefined,
      target.token
    ).catch(error => ({ response: { ok: false, status: 0 }, payload: { error: { message: error.message } } }));
    cleanup.push({
      tenantId: target.tenantId,
      status: result.response.status,
      passed: result.response.ok || result.response.status === 404,
      error: result.response.ok || result.response.status === 404 ? null : result.payload.error?.message
    });
  }
  await browser?.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-MOBILE-001',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanup.every(item => item.passed) && checks.length === 9,
    apiBaseUrl,
    frontendBaseUrl,
    viewports: ['360x780', '390x844', '430x844'],
    checks,
    cleanup,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.ok(cleanup.every(item => item.passed), `Cleanup failed: ${cleanup.map(item => item.error).filter(Boolean).join(' | ')}`);
assert.equal(checks.length, 9);
console.log('V3-MOBILE-001 real-browser passed: 360/390/430 shell, scoped search, create, inbox and tenant isolation.');
