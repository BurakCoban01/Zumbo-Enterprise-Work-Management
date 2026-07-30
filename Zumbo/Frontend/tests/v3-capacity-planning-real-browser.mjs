import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-feature-006-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-FEATURE-006', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret(
  'ZUMBO_IDENTITY_ADMIN_EMAIL',
  'for V3-FEATURE-006 tenant cleanup'
);
const adminBootstrapToken = requireLocalSecret(
  'ZUMBO_IDENTITY_BOOTSTRAP_TOKEN',
  'for V3-FEATURE-006 tenant cleanup'
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
      assert.ok(authentication.response.ok, 'Cleanup administrator authentication failed');
      return authentication.data.accessToken;
    })();
  }
  return cleanupAdminTokenPromise;
}

async function archiveTenant() {
  const token = await cleanupAdminToken();
  const result = await apiRequest(
    `/api/organizations/${encodeURIComponent(tenantId)}/archive`,
    'POST',
    undefined,
    token
  );
  if (result.response.ok || result.response.status === 404) {
    return { organizationId: tenantId, status: result.response.status };
  }
  throw new Error(result.payload.error?.message || `Tenant cleanup failed: ${result.response.status}`);
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

async function capture(page, name) {
  const path = resolve(outputDir, name);
  await page.screenshot({ path, fullPage: true });
  const bytes = await readFile(path);
  assert.ok(bytes.length > 15_000, `${name} is unexpectedly small`);
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: 'Ada Yılmaz',
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Kapasite Kanıtı',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `f6viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Kapasite Teslimat Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer team invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: 'Deniz Kaya',
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, viewerRegistration.accessToken, 'Viewer team invitation acceptance');
  const viewer = viewerRegistration.user;
  const viewerToken = viewerRegistration.accessToken;

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `CP${stamp.slice(-5)}`,
    name: 'Atlas Kapasite Teslimatı',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Project creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewer.id,
    role: 'Viewer'
  }, ownerToken, 'Project viewer grant');
  await requireApi(`/api/projects/${project.id}/teams`, 'POST', {
    teamId: team.id
  }, ownerToken, 'Project team link');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Kapasite Panosu',
    type: 'Kanban'
  }, ownerToken, 'Board creation');
  const estimatedWork = await requireApi('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Tahminli teslimat işi',
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id,
    dueDate: '2026-07-10T12:00:00Z',
    teamId: team.id
  }, ownerToken, 'Estimated work item creation');
  await requireApi(`/api/work-items/${estimatedWork.id}/planning`, 'PATCH', {
    sprintId: null,
    estimatePoints: 5
  }, ownerToken, 'Work item estimate');
  await requireApi('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Tarihsiz tahminsiz iş',
    type: 'Task',
    priority: 'Medium',
    assigneeUserId: owner.id,
    dueDate: null,
    teamId: team.id
  }, ownerToken, 'Unscheduled work item creation');

  const saveBody = {
    name: 'Atlas teslimat kapasitesi',
    description: 'Gerçek API ile iki haftalık sentetik kapasite planı',
    periodStart: '2026-07-06',
    periodEnd: '2026-07-19',
    portfolioId: null,
    projectIds: [project.id],
    members: [{
      userId: owner.id,
      teamId: team.id,
      weeklyCapacityHours: 40
    }],
    allocations: [{
      id: null,
      userId: owner.id,
      projectId: project.id,
      startDate: '2026-07-06',
      endDate: '2026-07-19',
      percent: 60
    }],
    viewerUserIds: [viewer.id]
  };
  const plan = await requireApi(
    '/api/capacity-plans',
    'POST',
    saveBody,
    ownerToken,
    'Capacity plan creation'
  );
  const snapshot = await requireApi(
    `/api/capacity-plans/${plan.id}/snapshot`,
    'GET',
    undefined,
    ownerToken,
    'Capacity snapshot'
  );
  assert.equal(snapshot.sourceStatus, 'Ready');
  assert.equal(snapshot.summary.capacityHours, 80);
  assert.equal(snapshot.summary.allocatedHours, 48);
  assert.equal(snapshot.summary.openItems, 2);
  assert.equal(snapshot.summary.estimatedPoints, 5);
  assert.equal(snapshot.summary.unestimatedItems, 1);
  assert.equal(snapshot.summary.unscheduledItems, 1);
  checks.push('real-api-snapshot-separate-units-and-work-source');

  const viewerPlan = await requireApi(
    `/api/capacity-plans/${plan.id}`,
    'GET',
    undefined,
    viewerToken,
    'Viewer plan read'
  );
  assert.equal(viewerPlan.canEdit, false);
  const forbiddenUpdate = await apiRequest(
    `/api/capacity-plans/${plan.id}`,
    'PUT',
    saveBody,
    viewerToken
  );
  assert.equal(forbiddenUpdate.response.status, 403);
  const forbiddenScenario = await apiRequest(
    `/api/capacity-plans/${plan.id}/scenarios`,
    'POST',
    { allocations: saveBody.allocations },
    viewerToken
  );
  assert.equal(forbiddenScenario.response.status, 403);
  checks.push('real-api-viewer-read-forbidden-write-and-scenario');

  const scenario = await requireApi(
    `/api/capacity-plans/${plan.id}/scenarios`,
    'POST',
    {
      allocations: [
        ...saveBody.allocations,
        {
          id: null,
          userId: owner.id,
          projectId: project.id,
          startDate: '2026-07-06',
          endDate: '2026-07-19',
          percent: 50
        }
      ]
    },
    ownerToken,
    'Capacity scenario'
  );
  assert.equal(scenario.baseline.summary.allocatedHours, 48);
  assert.equal(scenario.candidate.summary.allocatedHours, 88);
  const persisted = await requireApi(
    `/api/capacity-plans/${plan.id}`,
    'GET',
    undefined,
    ownerToken,
    'Persisted capacity plan'
  );
  assert.equal(persisted.allocations.length, 1);
  checks.push('real-api-scenario-is-nonpersistent');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'owner-desktop');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=capacity&project=${project.id}`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await ownerPage.getByText('Atlas teslimat kapasitesi', { exact: true }).first()
    .waitFor({ timeout: 45_000 });
  await ownerPage.locator('.capacity-summary').waitFor({ timeout: 45_000 });
  assert.match(await ownerPage.locator('.capacity-summary').innerText(), /80[.,]0 sa/);
  assert.match(await ownerPage.locator('.capacity-summary').innerText(), /5[.,]0 puan/);
  assert.equal(await ownerPage.locator('.capacity-week').count(), 2);
  await ownerPage.getByRole('tab', { name: 'Projeler' }).click();
  assert.match(await ownerPage.locator('.capacity-table').innerText(), /Atlas Kapasite Teslimatı/);
  assert.equal(await ownerPage.getByRole('button', { name: 'Plan oluştur' }).count(), 1);
  checks.push('real-desktop-owner-weekly-and-project-views');
  await capture(ownerPage, 'desktop-owner.png');

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(mobileContext, viewer.username);
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'viewer-mobile');
  await mobilePage.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/capacity`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await mobilePage.getByText('Atlas teslimat kapasitesi', { exact: true }).first()
    .waitFor({ timeout: 45_000 });
  await mobilePage.locator('.mobile-capacity-readonly').waitFor({ timeout: 45_000 });
  assert.equal(await mobilePage.getByRole('button', { name: 'Kapasite planı oluştur' }).count(), 0);
  assert.equal(await mobilePage.getByRole('tab', { name: 'Senaryo' }).count(), 0);
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    minimumActionHeight: Math.min(...Array.from(
      document.querySelectorAll('.mobile-capacity-tabs button')
    ).map(element => element.getBoundingClientRect().height))
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  assert.ok(dimensions.minimumActionHeight >= 44);
  checks.push('real-mobile-viewer-authority-no-overflow');
  await capture(mobilePage, 'mobile-viewer.png');

  await mobileContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-006',
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
console.log(
  'V3-FEATURE-006 real-browser passed: real snapshot, authority, scenario and desktop/mobile parity.'
);
