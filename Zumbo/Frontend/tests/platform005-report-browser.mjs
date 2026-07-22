import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { buildFrontend } from './build-frontend.mjs';
import { apiBaseUrl, frontendBaseUrl } from './environment.mjs';
import { startStaticServer } from './static-server.mjs';

const password = 'P@ssword123';
const stamp = Date.now().toString(36);
const email = `platform005-${stamp}@zumbo.local`;
const organizationId = `platform005-org-${stamp}`;
const outputDirectory = resolve(import.meta.dirname, '../../artifacts/runtime/platform005-browser');
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

const auth = await api('/api/auth/register', 'POST', {
  username: `platform005-${stamp}`,
  email,
  password,
  organizationId
});
await api('/api/organizations', 'POST', {
  name: `PLATFORM-005 browser organization ${stamp}`,
  tenantKey: organizationId
}, auth.accessToken);
const project = await api('/api/projects', 'POST', {
  organizationId,
  key: `P${stamp.slice(-6).toUpperCase()}`,
  name: `Reporting browser ${stamp}`,
  ownerUserId: auth.user.id
}, auth.accessToken);
const board = await api('/api/boards', 'POST', {
  projectId: project.id,
  name: 'Reporting browser board',
  type: 'Kanban'
}, auth.accessToken);
for (const title of ['Materialized baseline one', 'Materialized baseline two']) {
  await api('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title,
    type: 'Task',
    priority: 'Medium',
    assigneeUserId: auth.user.id
  }, auth.accessToken);
}

const frontendUrl = new URL(frontendBaseUrl);
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
  if (message.type() === 'error' && !message.text().includes('Failed to load resource')) {
    failures.push(`console: ${message.text()}`);
  }
});

try {
  await page.goto(`${server.origin}/desktop-bulma/index.html`, { waitUntil: 'networkidle' });
  await page.locator('input[autocomplete="username"]').fill(email);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.locator('form').getByRole('button', { name: /giri/i }).click();
  await page.locator('.side-nav').waitFor({ state: 'visible' });
  await page.locator('select[ng-model="vm.project"]').selectOption({ label: project.name });
  await page.waitForFunction(projectId => {
    const vm = window.angular.element(document.body).scope().vm;
    return vm.project?.id === projectId && vm.summary?.total === 2 && !vm.loading;
  }, project.id);

  const first = await page.evaluate(async projectId => {
    const response = await fetch(
      `${window.__ZUMBO_RUNTIME_CONFIG__.apiBaseUrl}/api/work-items/reports/project-summary/${projectId}`,
      { credentials: 'include' });
    return {
      status: response.status,
      body: await response.json(),
      generatedAt: response.headers.get('x-zumbo-report-generated-at'),
      sourceVersion: response.headers.get('x-zumbo-report-source-version'),
      stale: response.headers.get('x-zumbo-report-stale'),
      ageSeconds: response.headers.get('x-zumbo-report-age-seconds')
    };
  }, project.id);
  const second = await page.evaluate(async projectId => {
    const response = await fetch(
      `${window.__ZUMBO_RUNTIME_CONFIG__.apiBaseUrl}/api/work-items/reports/project-summary/${projectId}`,
      { credentials: 'include' });
    return {
      generatedAt: response.headers.get('x-zumbo-report-generated-at'),
      sourceVersion: response.headers.get('x-zumbo-report-source-version')
    };
  }, project.id);

  assert.equal(first.status, 200);
  assert.equal(first.body.data.total, 2);
  assert.ok(Number.isFinite(Date.parse(first.generatedAt)));
  assert.ok(Number.isInteger(Number(first.sourceVersion)));
  assert.equal(first.stale, 'false');
  assert.ok(Number(first.ageSeconds) >= 0);
  assert.equal(second.generatedAt, first.generatedAt);
  assert.equal(second.sourceVersion, first.sourceVersion);

  await api('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Mutation invalidates report snapshot',
    type: 'Task',
    priority: 'High',
    assigneeUserId: auth.user.id
  }, auth.accessToken);

  await page.waitForFunction(async ({ projectId, previousVersion }) => {
    const response = await fetch(
      `${window.__ZUMBO_RUNTIME_CONFIG__.apiBaseUrl}/api/work-items/reports/project-summary/${projectId}`,
      { credentials: 'include' });
    const payload = await response.json();
    return payload.data.total === 3
      && Number(response.headers.get('x-zumbo-report-source-version')) > Number(previousVersion)
      && response.headers.get('x-zumbo-report-stale') === 'false';
  }, { projectId: project.id, previousVersion: first.sourceVersion }, { timeout: 30_000 });

  await page.locator('.summary-strip').screenshot({
    path: resolve(outputDirectory, 'fresh-summary.png')
  });
  assert.deepEqual(failures, []);
  const result = {
    passed: true,
    browser: 'chromium',
    projectId: project.id,
    initialTotal: first.body.data.total,
    refreshedTotal: 3,
    initialSourceVersion: Number(first.sourceVersion),
    cacheHitGeneratedAt: second.generatedAt,
    stale: false
  };
  await writeFile(resolve(outputDirectory, 'result.json'), JSON.stringify(result, null, 2));
  console.log(`PLATFORM-005 browser report freshness passed for ${project.id}.`);
} catch (error) {
  await page.screenshot({ path: resolve(outputDirectory, 'failure.png'), fullPage: true }).catch(() => {});
  await writeFile(resolve(outputDirectory, 'result.json'), JSON.stringify({
    passed: false,
    error: error.stack || error.message,
    failures
  }, null, 2));
  throw error;
} finally {
  await context.close();
  await browser.close();
  await server.close();
}
