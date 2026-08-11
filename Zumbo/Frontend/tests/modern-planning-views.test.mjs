import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..', 'projects', 'modern-desktop', 'src', 'app');
const [workspace, template, page, core, service, workItems] = await Promise.all([
  readFile(resolve(root, 'workspace.page.html'), 'utf8'),
  readFile(resolve(root, 'features/planning/views/project-planning-view.page.html'), 'utf8'),
  readFile(resolve(root, 'features/planning/views/project-planning-view.page.ts'), 'utf8'),
  readFile(resolve(root, 'features/planning/views/project-planning-view.core.ts'), 'utf8'),
  readFile(resolve(root, 'features/planning/views/project-planning-view.service.ts'), 'utf8'),
  readFile(resolve(root, 'features/work-items/project-work-item.service.ts'), 'utf8')
]);

test('modern planning routes preserve complete, permission-driven and CSP-safe contracts', () => {
  for (const mode of ['calendar', 'timeline', 'roadmap']) assert.match(workspace, new RegExp(`activeView\\(\\) === '${mode}'`));
  assert.match(workItems, /loadAll\(projectId: string\)/);
  assert.match(workItems, /Math\.ceil\(first\.totalCount \/ pageSize\)/);
  assert.match(service, /loadSprints\(projectId, page\.nextCursor/);
  assert.match(core, /workflow\.statuses/);
  assert.match(core, /percentage: item\.units \/ 100/);
  assert.match(page, /hasPermission\('WorkItemUpdate'\)/);
  assert.match(template, /\[draggable\]="canEdit\(\)"/);
  assert.match(template, /changeDueDate\(event\.task/);
  assert.doesNotMatch(template, /\[style\.|\sstyle=/);
  assert.match(template, /roadmap-segment/);
});
