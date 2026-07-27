import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-feature-001-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-FEATURE-001', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-FEATURE-001 tenant cleanup');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-FEATURE-001 tenant cleanup');
const password = 'P@ssword123';
const failures = [];
const checks = [];
let cleanupAdminTokenPromise;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;

await mkdir(outputDir, { recursive: true });

async function apiRequest(path, method = 'GET', body, token, extraHeaders = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...extraHeaders
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label, extraHeaders) {
  const result = await apiRequest(path, method, body, token, extraHeaders);
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
      assert.ok(authentication.response.ok, authentication.payload.error?.message || 'Cleanup authentication failed');
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

function intakeDefinition(boardId, accessPolicy) {
  return {
    accessPolicy,
    boardId,
    workItemType: 'Task',
    defaultPriority: 'Medium',
    confirmationMessage: 'Talebiniz ilgili ekibe iletildi.',
    fields: [
      { key: 'baslik', label: 'Talep basligi', type: 'Text', required: true, options: [] },
      { key: 'aciklama', label: 'Aciklama', type: 'LongText', required: false, options: [] }
    ],
    mapping: {
      titleFieldKey: 'baslik',
      descriptionFieldKey: 'aciklama',
      priorityFieldKey: null,
      dueDateFieldKey: null,
      customFields: []
    }
  };
}

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `intakeowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);

  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Talep Operasyonlari',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `intakeviewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Talep Yonetimi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invite = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `intakeviewer${stamp}`,
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invite.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `IN${stamp.slice(-5)}`,
    name: 'Talep Operasyon Merkezi',
    ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Viewer project grant');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Talep Panosu',
    type: 'Kanban'
  }, ownerToken, 'Board creation');
  checks.push('real-tenant-project-board-and-viewer-fixture');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'desktop-owner');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&view=intake`,
    { waitUntil: 'domcontentloaded' }
  );
  await ownerPage.getByRole('heading', { name: 'Intake ve triage merkezi' }).waitFor({ timeout: 45_000 });
  await ownerPage.getByRole('button', { name: 'Yeni form' }).click();
  await ownerPage.locator('input[ng-model="vm.intakeDraft.name"]').fill('BT erisim talepleri');
  await ownerPage.locator('textarea[ng-model="vm.intakeDraft.description"]').fill('Ekip ici erisim talepleri');
  await ownerPage.locator('select[ng-model="vm.intakeDraft.definition.boardId"]').selectOption({ label: 'Talep Panosu' });
  await ownerPage.getByRole('button', { name: 'Alan ekle' }).click();
  const newField = ownerPage.locator('.intake-field-row').last();
  await newField.locator('input[ng-model="field.label"]').fill('Ekran goruntusu');
  await newField.locator('input[ng-model="field.key"]').fill('ekran_goruntusu');
  await newField.locator('select[ng-model="field.type"]').selectOption({ label: 'Dosya' });
  const draftValidation = await ownerPage.locator('.intake-editor-form').evaluate(form => {
    const scope = window.angular.element(form).scope();
    return scope.vm.intakeDraftError();
  });
  assert.equal(draftValidation, null, `Intake draft remained invalid: ${draftValidation}`);
  await ownerPage.getByRole('button', { name: 'Form olu\u015ftur' }).click();
  await ownerPage.getByText('Form tasla\u011f\u0131 olu\u015fturuldu.', { exact: true }).waitFor();
  await ownerPage.getByRole('button', { name: 'Yay\u0131nla', exact: true }).click();
  await ownerPage.getByText('Formun yeni s\u00fcr\u00fcm\u00fc yay\u0131nland\u0131.', { exact: true }).waitFor();
  checks.push('desktop-real-definition-and-publish');

  const viewerContext = await browser.newContext({
    viewport: { width: 1280, height: 900 },
    reducedMotion: 'reduce'
  });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'desktop-viewer');
  await viewerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&view=intake`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.getByText(/Viewer rol\u00fcyle formlar salt okunur/i).waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.getByRole('button', { name: 'Yeni form' }).count(), 0);
  assert.equal(await viewerPage.getByRole('button', { name: 'Yay\u0131nla', exact: true }).count(), 0);
  checks.push('desktop-real-viewer-read-only');
  await viewerPage.screenshot({ path: resolve(outputDir, 'desktop-viewer.png'), fullPage: true });

  await ownerPage.getByRole('tab', { name: 'Talep olu\u015ftur' }).click();
  await ownerPage.getByLabel('Talep ba\u015fl\u0131\u011f\u0131 *').fill('VPN erisim talebi');
  await ownerPage.getByLabel('A\u00e7\u0131klama').fill('Saha ekibi icin sureli erisim');
  await ownerPage.locator('input[type="file"]').setInputFiles({
    name: 'ekran-goruntusu.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('synthetic intake attachment')
  });
  await ownerPage.getByRole('button', { name: 'Talebi g\u00f6nder' }).click();
  await ownerPage.getByText('Talep i\u015f kayd\u0131na d\u00f6n\u00fc\u015ft\u00fcr\u00fcld\u00fc').waitFor();
  assert.equal(await ownerPage.getByRole('button', { name: '\u0130\u015fi a\u00e7' }).count(), 1);
  checks.push('desktop-real-attachment-submission-and-work-item');

  const forms = await requireApi(
    `/api/intake/forms?projectId=${encodeURIComponent(project.id)}`,
    'GET',
    undefined,
    ownerToken,
    'Form list');
  const internalForm = forms.find(form => form.name === 'BT erisim talepleri');
  assert.ok(internalForm, 'The browser-created internal form must be persisted');
  const idempotencyKey = `intake-real-${stamp}`;
  const idempotentBody = {
    values: [
      { fieldKey: 'baslik', value: 'Idempotent lisans talebi' },
      { fieldKey: 'aciklama', value: 'Ayni anahtarla tekrar gonderim' }
    ]
  };
  const firstReplay = await requireApi(
    `/api/intake/forms/${internalForm.id}/submissions`,
    'POST',
    idempotentBody,
    ownerToken,
    'First idempotent submission',
    { 'Idempotency-Key': idempotencyKey });
  const secondReplay = await requireApi(
    `/api/intake/forms/${internalForm.id}/submissions`,
    'POST',
    idempotentBody,
    ownerToken,
    'Repeated idempotent submission',
    { 'Idempotency-Key': idempotencyKey });
  assert.equal(secondReplay.submissionId, firstReplay.submissionId);
  assert.equal(secondReplay.workItemId, firstReplay.workItemId);
  checks.push('real-idempotent-durable-work-creation');

  await ownerPage.getByRole('tab', { name: 'Triage' }).click();
  await ownerPage.getByText('VPN erisim talebi', { exact: true }).waitFor();
  const queueRow = ownerPage.locator('.intake-queue-row').filter({ hasText: 'VPN erisim talebi' });
  await queueRow.getByPlaceholder('\u0130nceleme notu').fill('Yetki kapsami dogrulandi.');
  await queueRow.getByRole('button', { name: '\u0130ncelemede' }).click();
  await queueRow.getByText('\u0130ncelemede', { exact: true }).waitFor();
  await queueRow.getByText(/ekran-goruntusu\.txt/).waitFor();
  checks.push('desktop-real-triage-and-attachment-security-state');
  await ownerPage.screenshot({ path: resolve(outputDir, 'desktop-triage.png'), fullPage: true });

  const publicForm = await requireApi('/api/intake/forms', 'POST', {
    projectId: project.id,
    name: 'Musteri geri bildirimi',
    description: 'Dis paylasim icin guvenli talep formu',
    definition: intakeDefinition(board.id, 'Public')
  }, ownerToken, 'Public form creation');
  const publishedPublicForm = await requireApi(
    `/api/intake/forms/${publicForm.id}/publish`,
    'POST',
    {},
    ownerToken,
    'Public form publish');
  assert.ok(publishedPublicForm.publicId, 'Published public form must expose an opaque public identifier');

  const abuseProbe = await apiRequest(
    `/api/intake/public/forms/${encodeURIComponent(publishedPublicForm.publicId)}/submissions`,
    'POST',
    { values: [{ fieldKey: 'baslik', value: 'Bot talebi' }], website: 'https://invalid.example' },
    undefined,
    { 'Idempotency-Key': `abuse-${stamp}` });
  assert.equal(abuseProbe.response.status, 400);
  checks.push('real-public-honeypot-rejection');

  const publicContext = await browser.newContext({
    viewport: { width: 1024, height: 900 },
    reducedMotion: 'reduce'
  });
  const publicPage = await publicContext.newPage();
  attachDiagnostics(publicPage, 'desktop-public');
  await publicPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#public=${encodeURIComponent(publishedPublicForm.publicId)}`,
    { waitUntil: 'domcontentloaded' }
  );
  await publicPage.getByRole('heading', { name: 'Musteri geri bildirimi' }).waitFor({ timeout: 45_000 });
  await publicPage.getByLabel('Talep basligi *').fill('Portal geri bildirimi');
  await publicPage.getByLabel('Aciklama').fill('Form akisinda sentetik geri bildirim');
  await publicPage.getByRole('button', { name: 'Talebi g\u00f6nder' }).click();
  await publicPage.getByText('Talebiniz al\u0131nd\u0131', { exact: true }).waitFor();
  assert.equal(await publicPage.getByRole('button', { name: /\u0130\u015fi a\u00e7/ }).count(), 0);
  const publicSubmissions = await requireApi(
    `/api/intake/forms/${publicForm.id}/submissions?page=1&pageSize=20`,
    'GET',
    undefined,
    ownerToken,
    'Public submission queue');
  const publicSubmission = publicSubmissions.items.find(item =>
    item.values.some(value => value.value === 'Portal geri bildirimi'));
  assert.ok(publicSubmission?.workItemId, 'Authorized triage must retain the generated work item reference');
  checks.push('real-authorized-public-triage-retains-work-item');
  checks.push('real-anonymous-confirmation-hides-work-item-id');
  await publicPage.screenshot({ path: resolve(outputDir, 'desktop-public-confirmation.png'), fullPage: true });

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(mobileContext, owner.username);
  const mobilePage = await mobileContext.newPage();
  attachDiagnostics(mobilePage, 'mobile-owner');
  await mobilePage.goto(
    `${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}/intake`,
    { waitUntil: 'domcontentloaded' }
  );
  await mobilePage.getByRole('heading', { name: 'Intake ve triage' }).waitFor({ timeout: 45_000 });
  await mobilePage.getByText('BT erisim talepleri', { exact: true }).waitFor();
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile intake overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  checks.push('real-mobile-intake-parity-no-overflow');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-forms.png'), fullPage: true });

  await mobileContext.close();
  await publicContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-001',
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
console.log('V3-FEATURE-001 real-browser passed: real publish, attachment, idempotency, triage, Viewer, public and mobile flows.');
