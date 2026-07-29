import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const security = require(resolve(root, 'shared/account-security-core.js'));
const desktop = await readFile(resolve(root, 'desktop-bulma/settings.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobile = await readFile(resolve(root, 'mobile-ionic/profile-security.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const api = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');

test('notification type normalization is stable, trimmed and case-insensitively unique', () => {
  assert.deepEqual(
    security.normalizeMutedTypes(' Assignment, mention, assignment, , DueDate '),
    ['Assignment', 'mention', 'DueDate']
  );
});

test('session activity rejects revoked, expired and malformed records', () => {
  const now = Date.parse('2026-07-23T09:00:00Z');
  assert.equal(security.isSessionActive({ expiresAt: '2026-07-23T10:00:00Z', revokedAt: null }, now), true);
  assert.equal(security.isSessionActive({ expiresAt: '2026-07-23T08:00:00Z', revokedAt: null }, now), false);
  assert.equal(security.isSessionActive({ expiresAt: '2026-07-23T10:00:00Z', revokedAt: '2026-07-23T08:30:00Z' }, now), false);
  assert.equal(security.isSessionActive({ expiresAt: 'invalid', revokedAt: null }, now), false);
});

test('one-time MFA values are cleared without touching durable status', () => {
  const state = { mfaSetup: { secret: 'secret' }, recoveryCodes: ['one'], mfaStatus: { enabled: true } };
  assert.deepEqual(security.clearOneTimeSecrets(state), {
    mfaSetup: null,
    recoveryCodes: [],
    mfaStatus: { enabled: true }
  });
});

test('session projection keeps active devices and only the two most recent closed sessions', () => {
  const now = Date.parse('2026-07-23T09:00:00Z');
  const sessions = [
    { id: 'old-closed', expiresAt: '2026-07-24T09:00:00Z', revokedAt: '2026-07-20T09:00:00Z', lastSeenAt: '2026-07-20T09:00:00Z' },
    { id: 'active', expiresAt: '2026-07-24T09:00:00Z', revokedAt: null, lastSeenAt: '2026-07-21T09:00:00Z' },
    { id: 'new-closed', expiresAt: '2026-07-24T09:00:00Z', revokedAt: '2026-07-23T08:00:00Z', lastSeenAt: '2026-07-23T08:00:00Z' },
    { id: 'middle-closed', expiresAt: '2026-07-24T09:00:00Z', revokedAt: '2026-07-22T09:00:00Z', lastSeenAt: '2026-07-22T09:00:00Z' }
  ];
  assert.deepEqual(security.selectVisibleSessions(sessions, now).map(session => session.id), [
    'active', 'new-closed', 'middle-closed'
  ]);
});

test('desktop settings expose current-session revoke and recovery-code lifecycle', () => {
  for (const endpoint of ['/api/auth/sessions', '/api/auth/mfa/recovery-codes']) assert.ok(desktop.includes(endpoint));
  for (const method of [
    'vm.revokeSession', 'vm.regenerateMfaRecoveryCodes', 'vm.dismissRecoveryCodes',
    'vm.clearSettingsOneTimeSecrets'
  ]) assert.ok(desktop.includes(method));
  assert.doesNotMatch(
    desktop.match(/vm\.loadSettings = function\(\) \{[\s\S]*?\n    \};/)?.[0] || '',
    /clearOneTimeSecrets/,
    'Background settings reloads must not erase one-time MFA values.'
  );
  assert.match(desktop, /vm\.entitySaving \|\| vm\.mfaSetup \|\| vm\.recoveryCodes\.length/);
  assert.match(desktop, /apiClient\.cancelPending\('mfa-recovery-session-rotation'\)/);
  assert.match(api, /apiClient\.cancelPending\('mfa-recovery-session-rotation'\)/);
  assert.match(desktopHtml, /ng-disabled="vm\.settingsLoading \|\| vm\.entitySaving"/);
  assert.match(desktop, /apiClient\.clearSession\('current-session-revoked'\)/);
  assert.match(desktopHtml, /session\.isCurrent/);
  assert.match(desktopHtml, /Bu liste kapatıldıktan sonra yeniden gösterilmez/);
});

test('mobile profile provides notification, MFA and targeted session parity', () => {
  for (const method of [
    'notificationPreferences', 'saveNotificationPreferences', 'mfaStatus',
    'beginMfaSetup', 'confirmMfaSetup', 'disableMfa',
    'regenerateMfaRecoveryCodes', 'sessions', 'revokeSession'
  ]) {
    assert.match(api, new RegExp(`${method}: function`), `${method} adapter is missing`);
  }
  assert.match(mobile, /ProfileSecurityController/);
  assert.match(mobile, /apiClient\.clearSession\('current-session-revoked'\)/);
  assert.match(mobileHtml, /vm\.savePreferences\(\)/);
  assert.match(mobileHtml, /vm\.regenerateRecoveryCodes\(\)/);
  assert.match(mobileHtml, /vm\.revokeSession\(session\)/);
});
