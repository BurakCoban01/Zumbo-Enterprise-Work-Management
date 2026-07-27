import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-003-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-003', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX-003 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX-003 tenant cleanup');
const password = 'P@ssword123';
let cleanupAdminTokenPromise;
let browser;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
const failures = [];
const checks = [];

await mkdir(outputDir, { recursive: true });

async function apiRequest(path, method = 'GET', body, token) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
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

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      const authentication = await apiRequest('/api/auth/login', 'POST', { usernameOrEmail: adminEmail, password });
      assert.ok(authentication.response.ok, authentication.payload.error?.message || 'Cleanup administrator authentication failed');
      return authentication.data.accessToken;
    })();
  }
  return cleanupAdminTokenPromise;
}

async function archiveTenant() {
  const token = await cleanupAdminToken();
  const result = await apiRequest(`/api/organizations/${encodeURIComponent(tenantId)}/archive`, 'POST', undefined, token);
  if (result.response.ok || result.response.status === 404) return { tenantId, status: result.response.status };
  throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
}

async function browserContextLogin(context, usernameOrEmail) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin }, data: { usernameOrEmail, password }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser context login failed');
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, payload.data);
  return payload.data;
}

function attachDiagnostics(page, label) {
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

async function createProject(owner, token, key, name, withBoard) {
  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId, key, name, ownerUserId: owner.id
  }, token, `${name} project creation`);
  const board = withBoard ? await requireApi('/api/boards', 'POST', {
    projectId: project.id, name: `${name} Panosu`, type: 'Kanban'
  }, token, `${name} board creation`) : null;
  return { project, board };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux3owner${stamp}`, email: adminEmail, password,
    organizationId: tenantId, bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Proje Görünümü', tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `ux3viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId, name: 'Platform Ekibi', ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitedTeam = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail, role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux3viewer${stamp}`, email: viewerEmail, password, organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitedTeam.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');
  const delivery = await createProject(owner, ownerToken, `PX${stamp.slice(-5)}`, 'Platform Teslimatı', true);
  const noBoard = await createProject(owner, ownerToken, `NB${stamp.slice(-5)}`, 'Hazırlık Alanı', false);
  for (const fixture of [delivery, noBoard]) {
    await requireApi(`/api/projects/${fixture.project.id}/members`, 'POST', {
      userId: viewerRegistration.user.id, role: 'Viewer'
    }, ownerToken, 'Viewer project grant');
  }

  await requireApi(`/api/projects/${delivery.project.id}/teams`, 'POST', { teamId: team.id }, ownerToken, 'Project team link');
  let project = await requireApi(`/api/projects/${delivery.project.id}/versions`, 'POST', { name: '1.4' }, ownerToken, 'Version creation');
  const version = project.versions.find(item => item.name === '1.4');
  project = await requireApi(`/api/projects/${delivery.project.id}/releases`, 'POST', {
    versionId: version.id, name: 'Sürüm 1.4', scheduledAt: new Date(Date.now() + 14 * 86400000).toISOString()
  }, ownerToken, 'Release creation');
  project = await requireApi(`/api/projects/${delivery.project.id}/milestones`, 'POST', {
    name: 'Pilot çıkışı', dueAt: new Date(Date.now() + 10 * 86400000).toISOString()
  }, ownerToken, 'Milestone creation');
  const sprint = await requireApi('/api/sprints', 'POST', {
    projectId: delivery.project.id, name: 'Sprint 14', goal: 'Pilot akışını güvenle aç',
    startDate: new Date(Date.now() - 3 * 86400000).toISOString().slice(0, 10),
    endDate: new Date(Date.now() + 11 * 86400000).toISOString().slice(0, 10)
  }, ownerToken, 'Sprint creation');
  const taskTitle = `Kritik erişim akışını doğrula ${stamp.slice(-4)}`;
  const riskTask = await requireApi('/api/work-items', 'POST', {
    projectId: delivery.project.id, boardId: delivery.board.id, title: taskTitle, type: 'Task',
    priority: 'High', assigneeUserId: owner.id, dueDate: new Date(Date.now() + 2 * 86400000).toISOString()
  }, ownerToken, 'Risk task creation');
  await requireApi(`/api/sprints/${sprint.id}/items/${riskTask.id}`, 'PUT', {
    estimatePoints: 3
  }, ownerToken, 'Sprint work-item planning');
  await requireApi(`/api/sprints/${sprint.id}/start`, 'POST', {}, ownerToken, 'Sprint start');
  checks.push('real-project-fixture');

  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'owner');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${delivery.project.id}&board=${delivery.board.id}&view=overview`,
    { waitUntil: 'domcontentloaded' }
  );
  await ownerPage.locator('.project-overview h2').getByText('Platform Teslimatı', { exact: true }).waitFor({ timeout: 45_000 });
  await ownerPage.getByText('Pilot çıkışı', { exact: true }).waitFor();
  await ownerPage.getByText('Sürüm 1.4', { exact: true }).waitFor();
  await ownerPage.getByText('Sprint 14', { exact: true }).waitFor();
  assert.equal(await ownerPage.getByRole('tab').count(), 11);
  assert.ok(await ownerPage.getByText('Yakın risk var', { exact: true }).isVisible());
  checks.push('owner-overview-health-delivery', 'unified-view-switcher');
  await ownerPage.screenshot({ path: resolve(outputDir, 'owner-overview.png'), fullPage: true });

  await ownerPage.getByRole('tab', { name: 'İş yükü', exact: true }).click();
  await ownerPage.locator('.insight-panel').getByText(owner.username, { exact: true }).waitFor();
  assert.match(ownerPage.url(), /section=reports/);
  assert.match(ownerPage.url(), /view=workload/);
  checks.push('workload-destination');

  await ownerPage.getByRole('tab', { name: 'Pano', exact: true }).click();
  await ownerPage.getByLabel('Arama').fill(stamp.slice(-4));
  await ownerPage.getByLabel('Öncelik').selectOption('High');
  await ownerPage.waitForFunction(value => location.hash.includes(`query=${value}`) && location.hash.includes('priority=High'), stamp.slice(-4));
  await ownerPage.reload({ waitUntil: 'domcontentloaded' });
  await ownerPage.getByLabel('Arama').waitFor({ timeout: 45_000 });
  assert.equal(await ownerPage.getByLabel('Arama').inputValue(), stamp.slice(-4));
  assert.equal(await ownerPage.getByLabel('Öncelik').inputValue(), 'High');
  checks.push('deep-link-filter-context');

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'viewer');
  await viewerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${noBoard.project.id}&view=board`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.locator('.project-overview h2').getByText('Hazırlık Alanı', { exact: true }).waitFor({ timeout: 45_000 });
  await viewerPage.locator('.overview-metrics').waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.getByRole('tab').count(), 4);
  assert.equal(await viewerPage.getByRole('tab', { name: 'Genel bakış', exact: true }).getAttribute('aria-selected'), 'true');
  assert.equal(await viewerPage.getByText('Pano yüklenmedi', { exact: true }).count(), 0);
  await viewerPage.locator('.create-button').click();
  assert.equal(await viewerPage.locator('.create-menu').getByRole('button', { name: 'Görev', exact: true }).count(), 0);
  checks.push('viewer-no-dead-end', 'viewer-create-boundary');
  await viewerPage.screenshot({ path: resolve(outputDir, 'viewer-no-board.png'), fullPage: true });

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
  await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  attachDiagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/projects/${delivery.project.id}`, { waitUntil: 'domcontentloaded' });
  await mobilePage.locator('.mobile-project-overview h2').getByText('Platform Teslimatı', { exact: true }).waitFor({ timeout: 45_000 });
  await mobilePage.getByText('Pilot çıkışı', { exact: true }).waitFor();
  await mobilePage.getByText('Sürüm 1.4', { exact: true }).waitFor();
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile overview overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  checks.push('mobile-real-overview');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-overview.png'), fullPage: true });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-UX-003',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 8,
    apiBaseUrl,
    frontendBaseUrl,
    checks,
    cleanup: cleanupResult,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
console.log('V3-UX-003 real-browser passed: project overview, unified views, deep links, Viewer fallback and mobile parity.');
