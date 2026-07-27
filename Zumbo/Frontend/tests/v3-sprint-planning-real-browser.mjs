import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-ux-005-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-UX-005', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-UX-005 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-UX-005 tenant cleanup');
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

function taskPayload(project, board, owner, title) {
  return {
    projectId: project.id, boardId: board.id, title, type: 'Task', priority: 'High',
    assigneeUserId: owner.id, dueDate: new Date(Date.now() + 3 * 86400000).toISOString()
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux5owner${stamp}`, email: adminEmail, password,
    organizationId: tenantId, bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', { name: 'Zumbo Sprint Planning', tenantKey: tenantId }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `ux5viewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId, name: 'Planlama Ekibi', ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail, role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `ux5viewer${stamp}`, email: viewerEmail, password, organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId, key: `SP${stamp.slice(-5)}`, name: 'Sprint Operasyonları', ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id, name: 'Sprint Panosu', type: 'Scrum'
  }, ownerToken, 'Board creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id, role: 'Viewer'
  }, ownerToken, 'Viewer project grant');

  const taskOne = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Planlanacak gerçek iş ${stamp.slice(-4)}`), ownerToken, 'Task one creation');
  const conflictTask = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Çakışacak gerçek iş ${stamp.slice(-4)}`), ownerToken, 'Conflict task creation');
  const mobileTask = await requireApi('/api/work-items', 'POST', taskPayload(project, board, owner, `Mobil planlanacak iş ${stamp.slice(-4)}`), ownerToken, 'Mobile task creation');
  const today = new Date();
  const startDate = today.toISOString().slice(0, 10);
  const endDate = new Date(today.getTime() + 13 * 86400000).toISOString().slice(0, 10);
  const nextStart = new Date(today.getTime() + 14 * 86400000).toISOString().slice(0, 10);
  const nextEnd = new Date(today.getTime() + 27 * 86400000).toISOString().slice(0, 10);
  const sprintOne = await requireApi('/api/sprints', 'POST', {
    projectId: project.id, name: `Gerçek sprint ${stamp.slice(-4)}`, goal: 'Planla, başlat ve güvenle devret', startDate, endDate
  }, ownerToken, 'Sprint one creation');
  const sprintTwo = await requireApi('/api/sprints', 'POST', {
    projectId: project.id, name: `Devralan sprint ${stamp.slice(-4)}`, goal: 'Tamamlanmayan kapsamı devral', startDate: nextStart, endDate: nextEnd
  }, ownerToken, 'Sprint two creation');
  checks.push('real-create-goal-dates');

  const ownerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'owner');
  await ownerPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=backlog`, { waitUntil: 'domcontentloaded' });
  await ownerPage.locator('.planning-workspace').waitFor({ timeout: 45_000 });
  await ownerPage.getByLabel('Hedef sprint').selectOption({ label: `${sprintOne.name} · Planned` });
  await ownerPage.getByLabel(`${taskOne.title} işini sprint kapsamına al`).click();
  await ownerPage.getByText('İş sprint kapsamına alındı.', { exact: true }).waitFor();
  const planned = await requireApi(`/api/work-items/${taskOne.id}`, 'GET', undefined, ownerToken, 'Planned task read');
  assert.equal(planned.sprintId, sprintOne.id);
  checks.push('real-owner-plan-if-match');

  await requireApi(`/api/work-items/${conflictTask.id}`, 'PUT', {
    title: `${conflictTask.title} dış güncelleme`, description: conflictTask.description || '',
    priority: conflictTask.priority, dueDate: conflictTask.dueDate || null
  }, ownerToken, 'External conflict update');
  await ownerPage.getByLabel(`${conflictTask.title} işini sprint kapsamına al`).click();
  await ownerPage.getByRole('alert').filter({ hasText: 'Çakışma algılandı' }).waitFor();
  await ownerPage.getByText(`${conflictTask.title} dış güncelleme`, { exact: true }).waitFor();
  checks.push('real-concurrency-recovery');
  await ownerPage.screenshot({ path: resolve(outputDir, 'owner-backlog-conflict.png') });

  await ownerPage.getByRole('tab', { name: 'Sprint', exact: true }).click();
  await ownerPage.getByLabel('Sprint seç').selectOption({ label: `${sprintOne.name} · Planned` });
  await ownerPage.getByRole('button', { name: "Sprint'i başlat" }).click();
  await ownerPage.locator('.burndown-bars').waitFor({ timeout: 45_000 });
  const burndown = await requireApi(`/api/sprints/${sprintOne.id}/burndown`, 'GET', undefined, ownerToken, 'Burndown read');
  assert.ok(burndown.length >= 1);
  checks.push('real-start-burndown');
  await ownerPage.getByLabel('Devreden iş hedefi').selectOption(sprintTwo.id);
  await ownerPage.getByRole('button', { name: "Sprint'i tamamla" }).click();
  await ownerPage.getByText('Completed', { exact: true }).first().waitFor();
  const completedSprint = await requireApi(`/api/sprints/${sprintOne.id}`, 'GET', undefined, ownerToken, 'Completed sprint read');
  const carriedTask = await requireApi(`/api/work-items/${taskOne.id}`, 'GET', undefined, ownerToken, 'Carried task read');
  assert.equal(completedSprint.status, 'Completed');
  assert.equal(completedSprint.carryoverItems, 1);
  assert.equal(carriedTask.sprintId, sprintTwo.id);
  checks.push('real-complete-carryover');
  await ownerPage.screenshot({ path: resolve(outputDir, 'owner-sprint-completed.png') });

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'viewer');
  await viewerPage.goto(`${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&board=${board.id}&view=backlog`, { waitUntil: 'domcontentloaded' });
  await viewerPage.locator('.planning-workspace').waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.locator('.planning-move').count(), 0);
  await viewerPage.getByRole('tab', { name: 'Sprint', exact: true }).click();
  assert.equal(await viewerPage.locator('.sprint-create').count(), 0);
  checks.push('real-viewer-read-only');

  const mobileContext = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
  const mobileAuthentication = await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  attachDiagnostics(mobilePage, 'mobile');
  await mobilePage.goto(`${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}`, { waitUntil: 'domcontentloaded' });
  await mobilePage.waitForFunction(initialToken => {
    const currentToken = sessionStorage.getItem('zumbo.csrfToken');
    return currentToken && currentToken !== initialToken && document.body.getAttribute('aria-busy') === 'false';
  }, mobileAuthentication.csrfToken, { timeout: 45_000 });
  await mobilePage.getByRole('button', { name: 'Backlog', exact: true }).click();
  await mobilePage.locator('.mobile-planning-row').first().waitFor({ timeout: 45_000 });
  await mobilePage.getByLabel('Backlog hedef sprinti').selectOption({ label: `${sprintTwo.name} · Planned` });
  await mobilePage.getByRole('button', { name: `${mobileTask.title} işini sprint kapsamına al` }).click();
  await mobilePage.getByText('İş sprint kapsamına alındı.', { exact: true }).waitFor();
  const mobilePlanned = await requireApi(`/api/work-items/${mobileTask.id}`, 'GET', undefined, ownerToken, 'Mobile planned task read');
  assert.equal(mobilePlanned.sprintId, sprintTwo.id);
  checks.push('real-mobile-touch-plan');
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  checks.push('real-mobile-no-overflow');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-backlog-planned.png') });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1, taskId: 'V3-UX-005', runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 8,
    apiBaseUrl, frontendBaseUrl, checks, cleanup: cleanupResult, failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.equal(checks.length, 8, `Expected 8 real checks, received ${checks.length}`);
console.log('V3-UX-005 real-browser passed: create, conflict recovery, start, burndown, carryover, Viewer and mobile planning.');
