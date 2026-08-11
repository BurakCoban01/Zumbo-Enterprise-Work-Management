import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..', 'projects', 'modern-desktop', 'src', 'app');
const [workspace, page, template, service] = await Promise.all([
  readFile(resolve(root, 'workspace.page.html'), 'utf8'),
  readFile(resolve(root, 'features/catalog/project-catalog.page.ts'), 'utf8'),
  readFile(resolve(root, 'features/catalog/project-catalog.page.html'), 'utf8'),
  readFile(resolve(root, 'features/catalog/project-catalog.service.ts'), 'utf8')
]);

test('modern project catalog preserves lifecycle, permission and audit contracts', () => {
  assert.match(workspace, /activeView\(\) === 'catalog'/);
  for (const resource of ['templates', 'components', 'versions', 'releases', 'milestones']) assert.match(service, new RegExp(`/\\$\\{encodeURIComponent\\(projectId\\)\\}/${resource}`));
  assert.match(service, /\/approve/);
  assert.match(service, /\/publish/);
  assert.match(service, /\/complete/);
  assert.match(page, /canManageProjectCatalog/);
  assert.match(page, /canReleaseProjectCatalog/);
  assert.match(page, /Project(?:Template|Component|Version|Release|Milestone)/);
  for (const tab of ['releases', 'milestones', 'components', 'templates', 'activity']) assert.match(template, new RegExp(`setTab\\('${tab}'\\)`));
  assert.match(template, /role="tablist"/);
  assert.doesNotMatch(template, /\[style\.|\sstyle=/);
});
