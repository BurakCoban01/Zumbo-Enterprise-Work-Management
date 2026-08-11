import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('../projects/modern-desktop/src/app/', import.meta.url);
const files = await Promise.all([
  'features/automation/automation.models.ts',
  'features/automation/automation.core.ts',
  'features/automation/automation.service.ts',
  'features/automation/automation.page.ts',
  'features/automation/automation.page.html',
  'workspace.page.ts',
  'workspace.page.html'
].map(path => readFile(new URL(path, root), 'utf8')));

test('modern Automation keeps rule, run, schedule and template contracts in a bounded feature', () => {
  const [models, core, service, page, template, workspace, workspaceTemplate] = files;
  assert.match(models, /interface AutomationRuleDraft/);
  assert.match(models, /interface WorkRecurrenceDraft/);
  assert.match(models, /interface WorkTemplateDraft/);
  assert.match(core, /function ruleRequest/);
  assert.match(core, /function recurrenceRequest/);
  assert.match(core, /function validRule/);
  assert.match(service, /\/api\/automations\?projectId=/);
  assert.match(service, /\/api\/automations\/runs\?projectId=/);
  assert.match(service, /\/api\/work-items\/templates\?projectId=/);
  assert.match(service, /\/api\/work-items\/recurrences\?projectId=/);
  assert.match(service, /\/api\/audit\/entity\//);
  assert.match(service, /ifMatch: version/);
  assert.match(service, /ifMatch: draft\.version/);
  assert.match(page, /WorkflowManage/);
  assert.match(page, /WorkItemCreate/);
  assert.match(page, /WorkItemUpdate/);
  for (const tab of ['rules', 'runs', 'schedules', 'templates', 'activity']) {
    assert.match(template, new RegExp(`tab\\(\\) === '${tab}'`));
  }
  assert.match(workspace, /import \{ AutomationPage \}/);
  assert.match(workspaceTemplate, /<zumbo-automation-page/);
  assert.match(workspace, /'automation', 'task'/);
  assert.doesNotMatch(service + page + template + workspaceTemplate, /fresh=/);
});
