import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { buildFrontend } from './build-frontend.mjs';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { startStaticServer } from './static-server.mjs';

const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for the PLATFORM-004 browser gate');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for the PLATFORM-004 browser gate');
const password = 'P@ssword123';
const frontendUrl = new URL(frontendBaseUrl);
const outputDirectory = resolve(import.meta.dirname, '../../artifacts/runtime/platform004-browser');
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

async function authenticate() {
  const login = await fetch(`${apiBaseUrl}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ usernameOrEmail: adminEmail, password })
  });
  if (login.ok) return (await login.json()).data;

  const stamp = Date.now().toString(36);
  return api('/api/auth/register', 'POST', {
    username: `platform004-${stamp}`,
    email: adminEmail,
    password,
    organizationId: `platform004-org-${stamp}`,
    bootstrapToken
  });
}

const server = await startStaticServer(resolve(import.meta.dirname, '../dist'), {
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
page.on('pageerror', error => failures.push(`page: ${error.message}`));
page.on('console', message => {
  if (message.type() === 'error'
    && !message.text().includes('ERR_INTERNET_DISCONNECTED')
    && !message.text().includes('Failed to load resource')) {
    failures.push(`console: ${message.text()}`);
  }
});

try {
  const auth = await authenticate();
  const stamp = Date.now().toString(36).toUpperCase();
  try {
    await api('/api/organizations', 'POST', {
      name: `PLATFORM-004 browser organization ${stamp}`,
      tenantKey: auth.user.organizationId
    }, auth.accessToken);
  } catch (error) {
    const message = String(error.message).toLowerCase();
    if (!message.includes('already exists') && !message.includes('must be unique')) throw error;
  }
  const project = await api('/api/projects', 'POST', {
    organizationId: auth.user.organizationId,
    key: `R${stamp.slice(-6)}`,
    name: `Realtime browser ${stamp}`,
    ownerUserId: auth.user.id
  }, auth.accessToken);
  const board = await api('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Realtime browser board',
    type: 'Kanban'
  }, auth.accessToken);
  const baseline = await api('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Realtime baseline',
    type: 'Task',
    priority: 'High',
    assigneeUserId: auth.user.id
  }, auth.accessToken);

  await page.goto(`${server.origin}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await page.locator('input[autocomplete="username"]').fill(adminEmail);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.locator('form').getByRole('button', { name: /giri/i }).click();
  await page.locator('.side-nav').waitFor({ state: 'visible' });
  await page.evaluate(({ projectId, boardId }) => {
    window.location.hash = `section=board&project=${encodeURIComponent(projectId)}&board=${encodeURIComponent(boardId)}`;
  }, { projectId: project.id, boardId: board.id });
  await page.locator('select[ng-model="vm.project"]').selectOption({ label: project.name });
  await page.waitForFunction(({ projectId, boardId }) => {
    const vm = window.angular.element(document.body).scope().vm;
    return vm.project?.id === projectId && vm.board?.id === boardId && !vm.loading && !vm.activeTaskLoad;
  }, { projectId: project.id, boardId: board.id });
  await page.evaluate(({ projectId, initial }) => {
    window.__platform004Events = [];
    window.__platform004ResyncItems = [];
    const injector = window.angular.element(document.querySelector('[ng-app]')).injector();
    const service = injector.get('realtimeService');
    service.subscribe(change => {
      window.__platform004Events.push(change);
      if (change.eventType !== 'resyncRequired') return;
      fetch(`${window.__ZUMBO_RUNTIME_CONFIG__.apiBaseUrl}/api/work-items?projectId=${encodeURIComponent(projectId)}&page=1&pageSize=100`, {
        credentials: 'include'
      }).then(response => response.json()).then(payload => {
        window.__platform004ResyncItems = payload.data;
        service.synchronize(payload.data);
      });
    });
    service.synchronize([initial]);
    window.__platform004Connect = service.connect(projectId);
  }, {
    projectId: project.id,
    initial: baseline
  });
  await page.evaluate(() => window.__platform004Connect);

  const moved = await api(`/api/work-items/${baseline.id}/status`, 'PATCH', {
    status: 'In Progress'
  }, auth.accessToken);
  await page.waitForFunction(({ id, version }) => window.__platform004Events.some(change =>
    change.workItemId === id && change.resourceVersion === version && change.schemaVersion === 1),
  { id: baseline.id, version: moved.version });

  await context.setOffline(true);
  await page.waitForTimeout(750);
  const missed = await api('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Created while browser was offline',
    type: 'Task',
    priority: 'Medium',
    assigneeUserId: auth.user.id
  }, auth.accessToken);
  await context.setOffline(false);
  await page.waitForFunction(id => window.__platform004Events.some(change =>
    change.eventType === 'resyncRequired')
    && window.__platform004ResyncItems.some(item => item.id === id)
    && window.angular.element(document.body).scope().vm.tasks.some(item => item.id === id), missed.id, { timeout: 30_000 });
  await page.locator(`[data-work-item-id="${missed.id}"]`).waitFor({ state: 'visible' });

  await page.screenshot({ path: resolve(outputDirectory, 'desktop-reconnected.png'), fullPage: true });
  assert.deepEqual(failures, []);
  const result = {
    passed: true,
    browser: 'chromium',
    projectId: project.id,
    baselineVersion: baseline.version,
    receivedVersion: moved.version,
    missedWorkItemId: missed.id,
    resyncCount: await page.evaluate(() => window.__platform004ResyncItems.length),
    uiTaskCount: await page.evaluate(() => window.angular.element(document.body).scope().vm.tasks.length)
  };
  await writeFile(resolve(outputDirectory, 'result.json'), JSON.stringify(result, null, 2));
  console.log(`PLATFORM-004 browser reconnect/resync passed for ${project.id}.`);
} catch (error) {
  await page.screenshot({ path: resolve(outputDirectory, 'failure.png'), fullPage: true }).catch(() => {});
  const loginError = await page.locator('.login-error').textContent().catch(() => null);
  await writeFile(resolve(outputDirectory, 'result.json'), JSON.stringify({
    passed: false,
    error: error.stack || error.message,
    loginError,
    failures
  }, null, 2));
  throw error;
} finally {
  await context.close();
  await browser.close();
  await server.close();
}
