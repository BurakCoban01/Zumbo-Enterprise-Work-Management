import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('../projects/modern-desktop/src/app/', import.meta.url);
const [models, core, service, page, template, workspace, workspaceTemplate] = await Promise.all([
  'features/jobs/jobs.models.ts', 'features/jobs/jobs.core.ts', 'features/jobs/jobs.service.ts',
  'features/jobs/jobs.page.ts', 'features/jobs/jobs.page.html', 'workspace.page.ts', 'workspace.page.html'
].map(path => readFile(new URL(path, root), 'utf8')));

test('modern Jobs preserves durable import, export, recovery and artifact contracts', () => {
  assert.match(models, /interface BulkJob /);
  assert.match(core, /maxInputItems: 5000/);
  assert.match(core, /function parseImport/);
  assert.match(core, /function canCancel/);
  assert.match(service, /\/api\/work-items\/bulk\/jobs\?projectId=/);
  assert.match(service, /\/bulk\/jobs\/import/);
  assert.match(service, /\/bulk\/jobs\/export/);
  assert.match(service, /\/cancel/);
  assert.match(service, /\/retry/);
  assert.match(service, /'errors' : 'result'/);
  assert.match(service, /idempotencyKey: this\.api\.newIdempotencyKey/);
  assert.match(service, /ifMatch: job\.version/);
  assert.match(page, /WorkItemCreate/);
  assert.match(page, /WorkItemView/);
  assert.match(page, /WorkItemUpdate/);
  assert.match(page, /setTimeout\(\(\) => this\.load\(true\), 2500\)/);
  assert.match(template, /JSON içe aktar/);
  assert.match(template, /Proje dışa aktarımı/);
  assert.match(workspace, /import \{ JobsPage \}/);
  assert.match(workspaceTemplate, /<zumbo-jobs-page/);
  assert.doesNotMatch(service + page + template + workspaceTemplate, /fresh=/);
});
