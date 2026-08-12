import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..', 'projects', 'modern-desktop', 'src', 'app');
const [workspace, page, template, core, service] = await Promise.all([
  readFile(resolve(root, 'workspace.page.html'), 'utf8'),
  readFile(resolve(root, 'features/reporting/project-reporting.page.ts'), 'utf8'),
  readFile(resolve(root, 'features/reporting/project-reporting.page.html'), 'utf8'),
  readFile(resolve(root, 'features/reporting/project-reporting.core.ts'), 'utf8'),
  readFile(resolve(root, 'features/reporting/project-reporting.service.ts'), 'utf8')
]);

test('modern reporting routes preserve complete report and dashboard contracts', () => {
  for (const mode of ['workload', 'reports', 'dashboards']) assert.match(workspace, new RegExp(`activeView\\(\\) === '${mode}'`));
  for (const report of ['project-summary', 'status-distribution', 'user-workload', 'due-date-risks', 'flow-time', 'completion-rate', 'team-performance']) assert.match(service, new RegExp(`/reports/${report}`));
  assert.match(service, /rawResponse: true/);
  assert.match(service, /loadAll\(projectId\)/);
  assert.match(core, /statusTotal \? Math\.round/);
  assert.match(core, /localeCompare\(right\.teamName, 'tr-TR'\)/);
  assert.match(template, /Kapasite eşiği yapılandırılmadı/);
  assert.match(template, /performans sıralaması değildir/);
  assert.match(template, /<table/);
  assert.match(page, /validateDashboard\(value\)/);
  assert.match(page, /value\.canEdit/);
  assert.doesNotMatch(template, /\[style\.|\sstyle=/);
});
