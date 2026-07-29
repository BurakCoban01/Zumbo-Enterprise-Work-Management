import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-feature-005-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-FEATURE-005', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret(
  'ZUMBO_IDENTITY_ADMIN_EMAIL',
  'for V3-FEATURE-005 tenant cleanup'
);
const adminBootstrapToken = requireLocalSecret(
  'ZUMBO_IDENTITY_BOOTSTRAP_TOKEN',
  'for V3-FEATURE-005 tenant cleanup'
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

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `f5owner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Hedef Kanıtı',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const resultOwnerEmail = `f5result${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Hedef Teslimat Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: resultOwnerEmail,
    role: 'Member'
  }, ownerToken, 'Key-result owner invitation');
  const resultOwnerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `f5result${stamp}`,
    email: resultOwnerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Key-result owner registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, resultOwnerRegistration.accessToken, 'Key-result owner invitation acceptance');
  const resultOwner = resultOwnerRegistration.user;

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `GO${stamp.slice(-5)}`,
    name: 'Atlas Hedef Teslimatı',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Project creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: resultOwner.id,
    role: 'Viewer'
  }, ownerToken, 'Project viewer grant');

  let portfolio = await requireApi('/api/portfolios', 'POST', {
    name: 'Hedef portföyü',
    description: 'Gerçek API ile sentetik hedef planı',
    viewerUserIds: [resultOwner.id]
  }, ownerToken, 'Portfolio creation');
  portfolio = await requireApi(
    `/api/portfolios/${portfolio.id}/initiatives`,
    'POST',
    {
      name: 'Ekip aktivasyonu',
      summary: 'Çeyrek hedef bağlantısı',
      parentInitiativeId: null,
      ownerUserId: owner.id,
      status: 'Active',
      health: 'OnTrack',
      confidence: 80,
      targetAt: '2026-09-30T00:00:00Z',
      projectIds: [project.id],
      milestoneLinks: []
    },
    ownerToken,
    'Initiative creation'
  );
  const initiative = portfolio.initiatives[0];

  let goal = await requireApi('/api/goals', 'POST', {
    name: 'Ekip aktivasyonunu artır',
    description: 'Gerçek API ile ölçülebilir çeyrek hedefi',
    periodStart: '2026-07-01',
    periodEnd: '2026-09-30',
    viewerUserIds: [resultOwner.id],
    initiativeLinks: [{
      portfolioId: portfolio.id,
      initiativeId: initiative.id
    }],
    projectIds: [project.id]
  }, ownerToken, 'Goal creation');
  goal = await requireApi(`/api/goals/${goal.id}/key-results`, 'POST', {
    name: 'Aktif ekip oranı',
    description: 'Pilot aktivasyon ölçümü',
    ownerUserId: resultOwner.id,
    baselineValue: 0,
    targetValue: 100,
    initialValue: 20,
    unit: '%',
    direction: 'Increase'
  }, ownerToken, 'Key-result creation');
  const keyResult = goal.keyResults[0];
  goal = await requireApi(`/api/goals/${goal.id}/status-updates`, 'POST', {
    status: 'Active',
    health: 'OnTrack',
    confidence: 76,
    note: 'Çeyrek hedefi gerçek API üzerinde yolunda.'
  }, ownerToken, 'Goal status update');
  goal = await requireApi(
    `/api/goals/${goal.id}/key-results/${keyResult.id}/progress-updates`,
    'POST',
    {
      currentValue: 45,
      confidence: 71,
      note: 'Pilot ekiplerin yüzde kırk beşi aktif.'
    },
    resultOwnerRegistration.accessToken,
    'Key-result owner progress update'
  );
  assert.equal(goal.progress, 45);
  assert.equal(goal.keyResults[0].progressUpdates.length, 1);
  assert.equal(goal.statusUpdates.length, 1);
  checks.push('real-api-goal-key-result-progress-history');

  const viewerGoal = await requireApi(
    `/api/goals/${goal.id}`,
    'GET',
    undefined,
    resultOwnerRegistration.accessToken,
    'Key-result owner goal read'
  );
  assert.equal(viewerGoal.canEdit, false);
  assert.equal(viewerGoal.canUpdateStatus, false);
  assert.equal(viewerGoal.keyResults[0].canUpdate, true);
  const forbidden = await apiRequest(
    `/api/goals/${goal.id}`,
    'PUT',
    {
      name: 'Yetkisiz değişiklik',
      description: null,
      periodStart: '2026-07-01',
      periodEnd: '2026-09-30',
      viewerUserIds: [],
      initiativeLinks: [],
      projectIds: []
    },
    resultOwnerRegistration.accessToken
  );
  assert.equal(forbidden.response.status, 403);
  checks.push('real-api-key-result-owner-scoped-authority');

  const rollup = await requireApi(
    `/api/goals/${goal.id}/rollup`,
    'GET',
    undefined,
    ownerToken,
    'Goal rollup'
  );
  assert.equal(rollup.sourceStatus, 'Ready');
  assert.equal(rollup.progress, 45);
  assert.equal(rollup.initiatives.length, 1);
  assert.equal(rollup.projects.length, 1);
  checks.push('real-api-ready-linked-rollup');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'owner-desktop');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=goals&project=${project.id}`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await ownerPage.getByText('Ekip aktivasyonunu artır', { exact: true }).first()
    .waitFor({ timeout: 45_000 });
  await ownerPage.locator('.goal-result-list').waitFor({ timeout: 45_000 });
  assert.match(await ownerPage.locator('.goal-summary').innerText(), /45%/);
  assert.match(await ownerPage.locator('.goal-result-list').innerText(), /Aktif ekip oranı/);
  await ownerPage.getByRole('tab', { name: 'Bağlantılar' }).click();
  assert.match(await ownerPage.locator('.goal-source-grid').innerText(), /Ekip aktivasyonu/);
  assert.match(await ownerPage.locator('.goal-source-grid').innerText(), /Atlas Hedef Teslimatı/);
  checks.push('real-desktop-owner-progress-and-links');
  await ownerPage.screenshot({
    path: resolve(outputDir, 'desktop-owner.png'),
    fullPage: true
  });

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(mobileContext, resultOwner.username);
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'result-owner-mobile');
  await mobilePage.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/goals`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await mobilePage.getByText('Ekip aktivasyonunu artır', { exact: true }).first()
    .waitFor({ timeout: 45_000 });
  await mobilePage.locator('.mobile-goal-readonly').waitFor({ timeout: 45_000 });
  assert.equal(await mobilePage.getByRole('button', { name: 'İlerleme güncelle' }).count(), 1);
  await mobilePage.getByRole('button', { name: 'İlerleme güncelle' }).click();
  await mobilePage.getByLabel('Güncel değer').fill('58');
  await mobilePage.getByLabel('İlerleme notu').fill('Mobil istemci ilerleme güncellemesi doğrulandı.');
  await mobilePage.getByRole('button', { name: 'İlerlemeyi yayınla' }).click();
  await mobilePage.getByText('Mobil istemci ilerleme güncellemesi doğrulandı.', { exact: true })
    .waitFor({ timeout: 45_000 });
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  checks.push('real-mobile-key-result-owner-progress-no-overflow');
  await mobilePage.screenshot({
    path: resolve(outputDir, 'mobile-result-owner.png'),
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
    taskId: 'V3-FEATURE-005',
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
console.log('V3-FEATURE-005 real-browser passed: API history, scoped authority and desktop/mobile parity.');
