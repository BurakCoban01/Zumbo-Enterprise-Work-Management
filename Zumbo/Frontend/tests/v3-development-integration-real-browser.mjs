import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const output = resolve(import.meta.dirname, '../../artifacts/ui/v3-integration-001-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-INTEGRATION-001', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret(
  'ZUMBO_IDENTITY_ADMIN_EMAIL',
  'for V3-INTEGRATION-001 tenant cleanup'
);
const bootstrapToken = requireLocalSecret(
  'ZUMBO_IDENTITY_BOOTSTRAP_TOKEN',
  'for V3-INTEGRATION-001 tenant cleanup'
);
const requestedProviderPort = process.env.ZUMBO_DEVELOPMENT_PROVIDER_PORT
  ? Number(process.env.ZUMBO_DEVELOPMENT_PROVIDER_PORT)
  : 0;
let providerPort;
let providerBaseUrl;
let repositoryUrl;
const providerToken = 'github-read-token-synthetic-123456';
const password = 'P@ssword123';
const checks = [];
const failures = [];
const providerRequests = [];
let cleanup = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;
let ownerToken;

assert.ok(
  Number.isInteger(requestedProviderPort)
    && (requestedProviderPort === 0
      || requestedProviderPort >= 1024 && requestedProviderPort <= 65535),
  'ZUMBO_DEVELOPMENT_PROVIDER_PORT must be zero or a non-privileged TCP port.'
);
await mkdir(output, { recursive: true });

const provider = createServer((request, response) => {
  providerRequests.push({
    method: request.method,
    path: request.url,
    authorization: request.headers.authorization || '',
    userAgent: request.headers['user-agent'] || ''
  });
  if (request.method !== 'GET'
      || request.headers.authorization !== `Bearer ${providerToken}`) {
    response.writeHead(401, { 'Content-Type': 'application/json' });
    response.end('{"message":"unauthorized"}');
    return;
  }
  if (new URL(request.url, providerBaseUrl).pathname === '/user') {
    response.writeHead(200, { 'Content-Type': 'application/json' });
    response.end('{"id":7,"login":"zumbo-e2e"}');
    return;
  }
  if (new URL(request.url, providerBaseUrl).pathname === '/user/repos') {
    response.writeHead(200, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify([{
      id: 4242,
      name: 'platform',
      full_name: 'zumbo/platform',
      html_url: repositoryUrl,
      default_branch: 'main'
    }]));
    return;
  }
  response.writeHead(404, { 'Content-Type': 'application/json' });
  response.end('{"message":"not found"}');
});

await new Promise((resolveListen, reject) => {
  provider.once('error', reject);
  provider.listen(requestedProviderPort, '127.0.0.1', resolveListen);
});
providerPort = provider.address().port;
providerBaseUrl = `http://127.0.0.1:${providerPort}`;
repositoryUrl = `https://127.0.0.1:${providerPort}/zumbo/platform`;

async function apiRequest(path, method = 'GET', body, token, headers = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers
    },
    body: body === undefined
      ? undefined
      : typeof body === 'string'
        ? body
        : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label, headers) {
  const result = await apiRequest(path, method, body, token, headers);
  assert.ok(
    result.response.ok,
    result.payload.error?.message || `${label} failed with HTTP ${result.response.status}`
  );
  return result.data;
}

async function eventually(operation, label, attempts = 100, delayMs = 120) {
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const result = await operation();
    if (result) return result;
    await new Promise(resolveDelay => setTimeout(resolveDelay, delayMs));
  }
  throw new Error(`${label} did not become observable after ${attempts} attempts.`);
}

async function archiveTenant() {
  const result = await apiRequest(
    `/api/organizations/${encodeURIComponent(tenantId)}/archive`,
    'POST',
    undefined,
    ownerToken
  );
  if (result.response.ok || result.response.status === 404) {
    return { tenantId, status: result.response.status };
  }
  throw new Error(
    result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`
  );
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
    if (/WebSocket|signalr|Failed to load resource/.test(detail)) return;
    failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

async function capture(page, name, minimumBytes = 15_000) {
  const path = resolve(output, name);
  await page.screenshot({ path, fullPage: true });
  assert.ok((await readFile(path)).length > minimumBytes, `${name} is unexpectedly small.`);
}

function taskPayload(project, board, owner, title) {
  return {
    projectId: project.id,
    boardId: board.id,
    title,
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id,
    dueDate: new Date(Date.now() + 3 * 86_400_000).toISOString()
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `gitowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  ownerToken = ownerRegistration.accessToken;
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Development Delivery',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const ordinaryRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `gitmember${stamp}`,
    email: `gitmember-${stamp}@zumbo.local`,
    password,
    organizationId: tenantId
  }, undefined, 'Ordinary user registration');
  const denied = await apiRequest(
    '/api/integrations/development',
    'GET',
    undefined,
    ordinaryRegistration.accessToken
  );
  assert.equal(denied.response.status, 403);
  checks.push('real-integration-management-permission-denied');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `GI${stamp.slice(-5).toUpperCase()}`,
    name: 'Provider Delivery',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Provider Delivery Board',
    type: 'Kanban'
  }, ownerToken, 'Board creation');
  const task = await requireApi('/api/work-items', 'POST', taskPayload(
    project,
    board,
    owner,
    'Signed provider event acceptance'
  ), ownerToken, 'Work item creation');

  const desktopContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(desktopContext, owner.username);
  const desktopPage = await desktopContext.newPage();
  diagnostics(desktopPage, 'desktop-owner');
  desktopPage.on('dialog', dialog => dialog.accept());
  await desktopPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=settings`, {
    waitUntil: 'domcontentloaded'
  });
  await desktopPage.getByRole('tab', { name: 'Entegrasyonlar' }).waitFor({ timeout: 45_000 });
  await desktopPage.getByRole('tab', { name: 'Entegrasyonlar' }).click();
  await desktopPage.getByRole('tab', { name: 'Geliştirme' }).click();
  await desktopPage.getByRole('heading', { name: 'Geliştirme bağlantıları' }).waitFor();
  await desktopPage.getByRole('button', { name: 'Sağlayıcı bağla' }).first().click();
  await desktopPage.getByLabel('Bağlantı adı').fill('Real GitHub source');
  await desktopPage.getByLabel('Sağlayıcı temel adresi').fill(providerBaseUrl);
  await desktopPage.getByLabel('Erişim anahtarı').fill(providerToken);
  await desktopPage.getByRole('button', { name: 'Bağla', exact: true }).click();
  await desktopPage.getByText('Webhook sırrı yalnız şimdi gösterilir', { exact: true }).waitFor();
  const webhookSecret = await desktopPage
    .locator('.development-layout .integration-secret > div code')
    .last()
    .innerText();
  const storage = await desktopPage.evaluate(() => JSON.stringify({
    local: { ...localStorage },
    session: { ...sessionStorage }
  }));
  assert.doesNotMatch(storage, new RegExp(webhookSecret));
  assert.doesNotMatch(storage, new RegExp(providerToken));
  const connection = (await requireApi(
    '/api/integrations/development',
    'GET',
    undefined,
    ownerToken,
    'Connection list'
  )).find(item => item.name === 'Real GitHub source');
  assert.ok(connection, 'Created development connection was not returned.');
  await desktopPage.getByRole('button', {
    name: 'Geliştirme webhook sırrını kapat'
  }).click();
  checks.push('real-ui-create-secret-once-and-credential-not-in-storage');

  await desktopPage.getByRole('button', { name: 'Sağlığı denetle' }).click();
  await desktopPage.getByText('Sağlıklı', { exact: true }).waitFor({ timeout: 45_000 });
  await desktopPage.getByRole('button', { name: 'Repository’leri getir' }).click();
  await desktopPage.getByLabel('Zumbo projesi').selectOption({ label: project.name });
  await desktopPage.locator('.development-mapping-form select').nth(1)
    .selectOption({ label: 'zumbo/platform' });
  await desktopPage.getByRole('button', { name: 'Eşleştir' }).click();
  await desktopPage.locator('.development-mapping-row')
    .getByText('zumbo/platform', { exact: true })
    .waitFor();
  const mapping = (await requireApi(
    `/api/integrations/development/${connection.id}/mappings`,
    'GET',
    undefined,
    ownerToken,
    'Repository mappings'
  ))[0];
  assert.equal(mapping.projectId, project.id);
  assert.ok(providerRequests.some(request =>
    request.path === '/user'
      && request.authorization === `Bearer ${providerToken}`
      && request.userAgent.includes('Zumbo-DevelopmentIntegration/1.0')));
  assert.ok(providerRequests.some(request => request.path.startsWith('/user/repos?')));
  assert.equal(
    await desktopPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    true
  );
  await capture(desktopPage, 'desktop-development-center.png');
  checks.push('real-provider-health-discovery-mapping-and-pinned-auth');

  const reference = `${project.key}-${task.id.slice(0, 8).toUpperCase()}`;
  const providerEvent = JSON.stringify({
    number: 702,
    repository: { id: 4242 },
    pull_request: {
      title: `Signed provider ingress ${reference}`,
      body: `Completes ${reference}`,
      html_url: `${repositoryUrl}/pull/702`,
      state: 'open',
      merged: false,
      updated_at: new Date().toISOString(),
      head: {
        ref: `feature/${reference}`,
        sha: '0123456789abcdef0123456789abcdef01234567'
      }
    }
  });
  const deliveryId = `delivery-${stamp}`;
  const signature = `sha256=${createHmac('sha256', webhookSecret)
    .update(providerEvent)
    .digest('hex')}`;
  const badSignature = await apiRequest(
    `/api/integrations/development/${connection.id}/webhook`,
    'POST',
    providerEvent,
    undefined,
    {
      'Content-Type': 'application/json',
      'X-GitHub-Delivery': `bad-${deliveryId}`,
      'X-GitHub-Event': 'pull_request',
      'X-Hub-Signature-256': 'sha256=invalid'
    }
  );
  assert.equal(badSignature.response.status, 401);
  const accepted = await requireApi(
    `/api/integrations/development/${connection.id}/webhook`,
    'POST',
    providerEvent,
    undefined,
    'Signed webhook',
    {
      'Content-Type': 'application/json',
      'X-GitHub-Delivery': deliveryId,
      'X-GitHub-Event': 'pull_request',
      'X-Hub-Signature-256': signature
    }
  );
  assert.equal(accepted.status, 'Accepted');
  const duplicate = await requireApi(
    `/api/integrations/development/${connection.id}/webhook`,
    'POST',
    providerEvent,
    undefined,
    'Duplicate webhook',
    {
      'Content-Type': 'application/json',
      'X-GitHub-Delivery': deliveryId,
      'X-GitHub-Event': 'pull_request',
      'X-Hub-Signature-256': signature
    }
  );
  assert.equal(duplicate.duplicate, true);
  const automaticLink = await eventually(async () => {
    const links = await requireApi(
      `/api/work-items/${task.id}/development-links`,
      'GET',
      undefined,
      ownerToken,
      'Work item development links'
    );
    return links.find(item => item.externalId === 'pr:702') || null;
  }, 'Automatically matched development link');
  assert.equal(automaticLink.source, 'Webhook');
  assert.equal(automaticLink.connectionActive, true);
  checks.push('real-signed-webhook-negative-signature-dedupe-and-automatic-link');

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-owner');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/tasks/${task.id}`, {
    waitUntil: 'domcontentloaded'
  });
  await mobilePage.getByRole('heading', { name: 'Geliştirme' }).waitFor({ timeout: 45_000 });
  await mobilePage.getByText(`Signed provider ingress ${reference}`, { exact: true }).waitFor();
  await mobilePage.getByText('Webhook ile eşleştirildi', { exact: true }).waitFor();
  await mobilePage.getByRole('button', { name: 'Geliştirme bağlantısı ekle' }).click();
  const form = mobilePage.locator('.mobile-task-development-form');
  await form.locator('select').first().selectOption(mapping.id);
  await form.getByLabel('Harici kimlik').fill('pr:703');
  await form.getByLabel('Başlık', { exact: true }).fill('Manual provider review');
  await form.getByLabel('HTTPS bağlantısı').fill(`${repositoryUrl}/pull/703`);
  await form.getByRole('button', { name: 'Bağlantıyı ekle' }).click();
  await mobilePage.getByText('Manual provider review', { exact: true }).waitFor();
  await mobilePage.getByText('Elle bağlandı', { exact: true }).waitFor();
  assert.equal(
    await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true
  );
  await capture(mobilePage, 'mobile-work-item-development.png', 10_000);
  checks.push('real-mobile-automatic-and-manual-work-item-links');

  await desktopPage.getByRole('button', { name: 'Bağlantıyı kes' }).click();
  await desktopPage.getByText('Bağlantı kesildi', { exact: true }).waitFor();
  const disconnectedLinks = await requireApi(
    `/api/work-items/${task.id}/development-links`,
    'GET',
    undefined,
    ownerToken,
    'Disconnected work item links'
  );
  assert.ok(disconnectedLinks.every(item => item.connectionActive === false));
  const inactiveMappings = await requireApi(
    `/api/integrations/development/${connection.id}/mappings`,
    'GET',
    undefined,
    ownerToken,
    'Disconnected mappings'
  );
  assert.ok(inactiveMappings.every(item => item.isActive === false));
  const afterDisconnect = await apiRequest(
    `/api/integrations/development/${connection.id}/webhook`,
    'POST',
    providerEvent,
    undefined,
    {
      'Content-Type': 'application/json',
      'X-GitHub-Delivery': `after-${deliveryId}`,
      'X-GitHub-Event': 'pull_request',
      'X-Hub-Signature-256': signature
    }
  );
  assert.equal(afterDisconnect.response.status, 401);
  checks.push('real-disconnect-invalidates-secrets-mappings-and-link-health');

  await mobileContext.close();
  await desktopContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  cleanup = await cleanupLedger.run();
  await browser?.close();
  await new Promise(resolveClose => provider.close(resolveClose));
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-INTEGRATION-001',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanup.failed === 0 && checks.length === 6,
    apiBaseUrl,
    frontendBaseUrl,
    providerBaseUrl,
    checks,
    cleanup,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(
  cleanup.failed,
  0,
  `Cleanup failures: ${cleanup.results.map(result => result.error).filter(Boolean).join(' | ')}`
);
assert.deepEqual(failures, []);
assert.equal(checks.length, 6, `Expected 6 real checks, received ${checks.length}.`);
console.log(
  'V3-INTEGRATION-001 real-browser passed: permission, provider, webhook, mobile link and disconnect gates.'
);
