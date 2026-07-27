import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-surface-003');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const user = { id: 'user-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const now = Date.now();
let preferences = { inAppEnabled: true, emailEnabled: false, mutedTypes: ['Assignment'] };
let sessions = [
  { id: 'session-current', deviceName: 'Bu tarayıcı', clientFingerprint: 'a'.repeat(64), createdAt: new Date(now - 7200000).toISOString(), lastSeenAt: new Date(now - 60000).toISOString(), expiresAt: new Date(now + 86400000).toISOString(), revokedAt: null, isCurrent: true },
  { id: 'session-office', deviceName: 'Ofis bilgisayarı', clientFingerprint: 'b'.repeat(64), createdAt: new Date(now - 86400000).toISOString(), lastSeenAt: new Date(now - 3600000).toISOString(), expiresAt: new Date(now + 86400000).toISOString(), revokedAt: null, isCurrent: false }
];
const recoveryCodes = ['RECOVERY-ONE', 'RECOVERY-TWO'];
const checks = [];
const failures = [];

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-surface-003' });
}

async function createContext(viewport) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    let data;
    if (path === '/api/browser-auth/session') data = { user, csrfToken: 'csrf' };
    else if (path === '/api/auth/sessions' && method === 'GET') data = sessions;
    else if (path.startsWith('/api/auth/sessions/') && method === 'DELETE') {
      const id = decodeURIComponent(path.split('/').pop());
      sessions = sessions.map(session => session.id === id ? { ...session, revokedAt: new Date().toISOString() } : session);
      data = { revoked: true };
    } else if (path === '/api/auth/mfa') data = { enabled: true, remainingRecoveryCodes: 8 };
    else if (path === '/api/auth/mfa/recovery-codes') data = { recoveryCodes };
    else if (path === '/api/auth/api-keys') data = [];
    else if (path === '/api/notifications/preferences/me' && method === 'GET') data = preferences;
    else if (path === '/api/notifications/preferences/me' && method === 'PUT') {
      preferences = request.postDataJSON();
      data = preferences;
    } else if (path === '/api/organizations') data = [{ id: 'org-1', tenantKey: 'org-1', name: 'Zumbo' }];
    else if (path === '/api/auth/roles' || path === '/api/auth/users' || path === '/api/projects' || path === '/api/teams') data = [];
    else if (path.startsWith('/api/audit/entity/Organization/')) data = [];
    else if (path.startsWith('/api/notifications/')) data = [];
    else data = [];
    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error' && !/WebSocket|signalr|Failed to load resource/.test(message.text())) {
      failures.push(`${label}: ${message.text()}`);
    }
  });
}

try {
  const desktopContext = await createContext({ width: 1440, height: 1000 });
  const desktop = await desktopContext.newPage();
  diagnostics(desktop, 'desktop');
  await desktop.goto(`${server.origin}/desktop-bulma/index.html#section=settings`, { waitUntil: 'domcontentloaded' });
  await desktop.getByRole('heading', { name: 'Ayarlar' }).waitFor();
  await desktop.getByText('Bu tarayıcı', { exact: true }).waitFor();
  assert.equal(await desktop.locator('.session-row.current').count(), 1);
  checks.push('desktop-current-session-visible');

  desktop.once('dialog', dialog => dialog.accept());
  const officeSession = desktop.locator('.session-row').filter({ hasText: 'Ofis bilgisayarı' });
  await officeSession.getByRole('button', { name: 'Seçilen cihaz oturumunu kapat' }).click();
  await officeSession.getByText('Kapalı', { exact: true }).waitFor();
  assert.ok(sessions.find(session => session.id === 'session-office').revokedAt);
  checks.push('desktop-targeted-session-revoke');

  await desktop.getByLabel('Sessize alınan bildirim türleri').fill('Assignment, mention, assignment');
  await desktop.locator('.preference-form').getByRole('button', { name: 'Kaydet' }).click();
  assert.deepEqual(preferences.mutedTypes, ['Assignment', 'mention']);
  checks.push('desktop-notification-preferences');

  await desktop.getByLabel('Kurtarma kodu yenileme parolası').fill('P@ssword123');
  await desktop.getByLabel('Kurtarma kodu yenileme doğrulaması').fill('123456');
  desktop.once('dialog', dialog => dialog.accept());
  await desktop.getByRole('button', { name: 'Kodları yenile' }).click();
  await desktop.getByText('RECOVERY-ONE', { exact: true }).waitFor();
  assert.equal(await desktop.locator('.recovery-output code').count(), 2);
  await desktop.getByRole('button', { name: 'Kurtarma kodlarını kapat' }).click();
  assert.equal(await desktop.locator('.recovery-output').count(), 0);
  checks.push('desktop-recovery-secret-once');
  await desktop.screenshot({ path: resolve(output, 'desktop-security.png'), fullPage: true });
  await desktopContext.close();

  const mobileContext = await createContext({ width: 390, height: 844 });
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile');
  await mobile.goto(`${server.origin}/mobile-ionic/index.html#/app/profile`, { waitUntil: 'domcontentloaded' });
  await mobile.getByRole('heading', { name: 'Aktif oturumlar' }).waitFor();
  await mobile.getByText('Bu tarayıcı', { exact: true }).waitFor();
  assert.equal(await mobile.locator('.mobile-session-row.current').count(), 1);
  assert.equal(await mobile.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  checks.push('mobile-security-parity-no-overflow');
  await mobile.screenshot({ path: resolve(output, 'mobile-security.png'), fullPage: true });
  await mobileContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = { schemaVersion: 1, taskId: 'V3-SURFACE-003', passed: failures.length === 0, checks, failures };
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 5);
console.log('V3-SURFACE-003 browser passed: sessions, preferences, recovery secrets and mobile parity.');
