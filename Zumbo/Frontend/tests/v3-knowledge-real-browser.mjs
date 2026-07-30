import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-feature-007-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-FEATURE-007', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret(
  'ZUMBO_IDENTITY_ADMIN_EMAIL',
  'for V3-FEATURE-007 tenant cleanup'
);
const adminBootstrapToken = requireLocalSecret(
  'ZUMBO_IDENTITY_BOOTSTRAP_TOKEN',
  'for V3-FEATURE-007 tenant cleanup'
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

async function inviteAndRegister(teamId, email, username, ownerToken) {
  const invitation = await requireApi(`/api/teams/${teamId}/members`, 'POST', {
    email,
    role: 'Member'
  }, ownerToken, `${username} team invitation`);
  const registration = await requireApi('/api/auth/register', 'POST', {
    username,
    email,
    password,
    organizationId: tenantId
  }, undefined, `${username} registration`);
  await requireApi(`/api/teams/${teamId}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, registration.accessToken, `${username} invitation acceptance`);
  return registration;
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
    name: 'Zumbo Bilgi Merkezi Kanıtı',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Bilgi Teslimat Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const viewerRegistration = await inviteAndRegister(
    team.id,
    `f7viewer${stamp}@zumbo.local`,
    'Deniz Kaya',
    ownerToken
  );
  const outsiderRegistration = await inviteAndRegister(
    team.id,
    `f7outsider${stamp}@zumbo.local`,
    'Mert Aydın',
    ownerToken
  );
  const viewer = viewerRegistration.user;
  const viewerToken = viewerRegistration.accessToken;

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `KD${stamp.slice(-5)}`,
    name: 'Atlas Bilgi Teslimatı',
    ownerUserId: owner.id,
    visibility: 'Private'
  }, ownerToken, 'Project creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewer.id,
    role: 'Viewer'
  }, ownerToken, 'Project viewer grant');
  const board = await requireApi('/api/boards', 'POST', {
    projectId: project.id,
    name: 'Bilgi Kanıt Panosu',
    type: 'Kanban'
  }, ownerToken, 'Board creation');
  const workItem = await requireApi('/api/work-items', 'POST', {
    projectId: project.id,
    boardId: board.id,
    title: 'Yayın kontrol listesini tamamla',
    type: 'Task',
    priority: 'High',
    assigneeUserId: owner.id,
    dueDate: null,
    teamId: null
  }, ownerToken, 'Linked work item creation');

  let document = await requireApi('/api/knowledge-documents', 'POST', {
    scopeType: 'Project',
    scopeId: project.id,
    title: 'Üretim yayın runbooku',
    contentMarkdown: '# İlk sürüm\n\nYayın sorumlusu ve kontrol listesi tanımlandı.',
    tags: ['runbook'],
    workItemIds: [workItem.id],
    userIds: [viewer.id],
    changeSummary: 'İlk doğrulanmış sürüm.'
  }, ownerToken, 'Knowledge document creation');
  document = await requireApi(`/api/knowledge-documents/${document.id}`, 'PUT', {
    title: 'Üretim yayın runbooku',
    contentMarkdown: [
      '# Üretim yayın runbooku',
      '',
      '> Değişiklik penceresi onaylandıktan sonra ilerleyin.',
      '',
      '- [Yayın kontrol listesi](/work-items/' + workItem.id + ')',
      '- **Rollback sahibi:** ' + owner.username,
      '',
      '```sh',
      'pnpm test',
      '```'
    ].join('\n'),
    tags: ['runbook', 'güvenlik'],
    workItemIds: [workItem.id],
    userIds: [viewer.id],
    changeSummary: 'Rollback ve güvenlik adımları eklendi.'
  }, ownerToken, 'Knowledge version creation');
  assert.equal(document.currentContentVersion, 2);
  assert.deepEqual(document.versions.map(item => item.number), [2, 1]);
  const firstVersion = await requireApi(
    `/api/knowledge-documents/${document.id}/versions/1`,
    'GET',
    undefined,
    ownerToken,
    'Knowledge version history'
  );
  assert.match(firstVersion.contentMarkdown, /İlk sürüm/);
  const unsafe = await apiRequest('/api/knowledge-documents', 'POST', {
    scopeType: 'Project',
    scopeId: project.id,
    title: 'Unsafe content',
    contentMarkdown: '[unsafe](javascript:alert(1))',
    tags: [],
    workItemIds: [],
    userIds: [],
    changeSummary: 'Unsafe content.'
  }, ownerToken);
  assert.equal(unsafe.response.status, 400);
  assert.equal(unsafe.payload.error?.code, 'VALIDATION_ERROR');
  checks.push('real-api-immutable-version-history-and-safe-markdown');

  const viewerDocument = await requireApi(
    `/api/knowledge-documents/${document.id}`,
    'GET',
    undefined,
    viewerToken,
    'Viewer knowledge read'
  );
  assert.equal(viewerDocument.canEdit, false);
  assert.equal(viewerDocument.canComment, true);
  const forbidden = await apiRequest(`/api/knowledge-documents/${document.id}`, 'PUT', {
    title: 'Yetkisiz sürüm',
    contentMarkdown: 'Yetkisiz içerik',
    tags: [],
    workItemIds: [],
    userIds: [],
    changeSummary: 'Yetkisiz.'
  }, viewerToken);
  assert.equal(forbidden.response.status, 403);
  const hidden = await apiRequest(
    `/api/knowledge-documents/${document.id}`,
    'GET',
    undefined,
    outsiderRegistration.accessToken
  );
  assert.equal(hidden.response.status, 404);
  checks.push('real-api-viewer-authority-and-resource-isolation');

  let commented = await requireApi(
    `/api/knowledge-documents/${document.id}/comments`,
    'POST',
    { body: 'Rollback sahibi açıkça belirtilmeli.' },
    viewerToken,
    'Viewer comment'
  );
  const comment = commented.comments[0];
  commented = await requireApi(
    `/api/knowledge-documents/${document.id}/comments/${comment.id}/resolve`,
    'PATCH',
    {},
    viewerToken,
    'Viewer comment resolution'
  );
  assert.equal(commented.comments[0].resolved, true);
  const search = await requireApi(
    '/api/knowledge-documents?query=runbook&page=1&pageSize=20',
    'GET',
    undefined,
    viewerToken,
    'Knowledge search'
  );
  assert.equal(search.sourceStatus, 'Ready');
  assert.equal(search.items[0].id, document.id);
  const linkOptions = await requireApi(
    `/api/knowledge-documents/scope-link-options?scopeType=Project&scopeId=${project.id}`,
    'GET',
    undefined,
    ownerToken,
    'Knowledge named link options'
  );
  assert.match(linkOptions.workItems[0].label, /Yayın kontrol listesini tamamla/);
  assert.ok(linkOptions.users.some(item => item.id === viewer.id));
  checks.push('real-api-search-comments-and-named-links');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'owner-desktop');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=knowledge&project=${project.id}`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await ownerPage.locator('#knowledge-document-title').waitFor({ timeout: 45_000 });
  assert.match(await ownerPage.locator('.knowledge-render').innerText(), /Rollback sahibi/);
  await ownerPage.locator('.knowledge-link-list')
    .getByText(/Yayın kontrol listesini tamamla/)
    .waitFor({ timeout: 45_000 });
  assert.equal(await ownerPage.locator('.knowledge-render a[href^="javascript:"]').count(), 0);
  await ownerPage.getByRole('button', { name: /v1 · Üretim yayın runbooku/ }).click();
  await ownerPage.locator('.knowledge-version-banner').waitFor({ timeout: 45_000 });
  assert.match(await ownerPage.locator('.knowledge-render').innerText(), /İlk sürüm/);
  await ownerPage.getByRole('button', { name: 'Güncel sürüme dön' }).click();
  checks.push('real-desktop-owner-safe-render-history-and-links');
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
    `${frontendBaseUrl}/mobile-ionic/index.html#/knowledge`,
    { waitUntil: 'domcontentloaded', timeout: 60_000 }
  );
  await mobilePage.getByText('Üretim yayın runbooku', { exact: true }).first()
    .waitFor({ timeout: 45_000 });
  await mobilePage.locator('.mobile-knowledge-readonly').waitFor({ timeout: 45_000 });
  assert.equal(await mobilePage.getByRole('button', { name: 'Yeni sürüm' }).count(), 0);
  await mobilePage.getByRole('tab', { name: 'Yorumlar' }).click();
  await mobilePage.getByLabel('Yeni yorum').fill('Mobil gerçek API yorumu.');
  await mobilePage.getByRole('button', { name: 'Yorum ekle' }).click();
  await mobilePage.getByText('Mobil gerçek API yorumu.', { exact: true })
    .waitFor({ timeout: 45_000 });
  await mobilePage.getByRole('tab', { name: 'Bağlar' }).click();
  assert.match(await mobilePage.locator('.mobile-knowledge-links').innerText(), /Yayın kontrol listesini tamamla/);
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    minimumActionHeight: Math.min(...Array.from(
      document.querySelectorAll('.mobile-knowledge-tabs button')
    ).map(element => element.getBoundingClientRect().height))
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  assert.ok(dimensions.minimumActionHeight >= 44);
  checks.push('real-mobile-viewer-comment-authority-and-responsive-links');
  await capture(mobilePage, 'mobile-viewer.png');

  await mobileContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-007',
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
  'V3-FEATURE-007 real-browser passed: versions, safe content, search, comments and desktop/mobile authority.'
);
