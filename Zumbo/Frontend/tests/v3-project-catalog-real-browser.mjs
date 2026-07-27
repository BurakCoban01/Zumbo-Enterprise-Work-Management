import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-surface-001-real');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const runContext = createRunContext('V3-SURFACE-001', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-SURFACE-001 tenant cleanup');
const bootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-SURFACE-001 tenant cleanup');
const password = 'P@ssword123';
const failures = [];
const checks = [];
let cleanupAdminTokenPromise;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
let browser;

await mkdir(outputDir, { recursive: true });

async function apiRequest(path, method = 'GET', body, token) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
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

try {
  browser = await chromium.launch({ headless: true });
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `surfaceowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);

  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Teslimat Kataloğu',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const viewerEmail = `surfaceviewer${stamp}@zumbo.local`;
  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Teslimat Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const invite = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `surfaceviewer${stamp}`,
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invite.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key: `SF${stamp.slice(-5)}`,
    name: 'Teslimat Merkezi',
    ownerUserId: owner.id
  }, ownerToken, 'Project creation');
  await requireApi(`/api/projects/${project.id}/members`, 'POST', {
    userId: viewerRegistration.user.id,
    role: 'Viewer'
  }, ownerToken, 'Viewer project grant');
  await requireApi(`/api/projects/${project.id}/milestones`, 'POST', {
    name: 'Pilot doğrulama',
    dueAt: new Date(Date.now() + 10 * 86400000).toISOString()
  }, ownerToken, 'Milestone fixture');
  checks.push('real-tenant-and-role-fixture');

  const ownerContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await browserContextLogin(ownerContext, owner.username);
  const ownerPage = await ownerContext.newPage();
  attachDiagnostics(ownerPage, 'desktop-owner');
  await ownerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&view=catalog`,
    { waitUntil: 'domcontentloaded' }
  );
  await ownerPage.getByRole('heading', { name: 'Sürümler, yayınlar ve proje kataloğu' }).waitFor({ timeout: 45_000 });
  assert.equal(await ownerPage.getByRole('tab', { name: 'Teslimat', exact: true }).getAttribute('aria-selected'), 'true');
  checks.push('desktop-normal-navigation');

  await ownerPage.getByLabel('Sürüm adı').fill('4.0');
  await ownerPage.getByRole('button', { name: 'Sürüm oluştur' }).click();
  await ownerPage.getByLabel('Sürümler').getByText('4.0', { exact: true }).waitFor();
  await ownerPage.getByLabel('Planlanan sürüm').selectOption({ label: '4.0' });
  await ownerPage.getByLabel('Yayın adı').fill('Sürüm 4.0');
  await ownerPage.getByRole('button', { name: 'Taslak oluştur' }).click();
  await ownerPage.getByRole('button', { name: 'Onayla' }).click();
  await ownerPage.getByRole('button', { name: 'Yayınla' }).click();
  await ownerPage.getByText('Published', { exact: true }).waitFor();
  checks.push('real-version-release-lifecycle');
  await ownerPage.screenshot({ path: resolve(outputDir, 'desktop-owner-releases.png'), fullPage: true });

  await ownerPage.getByRole('tab', { name: 'Kilometre taşları' }).click();
  await ownerPage.getByRole('button', { name: 'Pilot doğrulama kilometre taşını düzenle' }).click();
  await ownerPage.getByLabel('Ad', { exact: true }).fill('Pilot tamamlandı');
  await ownerPage.getByRole('button', { name: 'Değişiklikleri kaydet' }).click();
  await ownerPage.getByRole('button', { name: 'Tamamla' }).click();
  await ownerPage.getByText('Completed', { exact: true }).waitFor();
  checks.push('real-milestone-lifecycle');

  await ownerPage.getByRole('tab', { name: 'Bileşenler' }).click();
  await ownerPage.getByLabel('Ad', { exact: true }).fill('Web istemcisi');
  await ownerPage.getByLabel('Açıklama').fill('Masaüstü teslimat yüzeyi');
  await ownerPage.getByRole('button', { name: 'Bileşen ekle' }).click();
  await ownerPage.getByText('Web istemcisi', { exact: true }).waitFor();
  await ownerPage.getByRole('button', { name: 'Web istemcisi bileşenini düzenle' }).click();
  await ownerPage.getByLabel('Ad', { exact: true }).fill('Web uygulaması');
  await ownerPage.getByRole('button', { name: 'Değişiklikleri kaydet' }).click();
  await ownerPage.getByRole('button', { name: 'Web uygulaması bileşenini arşivle' }).click();
  await ownerPage.getByRole('button', { name: 'Evet' }).click();
  await ownerPage.getByLabel('Sorumluluk alanları').getByText('Arşiv', { exact: true }).waitFor();
  checks.push('real-component-crud');

  await ownerPage.getByRole('tab', { name: 'Şablonlar' }).click();
  await ownerPage.getByLabel('Ad', { exact: true }).fill('Standart teslimat');
  await ownerPage.getByLabel('Varsayılan bileşen adları').fill('API\nWeb uygulaması\nMobil');
  await ownerPage.getByLabel('Bu projede varsayılan şablon').check();
  await ownerPage.getByRole('button', { name: 'Şablon ekle' }).click();
  await ownerPage.getByText('Standart teslimat', { exact: true }).waitFor();
  checks.push('real-template-create-and-limit-contract');

  await ownerPage.getByRole('tab', { name: 'Bileşenler' }).click();
  await ownerPage.getByLabel('Ad', { exact: true }).fill('Çakışan taslak');
  await requireApi(`/api/projects/${project.id}/components`, 'POST', {
    name: 'Dış değişiklik',
    description: 'Eşzamanlı API işlemi'
  }, ownerToken, 'Concurrent component creation');
  await ownerPage.getByRole('button', { name: 'Bileşen ekle' }).click();
  await ownerPage.getByText(/güncel veriler yüklendi/i).waitFor();
  await ownerPage.getByText('Dış değişiklik', { exact: true }).waitFor();
  assert.equal(await ownerPage.getByLabel('Ad', { exact: true }).inputValue(), '');
  checks.push('real-stale-conflict-reload');

  await ownerPage.getByRole('tab', { name: 'Etkinlik' }).click();
  await ownerPage.getByText('ProjectReleasePublished', { exact: true }).waitFor();
  await ownerPage.getByText('ProjectTemplateCreated', { exact: true }).waitFor();
  checks.push('real-catalog-audit');

  const viewerContext = await browser.newContext({
    viewport: { width: 1280, height: 900 },
    reducedMotion: 'reduce'
  });
  await browserContextLogin(viewerContext, viewerRegistration.user.username);
  const viewerPage = await viewerContext.newPage();
  attachDiagnostics(viewerPage, 'desktop-viewer');
  await viewerPage.goto(
    `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${project.id}&view=catalog`,
    { waitUntil: 'domcontentloaded' }
  );
  await viewerPage.getByText(/salt okunur gösteriliyor/i).waitFor({ timeout: 45_000 });
  assert.equal(await viewerPage.getByRole('button', { name: 'Sürüm oluştur' }).count(), 0);
  assert.equal(await viewerPage.getByRole('button', { name: 'Yayınla' }).count(), 0);
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
    `${frontendBaseUrl}/mobile-ionic/index.html#/projects/${project.id}/catalog?tab=components`,
    { waitUntil: 'domcontentloaded' }
  );
  await mobilePage.getByRole('heading', { name: 'Teslimat kataloğu' }).waitFor({ timeout: 45_000 });
  await mobilePage.getByRole('tab', { name: 'Bileşen' }).click();
  await mobilePage.getByLabel('Ad', { exact: true }).fill('Mobil istemci');
  await mobilePage.getByLabel('Açıklama').fill('Mobil teslimat yüzeyi');
  await mobilePage.getByRole('button', { name: 'Ekle', exact: true }).click();
  await mobilePage.getByText('Mobil istemci', { exact: true }).waitFor();
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile catalog overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  const tabsFit = await mobilePage.getByRole('tab').evaluateAll((tabs, width) => tabs.every(tab => {
    const bounds = tab.getBoundingClientRect();
    return bounds.left >= -1 && bounds.right <= width + 1;
  }), dimensions.width);
  assert.equal(tabsFit, true, 'Mobile catalog tabs must remain fully visible without horizontal scrolling');
  checks.push('real-mobile-mutation-no-overflow');
  await mobilePage.screenshot({ path: resolve(outputDir, 'mobile-components.png'), fullPage: true });

  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  await writeFile(resolve(outputDir, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-001',
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
console.log('V3-SURFACE-001 real-browser passed: real lifecycle, conflict, audit, Viewer and mobile parity.');
