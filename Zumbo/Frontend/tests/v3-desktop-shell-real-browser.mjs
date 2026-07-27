import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-001', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX tenant cleanup');
const password = 'P@ssword123';
let cleanupAdminTokenPromise;
let browser;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };

await mkdir(outputDir, { recursive: true });

async function apiRequest(path, method, body, token) {
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

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      let authentication = await apiRequest('/api/auth/register', 'POST', {
        username: 'local-system-admin',
        email: adminEmail,
        password,
        organizationId: 'local-system-administration',
        bootstrapToken: adminBootstrapToken
      });
      if (authentication.response.status === 409) {
        authentication = await apiRequest('/api/auth/login', 'POST', {
          usernameOrEmail: adminEmail,
          password
        });
      }
      assert.ok(authentication.response.ok, authentication.payload.error?.message || 'Cleanup administrator authentication failed');
      return authentication.data.accessToken;
    })();
  }
  return cleanupAdminTokenPromise;
}

async function archiveTenant() {
  const token = await cleanupAdminToken();
  const result = await apiRequest(`/api/organizations/${encodeURIComponent(tenantId)}/archive`, 'POST', undefined, token);
  if (result.response.ok || result.response.status === 404) {
    return { tenantId, status: result.response.status };
  }
  throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
}

async function browserContextLogin(context, usernameOrEmail) {
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

function attachDiagnostics(page, label, failures) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (detail.includes('/hubs/work-items') || detail.includes('Failed to start the connection')) return;
    if (!detail.includes('Failed to load resource')) failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

function assertNoBrowserSecrets(state) {
  assert.equal(state.accessToken, null, 'Access token was exposed through Web Storage');
  assert.equal(state.refreshToken, null, 'Refresh token was exposed through Web Storage');
  assert.doesNotMatch(state.visibleCookies, /zumbo-(?:access|refresh)=/i, 'HttpOnly auth cookie was visible to JavaScript');
}

async function browserSecurityState(page) {
  return page.evaluate(() => ({
    accessToken: localStorage.getItem('zumbo.accessToken') || sessionStorage.getItem('zumbo.accessToken'),
    refreshToken: localStorage.getItem('zumbo.refreshToken') || sessionStorage.getItem('zumbo.refreshToken'),
    visibleCookies: document.cookie
  }));
}

const failures = [];

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await apiRequest('/api/auth/register', 'POST', {
    username: `uxowner${stamp}`,
    email: `uxowner${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  });
  assert.ok(ownerRegistration.response.ok, ownerRegistration.payload.error?.message || 'Owner registration failed');
  const owner = ownerRegistration.data.user;
  const ownerToken = ownerRegistration.data.accessToken;
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);
  const organization = await apiRequest('/api/organizations', 'POST', {
    name: 'Zumbo UX Workspace',
    tenantKey: tenantId
  }, ownerToken);
  assert.ok(organization.response.ok, organization.payload.error?.message || 'Organization creation failed');
  const project = await apiRequest('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `UX${stamp.slice(-6)}`,
    name: 'Zumbo Platform',
    ownerUserId: owner.id
  }, ownerToken);
  assert.ok(project.response.ok, project.payload.error?.message || 'Project creation failed');
  const projectId = project.data.id;
  const board = await apiRequest('/api/boards', 'POST', {
    projectId,
    name: 'Mühendislik Panosu',
    type: 'Kanban'
  }, ownerToken);
  assert.ok(board.response.ok, board.payload.error?.message || 'Board creation failed');
  const taskTitle = `Servis sınırını gözden geçir ${stamp.slice(-4)}`;
  const task = await apiRequest('/api/work-items', 'POST', {
    projectId,
    boardId: board.data.id,
    title: taskTitle,
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id,
    dueDate: null
  }, ownerToken);
  assert.ok(task.response.ok, task.payload.error?.message || 'Work-item creation failed');

  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'owner', failures);
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${projectId}&board=${board.data.id}`,
    { waitUntil: 'domcontentloaded' }
  );
  const ownerTask = ownerPage.locator('.task').first();
  await ownerTask.waitFor({ timeout: 45_000 });
  const shellState = await ownerPage.evaluate(() => {
    const scope = window.angular.element(document.body).scope();
    return {
      boardId: scope.vm.board?.id,
      user: JSON.parse(localStorage.getItem('zumbo.currentUser'))
    };
  });
  assert.equal(shellState.boardId, board.data.id, 'Real workspace did not restore the requested board context');
  assert.equal(shellState.user.organizationId, tenantId, 'Real-browser fixture escaped its controlled tenant');
  assertNoBrowserSecrets(await browserSecurityState(ownerPage));

  const nav = ownerPage.locator('.side-nav');
  await nav.getByRole('button', { name: 'Raporlar', exact: true }).click();
  await ownerPage.waitForFunction(() => location.hash.includes('section=reports'));
  await nav.getByRole('button', { name: 'Ekipler', exact: true }).click();
  await ownerPage.waitForFunction(() => location.hash.includes('section=teams'));
  await ownerPage.goBack();
  await ownerPage.waitForFunction(() => location.hash.includes('section=reports'));
  assert.ok(await nav.getByRole('button', { name: 'Raporlar', exact: true }).evaluate(element => element.classList.contains('active')));
  await ownerPage.goForward();
  await ownerPage.waitForFunction(() => location.hash.includes('section=teams'));

  await ownerPage.keyboard.press('Control+K');
  const commandInput = ownerPage.getByRole('combobox', { name: 'Komut ara' });
  await commandInput.fill(taskTitle);
  await ownerPage.locator('[role="option"]').filter({ hasText: taskTitle }).waitFor();
  await commandInput.press('Enter');
  await ownerPage.locator('.inspector').waitFor();
  assert.equal(await ownerPage.locator('#task-title').inputValue(), taskTitle);
  await ownerPage.waitForFunction(id => location.hash.includes('section=board') && location.hash.includes(`task=${id}`), task.data.id);
  const notificationButton = ownerPage.getByRole('button', { name: 'Bildirimler', exact: true });
  await notificationButton.click();
  await ownerPage.locator('.notification-popover').waitFor();
  await notificationButton.click();
  const userMenuButton = ownerPage.getByRole('button', { name: 'Kullanıcı menüsü', exact: true });
  await userMenuButton.click();
  assert.ok(await ownerPage.locator('.user-popover').getByText(shellState.user.email, { exact: true }).isVisible());
  assert.ok(await ownerPage.locator('.user-popover').getByRole('button', { name: 'Çıkış yap', exact: true }).isVisible());
  await userMenuButton.click();
  assert.equal((await ownerPage.locator('body').innerText()).includes(tenantId), false, 'Shell exposed the opaque tenant identifier');
  const themeButton = ownerPage.getByRole('button', { name: 'Temayı değiştir', exact: true }).first();
  await themeButton.click();
  await ownerPage.locator('body.theme-dark').waitFor();
  await themeButton.click();
  assert.equal(await ownerPage.locator('body.theme-dark').count(), 0);

  const viewerRegistration = await apiRequest('/api/auth/register', 'POST', {
    username: `uxviewer${stamp}`,
    email: `uxviewer${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  });
  assert.ok(viewerRegistration.response.ok, viewerRegistration.payload.error?.message || 'Viewer registration failed');
  const viewer = viewerRegistration.data.user;
  const viewerGrant = await apiRequest(`/api/projects/${projectId}/members`, 'POST', {
    userId: viewer.id,
    role: 'Viewer'
  }, ownerToken);
  assert.ok(viewerGrant.response.ok, viewerGrant.payload.error?.message || 'Viewer project grant failed');

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewer.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'viewer', failures);
  await viewerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${projectId}&board=${shellState.boardId}`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.locator('.task').filter({ hasText: taskTitle }).waitFor({ timeout: 45_000 });
  assertNoBrowserSecrets(await browserSecurityState(viewerPage));
  await viewerPage.locator('.create-button').click();
  assert.equal(await viewerPage.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).count(), 0);
  await viewerPage.keyboard.press('Control+K');
  await viewerPage.getByRole('combobox', { name: 'Komut ara' }).fill('Yeni görev');
  await viewerPage.getByText('Eşleşen komut veya görev yok.').waitFor();
  assert.equal(await viewerPage.locator('[role="option"]').count(), 0);

  await ownerPage.screenshot({ path: resolve(outputDir, 'owner-shell.png'), fullPage: true });
  await ownerPage.setViewportSize({ width: 390, height: 844 });
  await ownerPage.locator('.inspector .delete').click();
  const responsiveState = await ownerPage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(responsiveState.scrollWidth <= responsiveState.width + 1, `Shell overflowed at 390px: ${responsiveState.scrollWidth}/${responsiveState.width}`);
  assert.ok(await ownerPage.locator('.command-trigger').isVisible(), 'Command trigger disappeared at 390px');
  await viewerPage.screenshot({ path: resolve(outputDir, 'viewer-shell.png'), fullPage: true });
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-UX-001',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0,
    checks: [
      'real-api',
      'cookie-session',
      'history',
      'deep-link',
      'command-task-open',
      'viewer-create-boundary',
      'notification-session-controls',
      'light-dark-theme',
      'display-name-boundary',
      'responsive-390'
    ],
    cleanup: cleanupResult,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
console.log('V3-UX-001 real-browser shell passed: API-backed history, command task open, cookie session and Viewer boundary.');
