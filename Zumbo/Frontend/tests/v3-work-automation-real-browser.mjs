import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-surface-002-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-SURFACE-002', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-SURFACE-002 tenant cleanup');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-SURFACE-002 tenant cleanup');
const password = 'P@ssword123';
const failures = [];
const checks = [];
let cleanupAdminTokenPromise;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;

await mkdir(outputDir, { recursive: true });

async function apiRequest(path, method = 'GET', body, token, expectedVersion) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(expectedVersion ? { 'If-Match': `"${expectedVersion}"` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label, expectedVersion) {
  const result = await apiRequest(path, method, body, token, expectedVersion);
  assert.ok(result.response.ok, result.payload.error?.message || `${label} failed with HTTP ${result.response.status}`);
  return result.data;
}

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      const authentication = await apiRequest('/api/auth/login', 'POST', {
        usernameOrEmail: adminEmail,
        password
      });
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

function attachDiagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (/\/hubs\/work-items|Failed to start the connection|WebSocket connection/.test(detail)) return;
    if (!detail.includes('Failed to load resource')) failures.push(`${label}: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      failures.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
}

async function eventually(operation, label, attempts = 100, delayMs = 100) {
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const result = await operation();
    if (result) return result;
    await new Promise(resolveDelay => setTimeout(resolveDelay, delayMs));
  }
  throw new Error(`${label} did not become true after ${attempts} attempts`);
}

function localDateTime(value) {
  const date = new Date(value);
  const parts = [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0')
  ];
  return `${parts.join('-')}T${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `automationowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);

  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo İş Otomasyonu',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `automationviewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Otomasyon Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invite = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `automationviewer${stamp}`,
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invite.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `AU${stamp.slice(-5)}`,
    name: 'Operasyon Otomasyonu',
    ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Viewer project grant');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Operasyon Panosu',
    type: 'Kanban'
  }, ownerToken, 'Board creation');
  checks.push('real-tenant-project-role-board-fixture');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'desktop-owner');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&view=automation`,
    { waitUntil: 'domcontentloaded' }
  );
  await ownerPage.getByRole('heading', { name: 'Yinelenen işler ve iş şablonları' }).waitFor({ timeout: 45_000 });
  assert.equal(await ownerPage.getByRole('tab', { name: 'Otomasyon', exact: true }).getAttribute('aria-selected'), 'true');
  checks.push('real-desktop-normal-navigation-timezone');

  await ownerPage.getByRole('tab', { name: 'İş şablonları' }).click();
  await ownerPage.getByLabel('Şablon adı').fill('Günlük operasyon');
  await ownerPage.getByLabel('Üretilecek iş başlığı').fill('Günlük operasyon kuyruğunu incele');
  await ownerPage.getByLabel(/Etiketler/).fill(Array.from({ length: 51 }, (_, index) => `etiket-${index + 1}`).join(','));
  assert.equal(await ownerPage.getByRole('button', { name: 'Şablon oluştur' }).isDisabled(), true);
  await ownerPage.getByLabel(/Etiketler/).fill('operasyon, günlük');
  await ownerPage.getByRole('button', { name: 'Şablon oluştur' }).click();
  await ownerPage.getByText('Günlük operasyon', { exact: true }).waitFor();
  const templatePage = await requireApi(
    `/api/work-items/templates?projectId=${project.id}&page=1&pageSize=20`,
    'GET',
    undefined,
    ownerToken,
    'Template list');
  const createdTemplate = templatePage.items.find(item => item.name === 'Günlük operasyon');
  assert.ok(createdTemplate);
  checks.push('real-template-create-explicit-limit');

  await ownerPage.getByRole('button', { name: 'Günlük operasyon şablonuyla yineleme oluştur' }).click();
  await ownerPage.getByLabel('Sıklık').selectOption('Daily');
  await ownerPage.getByLabel('İlk çalıştırma · Europe/Istanbul').fill(localDateTime(Date.now() - 60_000));
  await ownerPage.getByLabel('En fazla çalıştırma').fill('1');
  await ownerPage.getByRole('button', { name: 'Takvimi önizle' }).click();
  await ownerPage.getByText('Sunucu takvim önizlemesi', { exact: true }).waitFor();
  const invalidPreview = await apiRequest('/api/work-items/recurrences/preview', 'POST', {
    projectId: project.id,
    templateId: createdTemplate.id,
    frequency: 'Daily',
    interval: 0,
    startAtUtc: new Date().toISOString(),
    endAtUtc: null,
    maxOccurrences: 1,
    previewCount: 5
  }, ownerToken);
  assert.equal(invalidPreview.response.status, 400);
  assert.equal(invalidPreview.payload.error.code, 'VALIDATION_ERROR');
  checks.push('real-authoritative-preview-negative');

  await ownerPage.getByRole('button', { name: 'Etkinleştir' }).click();
  const generatedRecurrence = await eventually(async () => {
    const page = await requireApi(
      `/api/work-items/recurrences?projectId=${project.id}&page=1&pageSize=20`,
      'GET',
      undefined,
      ownerToken,
      'Recurrence list');
    return page.items.find(item => item.templateId === createdTemplate.id && item.maxOccurrences === 1);
  }, 'Created recurrence');
  const generatedOccurrence = await eventually(async () => {
    const page = await requireApi(
      `/api/work-items/recurrences/${generatedRecurrence.id}/occurrences?page=1&pageSize=20`,
      'GET',
      undefined,
      ownerToken,
      'Occurrence list');
    return page.items.find(item => item.status === 'Generated' && item.createdWorkItemId);
  }, 'Generated recurrence occurrence', 200, 100);
  assert.ok(generatedOccurrence.createdWorkItemId);
  const generatedWorkItem = await requireApi(
    `/api/work-items/${generatedOccurrence.createdWorkItemId}`,
    'GET',
    undefined,
    ownerToken,
    'Generated work item');
  assert.equal(generatedWorkItem.title, 'Günlük operasyon kuyruğunu incele');
  await ownerPage.reload({ waitUntil: 'domcontentloaded' });
  const generatedRow = ownerPage.locator('.automation-row').filter({ hasText: 'Günlük operasyon' }).first();
  await generatedRow.locator('.automation-row-main').click();
  await generatedRow.getByText('Tamamlandı', { exact: true }).waitFor();
  await ownerPage.getByText('Oluşturuldu', { exact: true }).waitFor();
  checks.push('real-scheduler-generated-work-item');

  const lifecycleRecurrence = await requireApi('/api/work-items/recurrences', 'POST', {
    projectId: project.id,
    templateId: createdTemplate.id,
    frequency: 'Weekly',
    interval: 1,
    startAtUtc: new Date(Date.now() + 86400000).toISOString(),
    endAtUtc: new Date(Date.now() + 30 * 86400000).toISOString(),
    maxOccurrences: 2
  }, ownerToken, 'Lifecycle recurrence creation');
  await ownerPage.reload({ waitUntil: 'domcontentloaded' });
  const lifecycleRow = ownerPage.locator(`[data-recurrence-id="${lifecycleRecurrence.id}"]`);
  await lifecycleRow.waitFor();
  await ownerPage.waitForLoadState('networkidle');
  await ownerPage.waitForFunction(({ id, version }) => {
    const vm = window.angular.element(document.body).scope().vm;
    const recurrence = vm.workAutomation.recurrences.find(item => item.id === id);
    return !vm.workAutomationLoading && recurrence && recurrence.version === version;
  }, { id: lifecycleRecurrence.id, version: lifecycleRecurrence.version });
  await requireApi(
    `/api/work-items/recurrences/${lifecycleRecurrence.id}/state`,
    'PATCH',
    { active: false },
    ownerToken,
    'Concurrent recurrence pause',
    lifecycleRecurrence.version);
  assert.equal(await ownerPage.evaluate(id => {
    const vm = window.angular.element(document.body).scope().vm;
    return vm.workAutomation.recurrences.find(item => item.id === id).version;
  }, lifecycleRecurrence.id), lifecycleRecurrence.version);
  await lifecycleRow.getByRole('button', { name: 'Yinelemeyi arşivle' }).click();
  await lifecycleRow.getByRole('button', { name: 'Evet' }).click();
  await ownerPage.getByText(/başka bir kullanıcı tarafından değiştirildi/i).first().waitFor();
  await lifecycleRow.getByText('Duraklatıldı', { exact: true }).waitFor();
  checks.push('real-stale-conflict-authoritative-reload');

  await lifecycleRow.getByRole('button', { name: 'Yinelemeyi devam ettir' }).click();
  await lifecycleRow.getByText('Etkin', { exact: true }).waitFor();
  const resumedPage = await requireApi(
    `/api/work-items/recurrences?projectId=${project.id}&page=1&pageSize=20`,
    'GET',
    undefined,
    ownerToken,
    'Resumed recurrence list');
  assert.equal(resumedPage.items.find(item => item.id === lifecycleRecurrence.id).active, true);
  assert.equal(resumedPage.items.find(item => item.id === lifecycleRecurrence.id).archived, false);
  checks.push('real-pause-resume-current-version');

  const recurrenceAudit = await eventually(async () => {
    const entries = await requireApi(
      `/api/audit/entity/WorkItemRecurrence/${lifecycleRecurrence.id}`,
      'GET',
      undefined,
      ownerToken,
      'Recurrence audit');
    return entries.some(entry => entry.action === 'WorkItemRecurrencePaused')
      && entries.some(entry => entry.action === 'WorkItemRecurrenceResumed')
      ? entries
      : null;
  }, 'Recurrence audit actions');
  assert.ok(recurrenceAudit.some(entry => entry.action === 'WorkItemRecurrencePaused'));
  assert.ok(recurrenceAudit.some(entry => entry.action === 'WorkItemRecurrenceResumed'));
  const auditResponse = ownerPage.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname === `/api/audit/entity/WorkItemRecurrence/${lifecycleRecurrence.id}`
      && response.request().method() === 'GET';
  });
  await lifecycleRow.locator('.automation-row-main').click();
  assert.ok((await auditResponse).ok());
  await ownerPage.getByRole('tab', { name: 'Çalıştırma geçmişi' }).click();
  await ownerPage.getByText('WorkItemRecurrenceResumed', { exact: true }).waitFor();
  await ownerPage.getByText('WorkItemRecurrencePaused', { exact: true }).waitFor();
  checks.push('real-recurrence-audit');
  await ownerPage.screenshot({ path: resolve(outputDir, 'desktop-owner.png'), fullPage: true });

  const viewerContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'desktop-viewer');
  await viewerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&view=automation`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.getByText(/salt okunur gösteriliyor/i).waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.getByRole('button', { name: 'Etkinleştir' }).count(), 0);
  assert.equal(await viewerPage.getByRole('button', { name: /duraklat/i }).count(), 0);
  checks.push('real-viewer-read-only');
  await viewerPage.screenshot({ path: resolve(outputDir, 'desktop-viewer.png'), fullPage: true });

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  attachDiagnostics(mobilePage, 'mobile-owner');
  await mobilePage.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}/automation?tab=schedules`,
    { waitUntil: 'domcontentloaded' }
  );
  await mobilePage.getByRole('heading', { name: 'İş otomasyonu' }).waitFor({ timeout: 45_000 });
  await mobilePage.locator('.mobile-automation-row').filter({ hasText: 'Günlük operasyon' }).first().waitFor();
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile automation overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  const tabsFit = await mobilePage.locator('.mobile-automation-tabs [role="tab"]').evaluateAll((tabs, width) => tabs.every(tab => {
    const bounds = tab.getBoundingClientRect();
    return bounds.left >= -1 && bounds.right <= width + 1;
  }), dimensions.width);
  assert.equal(tabsFit, true);
  checks.push('real-mobile-parity-no-overflow');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-owner.png'), fullPage: true });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-002',
    runId: runContext.runId,
    passed: failures.length === 0 && cleanupResult.failed === 0 && checks.length === 10,
    apiBaseUrl,
    frontendBaseUrl,
    checks,
    cleanup: cleanupResult,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
assert.equal(checks.length, 10, `Expected 10 checks, received ${checks.length}`);
console.log('V3-SURFACE-002 real-browser passed: real preview, scheduler, conflict, audit, Viewer and mobile parity.');
