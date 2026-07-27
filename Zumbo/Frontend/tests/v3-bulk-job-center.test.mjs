import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const require = createRequire(import.meta.url);
const core = require(resolve(root, 'shared/bulk-job-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/bulk-job-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/bulk-job-center.js'), 'utf8');
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileApp = await readFile(resolve(root, 'mobile-ionic/app.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');

test('import parser accepts supported roots and normalizes optional fields', () => {
  const parsed = core.parseImport(JSON.stringify({
    items: [{
      sourceKey: ' ROW-1 ',
      boardId: ' board-1 ',
      title: ' First task ',
      type: '',
      priority: '',
      assigneeUserId: ''
    }]
  }), 240);
  assert.equal(parsed.valid, true);
  assert.deepEqual(parsed.rows, [{
    sourceKey: 'ROW-1',
    boardId: 'board-1',
    title: 'First task',
    type: 'Task',
    priority: 'Medium',
    assigneeUserId: null,
    dueDate: null,
    parentId: null,
    teamId: null,
    customFields: []
  }]);
  assert.deepEqual(core.importRequest('project-1', parsed, true), {
    projectId: 'project-1',
    items: parsed.rows,
    dryRun: true
  });
});

test('import parser rejects malformed, oversized and ambiguous input', () => {
  assert.equal(core.parseImport('{', 1).valid, false);
  assert.match(core.parseImport('[]', 2).errors[0], /1 ile 5000/);
  assert.match(core.parseImport('[{}]', 4).errors[0], /zorunlu/);
  assert.match(core.parseImport(JSON.stringify([
    { sourceKey: 'same', boardId: 'board', title: 'One' },
    { sourceKey: 'same', boardId: 'board', title: 'Two' }
  ]), 200).errors[0], /yinelenen sourceKey/);
  assert.match(core.parseImport('[{}]', core.limits.maxInputBytes + 1).errors[0], /5 MB/);
});

test('job state model covers progress, terminal actions and artifact expiry', () => {
  const active = { state: 'Running', totalItems: 8, processedItems: 3 };
  assert.equal(core.progress(active), 38);
  assert.equal(core.canCancel(active), true);
  assert.equal(core.canRetry(active), false);
  assert.equal(core.state(active).tone, 'info');

  const partial = { state: 'CompletedWithErrors', totalItems: 8, processedItems: 8 };
  assert.equal(core.progress(partial), 100);
  assert.equal(core.canCancel(partial), false);
  assert.equal(core.canRetry(partial), true);
  assert.equal(core.state(partial).tone, 'warning');

  const expired = {
    state: 'Completed',
    artifactsExpireAt: '2026-07-20T10:00:00Z'
  };
  assert.equal(core.artifactsExpired(expired, '2026-07-21T10:00:00Z'), true);
  assert.equal(core.state(expired, '2026-07-21T10:00:00Z').tone, 'muted');
});

test('desktop and mobile use durable job APIs without replacing synchronous bulk actions', () => {
  for (const endpoint of [
    '/api/work-items/bulk/jobs?projectId=',
    '/api/work-items/bulk/jobs/import',
    '/api/work-items/bulk/jobs/export',
    '/cancel',
    '/retry',
    "'errors' : 'result'"
  ]) {
    assert.ok((desktopSource + mobileApi).includes(endpoint), `${endpoint} is not surfaced`);
  }
  assert.match(desktopSource, /idempotencyKey: core\.idempotencyKey/);
  assert.match(desktopSource, /\$window\.confirm\('Çalışan işi iptal etmek istiyor musunuz\?'\)/);
  assert.match(mobileSource, /core\.idempotencyKey/);
  assert.match(desktopHtml, /vm\.bulkMove\('In Progress'\)/);
  assert.match(desktopHtml, /vm\.bulkAssignToMe\(\)/);
  assert.match(desktopHtml, /vm\.bulkArchive\(\)/);
});

test('desktop and mobile templates expose launch, history, recovery and expiry states', () => {
  assert.match(desktopHtml, /class="job-center"/);
  assert.match(desktopHtml, /vm\.submitBulkImport\(true\)/);
  assert.match(desktopHtml, /vm\.retryBulkJob/);
  assert.match(desktopHtml, /vm\.bulkJobArtifactsExpired/);

  assert.match(mobileApp, /state\('project-jobs'/);
  assert.match(mobileHtml, /templates\/bulk-job-center\.html/);
  assert.match(mobileHtml, /vm\.submitImport\(true\)/);
  assert.match(mobileHtml, /vm\.submitExport\(false\)/);
  assert.match(mobileHtml, /vm\.requestCancel\(vm\.selected\)/);
  assert.match(mobileHtml, /vm\.artifactsExpired\(vm\.selected\)/);
  assert.match(mobileSource, /apiClient\.cancelScope\('mobile-bulk-jobs'\)/);
  assert.match(mobileSource, /vm\.pwa\.offline/);
});
