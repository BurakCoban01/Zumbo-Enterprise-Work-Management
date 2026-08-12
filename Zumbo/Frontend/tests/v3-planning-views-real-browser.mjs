import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-007-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-007', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX-007 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX-007 tenant cleanup');
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

function isoDay(offset) {
  return new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);
}

function isoInstant(offset) {
  return new Date(Date.now() + offset * 86400000).toISOString();
}

function taskPayload(project, board, owner, title, dueOffset) {
  return {
    projectId: project.id, boardId: board.id, title, type: 'Task', priority: 'High',
    assigneeUserId: owner.id, dueDate: isoInstant(dueOffset)
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux7owner${stamp}`, email: adminEmail, password,
    organizationId: tenantId, bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', { name: 'Zumbo Gerçek Planlama', tenantKey: tenantId }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `ux7viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', { organizationId: tenantId, name: 'Plan Ekibi', ownerUserId: owner.id }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', { email: viewerEmail, role: 'Member' }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux7viewer${stamp}`, email: viewerEmail, password, organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', { token: invitation.invitationToken }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  let project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId, key: `PL${stamp.slice(-5)}`, name: 'Gerçek Teslimat Planı', ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', { projectId: project.id, name: 'Plan Panosu', type: 'Scrum' }, ownerToken, 'Board creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', { userId: viewerRegistration.user.id, role: 'Viewer' }, ownerToken, 'Viewer project grant');
  project = await requireApi(`/api/projects/${project.id}/versions`, 'POST', { name: '1.0' }, ownerToken, 'Version creation');
  const version = project.versions.find(item => item.name === '1.0');
  project = await requireApi(`/api/projects/${project.id}/releases`, 'POST', {
    versionId: version.id, name: `Gerçek sürüm ${stamp.slice(-4)}`, scheduledAt: isoInstant(12)
  }, ownerToken, 'Release creation');
  project = await requireApi(`/api/projects/${project.id}/milestones`, 'POST', {
    name: `Gerçek pilot ${stamp.slice(-4)}`, dueAt: isoInstant(9)
  }, ownerToken, 'Milestone creation');
  const sprint = await requireApi('/api/sprints', 'POST', {
    projectId: project.id, name: `Gerçek plan sprinti ${stamp.slice(-4)}`, goal: 'Bağımlı teslimatı doğrula',
    startDate: isoDay(-2), endDate: isoDay(12)
  }, ownerToken, 'Sprint creation');

  const blocker = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Gerçek engelleyici ${stamp.slice(-4)}`, 6), ownerToken, 'Blocker creation');
  const dependent = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Gerçek bağımlı ${stamp.slice(-4)}`, 4), ownerToken, 'Dependent creation');
  const movable = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Gerçek tarih değişimi ${stamp.slice(-4)}`, 5), ownerToken, 'Movable task creation');
  await requireApi(`/api/work-items/${blocker.id}/relations`, 'POST', { relatedWorkItemId: dependent.id, relationType: 'Blocks' }, ownerToken, 'Dependency creation');
  await requireApi(`/api/sprints/${sprint.id}/items/${dependent.id}`, 'PUT', { estimatePoints: 5 }, ownerToken, 'Sprint planning');
  checks.push('real-fixture-dates-dependency');

  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await browserContextLogin(ownerContext, owner.username);
  const page = await ownerContext.newPage();
  diagnostics(page, 'owner');
  await page.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=calendar&calendar=month&anchor=${isoDay(5)}`, { waitUntil: 'domcontentloaded' });
  await page.getByText('Tüm proje kapsamı', { exact: true }).waitFor({ timeout: 45_000 });
  await page.getByText(`Gerçek pilot ${stamp.slice(-4)}`, { exact: true }).waitFor();
  await page.getByText(`Gerçek sürüm ${stamp.slice(-4)}`, { exact: true }).waitFor();
  checks.push('real-calendar-project-dates');
  await page.screenshot({ path: resolve(outputDir, 'owner-calendar.png'), fullPage: true });

  await page.getByRole('button', { name: 'Liste', exact: true }).click();
  const blockerBefore = await requireApi(`/api/work-items/${blocker.id}`, 'GET', undefined, ownerToken, 'Blocker before external update');
  const externalDue = isoInstant(7);
  await requireApi(`/api/work-items/${blocker.id}`, 'PUT', {
    title: blockerBefore.title, description: blockerBefore.description || '', priority: blockerBefore.priority, dueDate: externalDue
  }, ownerToken, 'External blocker update');
  await page.getByLabel(`${blocker.title} bitiş tarihi`).fill(isoDay(8));
  await page.locator('.planning-feedback-v3').filter({ hasText: 'başka bir kullanıcı tarafından değiştirildi' }).waitFor({ timeout: 45_000 });
  const blockerAfter = await requireApi(`/api/work-items/${blocker.id}`, 'GET', undefined, ownerToken, 'Blocker after conflict');
  assert.equal(blockerAfter.dueDate.slice(0, 10), externalDue.slice(0, 10));
  checks.push('real-reschedule-conflict-rollback');

  const movedDay = isoDay(10);
  await page.getByLabel(`${movable.title} bitiş tarihi`).fill(movedDay);
  await page.locator('.planning-feedback-v3').filter({ hasText: 'olarak güncellendi' }).waitFor();
  const moved = await requireApi(`/api/work-items/${movable.id}`, 'GET', undefined, ownerToken, 'Moved task read');
  assert.equal(moved.dueDate.slice(0, 10), movedDay);
  checks.push('real-reschedule-if-match');

  await page.getByRole('button', { name: 'Daha fazla', exact: true }).click();
  await page.getByRole('menuitem', { name: 'Zaman çizelgesi', exact: true }).click();
  await page.locator('.planning-risk-note').filter({ hasText: '1 bağımlılık' }).waitFor();
  await page.getByRole('button', { name: 'Tabloyu göster' }).click();
  assert.ok(await page.locator('.planning-table tbody tr').count() >= 3);
  checks.push('real-gantt-dependency-table');
  await page.screenshot({ path: resolve(outputDir, 'owner-timeline-table.png'), fullPage: true });

  await page.getByRole('button', { name: 'Daha fazla', exact: true }).click();
  await page.getByRole('menuitem', { name: 'Yol haritası', exact: true }).click();
  await page.getByText('Teslimat yol haritası', { exact: true }).waitFor();
  await page.getByText(`Gerçek plan sprinti ${stamp.slice(-4)}`, { exact: true }).waitFor();
  assert.ok(await page.locator('.roadmap-segment').count() >= 1);
  checks.push('real-roadmap-rollup');

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'viewer');
  await viewerPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=calendar&calendar=list&anchor=${isoDay(5)}`, { waitUntil: 'domcontentloaded' });
  await viewerPage.getByText('Tüm proje kapsamı', { exact: true }).waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.locator('.planning-surface-v3 input[type="date"]').count(), 0);
  assert.equal(await viewerPage.locator('.planning-surface-v3 [draggable="true"]').count(), 0);
  checks.push('real-viewer-read-only');

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}/plan?mode=calendar&anchor=${isoDay(5)}`, { waitUntil: 'domcontentloaded' });
  await mobilePage.getByText('Tüm proje kapsamı', { exact: false }).waitFor({ timeout: 45_000 });
  await mobilePage.getByText(`Gerçek pilot ${stamp.slice(-4)}`, { exact: true }).waitFor();
  await mobilePage.getByRole('button', { name: 'Zaman', exact: true }).click();
  await mobilePage.getByText('İş zaman çizelgesi', { exact: true }).waitFor();
  await mobilePage.getByRole('button', { name: 'Yol haritası', exact: true }).click();
  await mobilePage.getByText(`Gerçek sürüm ${stamp.slice(-4)}`, { exact: true }).waitFor();
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  checks.push('real-mobile-plan-parity');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-roadmap.png'), fullPage: true });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1, taskId: 'V3-UX-007', runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 8,
    apiBaseUrl, frontendBaseUrl, checks, cleanup: cleanupResult, failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.equal(checks.length, 8, `Expected 8 real checks, received ${checks.length}`);
console.log('V3-UX-007 real-browser passed: real dates, dependency Gantt/table, roadmap, conflict recovery, Viewer and mobile parity.');
