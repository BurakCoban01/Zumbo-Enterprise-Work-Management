import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('../projects/modern-desktop/src/app/', import.meta.url);
const files = await Promise.all([
  'features/intake/intake.models.ts', 'features/intake/intake-form.core.ts',
  'features/intake/intake-submission.core.ts', 'features/intake/intake.service.ts',
  'features/intake/intake.page.ts', 'features/intake/intake.page.html',
  'workspace.page.ts', 'workspace.page.html'
].map(path => readFile(new URL(path, root), 'utf8')));

test('modern Intake keeps form, submission and triage contracts in a bounded feature', () => {
  const [models, formCore, submissionCore, service, page, template, workspace, workspaceTemplate] = files;
  assert.match(models, /interface IntakeForm /);
  assert.match(models, /interface PublishedIntakeForm /);
  assert.match(formCore, /validateIntakeDraft/);
  assert.match(submissionCore, /submissionFormData/);
  assert.match(service, /\/api\/intake\/forms\?projectId=/);
  assert.match(service, /idempotencyKey: this\.api\.newIdempotencyKey\(\)/);
  assert.match(service, /page=1&pageSize=100/);
  assert.match(page, /WorkflowManage/);
  assert.match(page, /WorkItemCreate/);
  assert.match(page, /WorkItemUpdate/);
  assert.match(template, /Intake ve triage merkezi/);
  assert.match(template, /Triage kuyruğu/);
  assert.match(workspace, /import \{ IntakePage \}/);
  assert.match(workspaceTemplate, /<zumbo-intake-page/);
  assert.doesNotMatch(service + page + template, /fresh=/);
});
