import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const require = createRequire(import.meta.url);
const core = require(resolve(root, 'shared/audit-privacy-core.js'));
const desktop = await readFile(resolve(root, 'desktop-bulma/audit-center.js'), 'utf8');
const desktopPrivacy = await readFile(resolve(root, 'desktop-bulma/privacy-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobile = await readFile(resolve(root, 'mobile-ionic/privacy-center.js'), 'utf8');
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');

function storage() {
  const values = new Map();
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: key => values.delete(key)
  };
}

test('audit filters require paired resources and enforce ordered bounded dates', () => {
  assert.throws(
    () => core.normalizedAuditFilters({ entityType: 'Project' }),
    error => error.code === 'AUDIT_ENTITY_PAIR_REQUIRED'
  );
  assert.throws(
    () => core.normalizedAuditFilters({ from: '2026-07-23', to: '2026-07-22' }),
    error => error.code === 'AUDIT_DATE_ORDER_INVALID'
  );
  assert.throws(
    () => core.normalizedAuditFilters({ from: '2025-01-01', to: '2026-07-23' }),
    error => error.code === 'AUDIT_DATE_RANGE_INVALID'
  );

  const url = core.auditSearchUrl({
    actorUserId: ' user-1 ',
    action: 'WorkItemUpdated',
    entityType: 'WorkItem',
    entityId: 'item-1',
    from: '2026-07-01',
    to: '2026-07-23'
  }, { organizationId: 'org-1', pageSize: 500, cursor: 'next/token' });
  assert.match(url, /^\/api\/audit\?/);
  assert.match(url, /organizationId=org-1/);
  assert.match(url, /pageSize=100/);
  assert.match(url, /cursor=next%2Ftoken/);
});

test('audit projection redacts marked values and bounds visible payloads', () => {
  const changes = core.safeAuditChanges({
    changes: [
      { field: 'PasswordHash', oldValue: 'old-secret', newValue: 'new-secret', redacted: true },
      { field: 'Title', oldValue: 'Before', newValue: 'A'.repeat(900), redacted: false }
    ]
  });
  assert.deepEqual(changes[0], {
    field: 'PasswordHash',
    oldValue: '[REDACTED]',
    newValue: '[REDACTED]',
    redacted: true
  });
  assert.equal(changes[1].newValue.length, 500);
  assert.doesNotMatch(JSON.stringify(changes), /old-secret|new-secret/);
});

test('audit role and integrity states do not overclaim access or empty history', () => {
  assert.equal(core.hasPermission({ roles: ['OrganizationAdmin'] }, [], 'AuditReadAll'), false);
  assert.equal(core.hasPermission({ roles: ['AuditReader'] }, [], 'AuditReadAll'), true);
  assert.equal(core.hasPermission(
    { roles: ['Compliance'] },
    [{ name: 'Compliance', permissions: ['AuditReadAll'] }],
    'AuditReadAll'
  ), true);
  assert.equal(core.integrityState({ verified: 0, valid: true, completeHistory: true }), 'empty');
  assert.equal(core.integrityState({ verified: 4, valid: false, completeHistory: true }), 'invalid');
  assert.equal(core.integrityState({ verified: 4, valid: true, completeHistory: false }), 'partial');
  assert.equal(core.integrityState({ verified: 4, valid: true, completeHistory: true }), 'valid');
});

test('privacy receipt is scoped to tenant and user and rejects malformed tokens', () => {
  const session = storage();
  const owner = { id: 'user-1', organizationId: 'org-1' };
  const otherTenant = { id: 'user-1', organizationId: 'org-2' };
  const receipt = {
    statusToken: 'valid_status_token_1234567890',
    job: { id: 'privacy-job-1' }
  };

  assert.equal(core.savePrivacyReceipt(session, owner, receipt), true);
  assert.deepEqual(core.loadPrivacyReceipt(session, owner), {
    id: 'privacy-job-1',
    statusToken: receipt.statusToken
  });
  assert.equal(core.loadPrivacyReceipt(session, otherTenant), null);
  assert.equal(core.savePrivacyReceipt(session, owner, {
    statusToken: '<script>',
    job: { id: 'privacy-job-2' }
  }), false);
  core.clearPrivacyReceipt(session, owner);
  assert.equal(core.loadPrivacyReceipt(session, owner), null);
});

test('privacy workflow requires exact confirmation and exposes recovery states', () => {
  assert.throws(
    () => core.validateAnonymization({ password: 'secret', confirmation: 'anonymize' }),
    error => error.code === 'PRIVACY_CONFIRMATION_REQUIRED'
  );
  assert.deepEqual(core.validateAnonymization({
    password: 'secret',
    confirmation: 'ANONYMIZE'
  }), { password: 'secret', confirmation: 'ANONYMIZE' });
  assert.equal(core.canRetryPrivacy({ state: 'Failed' }), true);
  assert.equal(core.canReconcilePrivacy({
    state: 'Running',
    updatedAt: '2026-07-23T08:00:00Z'
  }, Date.parse('2026-07-23T08:03:00Z')), true);
  assert.equal(core.isPrivacyTerminal({ state: 'Completed' }), true);
});

test('desktop surface is role-gated, redacted and confirmation-protected', () => {
  assert.match(desktop, /canViewAuditCenter/);
  assert.match(desktop, /core\.safeAuditChanges/);
  assert.match(desktop, /vm\.auditReferenceLabel = shortId/);
  assert.match(desktop, /\/api\/audit\/integrity\//);
  assert.match(desktopHtml, /ng-if="vm\.canViewAuditCenter\(\)"/);
  assert.match(desktopHtml, /<dt>Kaynak<\/dt><dd>{{vm\.auditEntityLabel\(vm\.auditCenter\.selected\)}}<\/dd>/);
  assert.match(desktopHtml, /<dt>Korelasyon<\/dt><dd>{{vm\.auditReferenceLabel\(vm\.auditCenter\.selected\.correlationId\)}}<\/dd>/);
  assert.doesNotMatch(desktopHtml, /selected\.entityType}} · {{vm\.auditCenter\.selected\.entityId/);
  assert.doesNotMatch(desktopHtml, /<dd>{{vm\.auditCenter\.selected\.correlationId}}<\/dd>/);
  assert.doesNotMatch(desktopHtml, /selected\.oldValue|selected\.newValue|statusToken/);
  assert.match(desktopPrivacy, /\$window\.confirm/);
  assert.match(desktopPrivacy, /privacyStatusToken: receipt\.statusToken/);
  assert.match(desktopPrivacy, /refresh: false/);
  assert.match(desktopPrivacy, /if \(storedReceipt\) schedule\(storedReceipt/);
});

test('mobile surface uses durable export, status, retry and reconciliation APIs', () => {
  for (const method of [
    'exportPrivacyData', 'createPrivacyJob', 'privacyJobStatus',
    'privacyJob', 'retryPrivacyJob', 'reconcilePrivacyJob'
  ]) {
    assert.match(mobileApi, new RegExp(`${method}: function`), `${method} adapter is missing`);
  }
  assert.match(mobile, /\$ionicPopup\.confirm/);
  assert.match(mobile, /sessionStorage/);
  assert.match(mobile, /if \(storedReceipt\) schedule\(storedReceipt/);
  assert.match(mobileHtml, /vm\.privacyProgress\(vm\.privacyWorkflow\)/);
  assert.match(mobileHtml, /vm\.retryPrivacyWorkflow\(\)/);
  assert.match(mobileHtml, /vm\.reconcilePrivacyWorkflow\(\)/);
  assert.doesNotMatch(mobileHtml, /statusToken/);
});
