import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-feature-003-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-FEATURE-003', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret(
  'ZUMBO_IDENTITY_ADMIN_EMAIL',
  'for V3-FEATURE-003 tenant cleanup'
);
const adminBootstrapToken = requireLocalSecret(
  'ZUMBO_IDENTITY_BOOTSTRAP_TOKEN',
  'for V3-FEATURE-003 tenant cleanup'
);
const password = 'P@ssword123';
let cleanupAdminTokenPromise;
let browser;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
const failures = [];
const checks = [];

await mkdir(outputDir, { recursive: true });

async function apiRequest(path, method = 'GET', body, token, headers = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers
    },
    body: body === undefined ? undefined : JSON.stringify(body)
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

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      const authentication = await apiRequest('/api/auth/login', 'POST', {
        usernameOrEmail: adminEmail,
        password
      });
      assert.ok(
        authentication.response.ok,
        authentication.payload.error?.message || 'Cleanup administrator authentication failed'
      );
      return authentication.data.accessToken;
    })();
  }
  return cleanupAdminTokenPromise;
}

async function archiveOrganization(organizationId) {
  const token = await cleanupAdminToken();
  const result = await apiRequest(
    `/api/organizations/${encodeURIComponent(organizationId)}/archive`,
    'POST',
    undefined,
    token
  );
  if (result.response.ok || result.response.status === 404) {
    return { organizationId, status: result.response.status };
  }
  throw new Error(
    result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`
  );
}

async function archiveTenant() {
  return archiveOrganization(tenantId);
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

function diagnostics(page, label) {
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

function dashboardDefinition(projectIds) {
  return {
    name: 'Portföy teslimat nabzı',
    description: 'Gerçek API ile oluşturulan sentetik dashboard',
    scope: 'Portfolio',
    projectIds,
    widgets: [{
      id: 'summary',
      type: 'ProjectSummary',
      title: 'Proje özeti',
      column: 1,
      row: 1,
      width: 12,
      height: 2,
      projectId: null,
      filter: null
    }, {
      id: 'workload',
      type: 'UserWorkload',
      title: 'İş yükü',
      column: 1,
      row: 3,
      width: 12,
      height: 2,
      projectId: projectIds[0],
      filter: null
    }],
    filter: { rangeDays: 30, dueRiskDays: 30, statuses: [] }
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `f3owner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Dashboard Kanıtı',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `f3viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Teslimat Görünümü Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `f3viewer${stamp}`,
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const projectA = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `DA${stamp.slice(-5)}`,
    name: 'Atlas Teslimat',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Primary project creation');
  const projectB = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `DB${stamp.slice(-5)}`,
    name: 'Mobil Dönüşüm',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Portfolio project creation');
  await requireApi(`/api/projects/${projectA.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Primary project viewer grant');
  const projectBWithViewer = await requireApi(`/api/projects/${projectB.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Portfolio project viewer grant');

  const boardA = await requireApi('/api/boards', 'POST', {
    projectId: projectA.id,
    name: 'Atlas Panosu',
    type: 'Kanban'
  }, ownerToken, 'Primary board creation');
  const boardB = await requireApi('/api/boards', 'POST', {
    projectId: projectB.id,
    name: 'Mobil Panosu',
    type: 'Kanban'
  }, ownerToken, 'Portfolio board creation');
  await requireApi('/api/work-items', 'POST', {
    projectId: projectA.id,
    boardId: boardA.id,
    title: 'Atlas teslimat riski',
    type: 'Task',
    priority: 'High',
    assigneeUserId: viewerRegistration.user.id,
    estimatePoints: 3
  }, ownerToken, 'Primary work item creation');
  await requireApi('/api/work-items', 'POST', {
    projectId: projectB.id,
    boardId: boardB.id,
    title: 'Mobil teslimat işi',
    type: 'Task',
    priority: 'Medium',
    assigneeUserId: owner.id,
    estimatePoints: 2
  }, ownerToken, 'Portfolio work item creation');

  const create = await apiRequest(
    '/api/dashboards',
    'POST',
    dashboardDefinition([projectA.id, projectB.id]),
    ownerToken
  );
  assert.ok(create.response.ok, create.payload.error?.message || 'Dashboard creation failed');
  assert.equal(create.response.headers.get('etag'), '"1"');
  const dashboard = create.data;
  const shared = await apiRequest(
    `/api/dashboards/${dashboard.id}/sharing`,
    'PUT',
    { viewerUserIds: [viewerRegistration.user.id] },
    ownerToken,
    { 'If-Match': '"1"' }
  );
  assert.ok(shared.response.ok, shared.payload.error?.message || 'Dashboard sharing failed');
  assert.equal(shared.response.headers.get('etag'), '"2"');

  const ownerRender = await requireApi(
    `/api/dashboards/${dashboard.id}/render`,
    'GET',
    undefined,
    ownerToken,
    'Owner dashboard render'
  );
  assert.equal(ownerRender.partial, false);
  assert.equal(ownerRender.widgets.length, 2);
  assert.equal(ownerRender.widgets[0].sources.length, 2);
  assert.ok(ownerRender.generatedAt);
  checks.push('real-api-create-share-render-freshness');

  const outsiderEmail = `f3outside${stamp}@zumbo.local`;
  const outsiderInvitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: outsiderEmail,
    role: 'Member'
  }, ownerToken, 'Unshared user invitation');
  const outsider = await requireApi('/api/auth/register', 'POST', {
    username: `f3outside${stamp}`,
    email: outsiderEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Outsider registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: outsiderInvitation.invitationToken
  }, outsider.accessToken, 'Unshared user invitation acceptance');
  const denied = await apiRequest(
    `/api/dashboards/${dashboard.id}`,
    'GET',
    undefined,
    outsider.accessToken
  );
  assert.equal(denied.response.status, 404);
  checks.push('real-api-unshared-resource-isolation');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'owner');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=reports&project=${projectA.id}&view=dashboards`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await ownerPage.getByText('Portföy teslimat nabzı', { exact: true }).waitFor({ timeout: 45_000 });
  await ownerPage.locator('.dashboard-widget').first().waitFor();
  assert.equal(await ownerPage.locator('.dashboard-widget').count(), 2);
  assert.ok(await ownerPage.locator('.dashboard-editor').isVisible());
  await ownerPage.waitForFunction(
    () => document.querySelectorAll('.dashboard-share select option').length >= 2,
    undefined,
    { timeout: 45_000 }
  );
  assert.match(
    await ownerPage.locator('.dashboard-share select').innerText(),
    new RegExp(viewerRegistration.user.username)
  );
  assert.match(await ownerPage.locator('.dashboard-render-grid').innerText(), /Atlas Teslimat/);
  assert.match(await ownerPage.locator('.dashboard-render-grid').innerText(), /Mobil Dönüşüm/);
  assert.ok(await ownerPage.locator('.dashboard-widget th[scope="col"]').count() > 0);
  assert.ok(await ownerPage.locator('.dashboard-freshness').isVisible());
  checks.push('real-desktop-owner-named-accessible-table');
  await ownerPage.screenshot({
    path: resolve(outputDir, 'desktop-owner.png'),
    fullPage: true
  });

  const viewerContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'viewer-mobile');
  await viewerPage.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/projects/${projectA.id}/insights?mode=dashboards&range=30`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await viewerPage.getByText('Portföy teslimat nabzı', { exact: true }).waitFor({ timeout: 45_000 });
  await viewerPage.locator('.mobile-dashboard-readonly').waitFor();
  await viewerPage.locator('.mobile-dashboard-widget').first().waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.locator('.mobile-dashboard-widget').count(), 2);
  assert.ok(await viewerPage.locator('.mobile-dashboard-table-wrap th[scope="col"]').count() > 0);
  const dimensions = await viewerPage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    minimumActionHeight: Math.min(
      ...Array.from(document.querySelectorAll('.mobile-dashboard-readonly .button'))
        .map(element => element.getBoundingClientRect().height)
    )
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  assert.ok(dimensions.minimumActionHeight >= 44);
  checks.push('real-mobile-viewer-readonly-no-page-overflow');
  await viewerPage.screenshot({
    path: resolve(outputDir, 'mobile-viewer.png'),
    fullPage: true
  });

  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));

  const revoke = await apiRequest(
    `/api/projects/${projectB.id}/members/${viewerRegistration.user.id}`,
    'DELETE',
    undefined,
    ownerToken,
    { 'If-Match': `"${projectBWithViewer.version}"` }
  );
  assert.ok(revoke.response.ok, revoke.payload.error?.message || 'Viewer permission revocation failed');
  const viewerList = await requireApi(
    '/api/dashboards?page=1&pageSize=100',
    'GET',
    undefined,
    viewerRegistration.accessToken,
    'Viewer dashboard list after permission loss'
  );
  assert.equal(viewerList.items.length, 0);
  checks.push('real-source-permission-loss-hides-dashboard');
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-003',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 5,
    apiBaseUrl,
    frontendBaseUrl,
    viewports: ['1440x1000', '390x844'],
    checks,
    cleanup: cleanupResult,
    failures,
    noDeployment: true
  }, null, 2)}\n`, 'utf8');
}

assert.equal(
  cleanupResult.failed,
  0,
  `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`
);
assert.equal(checks.length, 5, `Expected 5 real checks, received ${checks.length}`);
console.log('V3-FEATURE-003 real-browser passed: API lifecycle, tenant/source authorization, desktop owner and mobile viewer parity.');
