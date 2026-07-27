import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-008-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-008', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX-008 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX-008 tenant cleanup');
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

function dueDate(offset) {
  return new Date(Date.now() + offset * 86400000).toISOString();
}

function taskPayload(project, board, assignee, title, offset, estimatePoints) {
  return {
    projectId: project.id, boardId: board.id, title, type: 'Task', priority: 'High',
    assigneeUserId: assignee.id, dueDate: dueDate(offset), estimatePoints
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux8owner${stamp}`, email: adminEmail, password,
    organizationId: tenantId, bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', { name: 'Zumbo Gercek Raporlama', tenantKey: tenantId }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `ux8viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', { organizationId: tenantId, name: 'Rapor Ekibi', ownerUserId: owner.id }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', { email: viewerEmail, role: 'Member' }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux8viewer${stamp}`, email: viewerEmail, password, organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', { token: invitation.invitationToken }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId, key: `RP${stamp.slice(-5)}`, name: 'Gercek Teslimat Raporu', ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', { projectId: project.id, name: 'Rapor Panosu', type: 'Kanban' }, ownerToken, 'Board creation');

  const outsider = await requireApi('/api/auth/register', 'POST', {
    username: `ux8outside${stamp}`, email: `ux8outside${stamp}@zumbo.local`, password,
    organizationId: `outside-${tenantId}`
  }, undefined, 'Outsider registration');
  const denied = await apiRequest(`/api/work-items/reports/project-summary/${project.id}`, 'GET', undefined, outsider.accessToken);
  assert.equal(denied.response.status, 404, `Expected isolated report response, received HTTP ${denied.response.status}`);
  checks.push('real-permission-isolation');

  await requireApi(`/api/projects/${project.id}/members`, 'POST', { userId: viewerRegistration.user.id, role: 'Viewer' }, ownerToken, 'Viewer project grant');
  for (let index = 0; index < 12; index += 1) {
    const assignee = index % 2 ? viewerRegistration.user : owner;
    await requireApi('/api/work-items', 'POST', taskPayload(
      project, board, assignee, `Gercek rapor isi ${index + 1}`, index < 3 ? -1 : index + 1,
      index % 4 ? 2 : null
    ), ownerToken, `Work item ${index + 1} creation`);
  }
  checks.push('real-report-fixture');

  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await browserContextLogin(ownerContext, owner.username);
  const page = await ownerContext.newPage();
  diagnostics(page, 'owner');
  await page.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=reports&project=${project.id}&view=workload&range=30`, { waitUntil: 'domcontentloaded' });
  await page.getByText(/Kapasite/).waitFor({ timeout: 45_000 });
  await page.locator('.reporting-freshness').waitFor();
  assert.ok((await page.locator('.reporting-freshness').innerText()).trim().length > 0);
  await page.waitForFunction(() => document.querySelectorAll('.workload-table tbody tr').length === 2, null, { timeout: 45_000 });
  assert.equal(await page.locator('.workload-table tbody tr').count(), 2);
  await page.getByRole('button', { name: /leri a/ }).first().click();
  assert.ok(await page.locator('.reporting-drilldown .reporting-risk-list button').count() >= 6);
  checks.push('real-owner-workload-freshness-drilldown');
  await page.screenshot({ path: resolve(outputDir, 'owner-workload.png'), fullPage: true });

  await page.getByRole('tab', { name: 'Raporlar', exact: true }).click();
  await page.getByText(/tamamlama/, { exact: false }).waitFor();
  assert.ok(await page.locator('.reporting-table').count() >= 1);
  checks.push('real-owner-report-tables');
  await page.screenshot({ path: resolve(outputDir, 'owner-reports.png'), fullPage: true });

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'viewer');
  await viewerPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=reports&project=${project.id}&view=reports&range=90`, { waitUntil: 'domcontentloaded' });
  await viewerPage.getByText(/tamamlama/, { exact: false }).waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.locator('.reporting-error').count(), 0);
  checks.push('real-viewer-read-only-report');

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}/insights?mode=workload&range=30`, { waitUntil: 'domcontentloaded' });
  await mobilePage.getByText(/Kapasite/).waitFor({ timeout: 45_000 });
  await mobilePage.getByRole('tab', { name: 'Raporlar', exact: true }).click();
  await mobilePage.getByText(/tamamlama/, { exact: false }).waitFor();
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  checks.push('real-mobile-report-parity');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-reports.png'), fullPage: true });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1, taskId: 'V3-UX-008', runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 6,
    apiBaseUrl, frontendBaseUrl, checks, cleanup: cleanupResult, failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.equal(checks.length, 6, `Expected 6 real checks, received ${checks.length}`);
console.log('V3-UX-008 real-browser passed: permission isolation, authoritative workload/report data, freshness, drill-down, Viewer and mobile parity.');
