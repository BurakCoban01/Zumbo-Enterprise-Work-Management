import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-feature-004-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-FEATURE-004', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret(
  'ZUMBO_IDENTITY_ADMIN_EMAIL',
  'for V3-FEATURE-004 tenant cleanup'
);
const adminBootstrapToken = requireLocalSecret(
  'ZUMBO_IDENTITY_BOOTSTRAP_TOKEN',
  'for V3-FEATURE-004 tenant cleanup'
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

function initiative(name, ownerUserId, projectIds, parentInitiativeId = null) {
  return {
    name,
    summary: `${name} için sentetik kapsam`,
    parentInitiativeId,
    ownerUserId,
    status: 'Active',
    health: 'OnTrack',
    confidence: 80,
    targetAt: '2026-10-15T00:00:00Z',
    projectIds,
    milestoneLinks: []
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `f4owner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Portföy Kanıtı',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const initiativeOwnerEmail = `f4initiative${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Portföy Teslimat Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: initiativeOwnerEmail,
    role: 'Member'
  }, ownerToken, 'Initiative owner invitation');
  const initiativeOwnerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `f4initiative${stamp}`,
    email: initiativeOwnerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Initiative owner registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, initiativeOwnerRegistration.accessToken, 'Initiative owner invitation acceptance');
  const initiativeOwner = initiativeOwnerRegistration.user;

  const projectA = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `PA${stamp.slice(-5)}`,
    name: 'Atlas Teslimat',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Primary project creation');
  const projectB = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `PB${stamp.slice(-5)}`,
    name: 'Mobil Dönüşüm',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Secondary project creation');
  await requireApi(`/api/projects/${projectA.id}/members`, 'POST', {
    userId: initiativeOwner.id,
    role: 'Viewer'
  }, ownerToken, 'Primary project viewer grant');
  await requireApi(`/api/projects/${projectB.id}/members`, 'POST', {
    userId: initiativeOwner.id,
    role: 'Viewer'
  }, ownerToken, 'Secondary project viewer grant');

  const boardA = await requireApi('/api/boards', 'POST', {
    projectId: projectA.id,
    name: 'Atlas Panosu',
    type: 'Kanban'
  }, ownerToken, 'Primary board creation');
  const boardB = await requireApi('/api/boards', 'POST', {
    projectId: projectB.id,
    name: 'Mobil Panosu',
    type: 'Kanban'
  }, ownerToken, 'Secondary board creation');
  await requireApi('/api/work-items', 'POST', {
    projectId: projectA.id,
    boardId: boardA.id,
    title: 'Platform hazırlığı',
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id,
    estimatePoints: 3
  }, ownerToken, 'Primary work item creation');
  await requireApi('/api/work-items', 'POST', {
    projectId: projectB.id,
    boardId: boardB.id,
    title: 'Mobil pilot',
    type: 'Task',
    priority: 'Medium',
    assigneeUserId: initiativeOwner.id,
    estimatePoints: 5
  }, ownerToken, 'Secondary work item creation');

  let portfolio = await requireApi('/api/portfolios', 'POST', {
    name: 'Teslimat portföyü',
    description: 'Gerçek API ile sentetik çapraz proje planı',
    viewerUserIds: [initiativeOwner.id]
  }, ownerToken, 'Portfolio creation');
  portfolio = await requireApi(
    `/api/portfolios/${portfolio.id}/initiatives`,
    'POST',
    initiative('Platform güvenilirliği', owner.id, [projectA.id, projectB.id]),
    ownerToken,
    'Parent initiative creation'
  );
  const parent = portfolio.initiatives[0];
  portfolio = await requireApi(
    `/api/portfolios/${portfolio.id}/initiatives`,
    'POST',
    initiative('Mobil ekip deneyimi', initiativeOwner.id, [projectB.id], parent.id),
    ownerToken,
    'Child initiative creation'
  );
  const child = portfolio.initiatives.find(item => item.ownerUserId === initiativeOwner.id);
  portfolio = await requireApi(
    `/api/portfolios/${portfolio.id}/dependencies`,
    'POST',
    {
      sourceProjectId: projectA.id,
      targetProjectId: projectB.id,
      description: 'Platform teslimatı mobil pilotu etkinleştirir.',
      status: 'Active',
      requiredBy: '2026-09-01T00:00:00Z'
    },
    ownerToken,
    'Dependency creation'
  );
  const statusUpdated = await requireApi(
    `/api/portfolios/${portfolio.id}/initiatives/${child.id}/status-updates`,
    'POST',
    {
      status: 'Active',
      health: 'AtRisk',
      confidence: 64,
      note: 'Mobil pilot bağımlılığı gerçek API üzerinde izleniyor.'
    },
    initiativeOwnerRegistration.accessToken,
    'Initiative owner status update'
  );
  const statusChild = statusUpdated.initiatives.find(item => item.id === child.id);
  assert.equal(statusChild.health, 'AtRisk');
  assert.equal(statusChild.statusUpdates.length, 1);
  checks.push('real-api-hierarchy-dependency-initiative-owner-status');

  const viewerPortfolio = await requireApi(
    `/api/portfolios/${portfolio.id}`,
    'GET',
    undefined,
    initiativeOwnerRegistration.accessToken,
    'Initiative owner portfolio read'
  );
  assert.equal(viewerPortfolio.canEdit, false);
  assert.equal(viewerPortfolio.initiatives.find(item => item.id === child.id).canUpdateStatus, true);
  assert.equal(viewerPortfolio.initiatives.find(item => item.id === parent.id).canUpdateStatus, false);
  const forbidden = await apiRequest(
    `/api/portfolios/${portfolio.id}`,
    'PUT',
    {
      name: 'Yetkisiz değişiklik',
      description: null,
      viewerUserIds: []
    },
    initiativeOwnerRegistration.accessToken
  );
  assert.equal(forbidden.response.status, 403);
  checks.push('real-api-readonly-portfolio-scoped-status-capability');

  const ownerRoadmap = await requireApi(
    `/api/portfolios/${portfolio.id}/roadmap`,
    'GET',
    undefined,
    ownerToken,
    'Owner roadmap'
  );
  assert.equal(ownerRoadmap.sourceStatus, 'Ready');
  assert.equal(ownerRoadmap.initiatives.length, 2);
  assert.equal(ownerRoadmap.dependencies.length, 1);
  checks.push('real-api-ready-roadmap-rollup');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'owner-desktop');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=portfolios&project=${projectA.id}`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await ownerPage.getByText('Teslimat portföyü', { exact: true }).first()
    .waitFor({ timeout: 45_000 });
  await ownerPage.locator('.portfolio-table-wrap').first().waitFor({ timeout: 45_000 });
  assert.match(await ownerPage.locator('.portfolio-table-wrap').first().innerText(), /Atlas Teslimat/);
  assert.match(await ownerPage.locator('.portfolio-table-wrap').first().innerText(), /Mobil Dönüşüm/);
  assert.equal(await ownerPage.locator('.portfolio-table-wrap th[scope="col"]').count(), 5);
  await ownerPage.getByRole('tab', { name: 'Bağımlılıklar' }).click();
  assert.match(await ownerPage.locator('.portfolio-panel').innerText(), /Platform teslimatı/);
  checks.push('real-desktop-owner-named-roadmap-dependency');
  await ownerPage.screenshot({
    path: resolve(outputDir, 'desktop-owner.png'),
    fullPage: true
  });

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(mobileContext, initiativeOwner.username);
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'initiative-owner-mobile');
  await mobilePage.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/portfolios`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await mobilePage.getByText('Teslimat portföyü', { exact: true }).first()
    .waitFor({ timeout: 45_000 });
  await mobilePage.locator('.mobile-portfolio-readonly').waitFor({ timeout: 45_000 });
  await mobilePage.getByRole('tab', { name: 'Hiyerarşi' }).click();
  assert.equal(await mobilePage.getByRole('button', { name: 'Durum güncelle' }).count(), 1);
  await mobilePage.getByRole('button', { name: 'Durum güncelle' }).click();
  await mobilePage.getByLabel('Durum notu').fill('Mobil istemci durum güncellemesi doğrulandı.');
  await mobilePage.getByRole('button', { name: 'Güncellemeyi yayınla' }).click();
  await mobilePage.getByText('Mobil istemci durum güncellemesi doğrulandı.', { exact: true })
    .waitFor({ timeout: 45_000 });
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  checks.push('real-mobile-initiative-owner-status-no-overflow');
  await mobilePage.screenshot({
    path: resolve(outputDir, 'mobile-initiative-owner.png'),
    fullPage: true
  });

  await mobileContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-004',
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
console.log('V3-FEATURE-004 real-browser passed: API lifecycle, scoped status authority and desktop/mobile parity.');
