import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/portfolio-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/portfolio-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/portfolio-center.css'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/portfolio-center.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/portfolio-center.css'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');

test('portfolio core builds hierarchy and strips response-only fields', () => {
  const rows = core.tree([
    { id: 'child', name: 'Child', parentInitiativeId: 'parent' },
    { id: 'parent', name: 'Parent', parentInitiativeId: null }
  ]);
  assert.deepEqual(rows.map(row => [row.item.id, row.depth]), [['parent', 0], ['child', 1]]);

  const draft = core.portfolio();
  draft.name = ' Delivery ';
  draft.canEdit = false;
  draft.viewerUserIds = ['viewer', 'viewer'];
  assert.deepEqual(core.portfolioPayload(draft), {
    name: 'Delivery',
    description: null,
    viewerUserIds: ['viewer']
  });
});

test('portfolio core validates initiative and directed dependency inputs', () => {
  const initiative = core.initiative('owner');
  assert.match(core.validateInitiative(initiative), /adı/);
  initiative.name = 'Platform';
  assert.match(core.validateInitiative(initiative), /proje/);
  initiative.projectIds = ['project-1'];
  initiative.confidence = 101;
  assert.match(core.validateInitiative(initiative), /0 ile 100/);
  initiative.confidence = 70;
  assert.equal(core.validateInitiative(initiative), null);

  const dependency = core.dependency();
  dependency.sourceProjectId = 'project-1';
  dependency.targetProjectId = 'project-1';
  dependency.description = 'Blocks';
  assert.match(core.validateDependency(dependency), /kendisine/);
});

test('desktop and mobile expose real responsive portfolio workflows', () => {
  assert.match(desktopSource, /\/api\/portfolios/);
  assert.match(desktopSource, /status-updates/);
  assert.match(desktopSource, /dependencies/);
  assert.match(desktopSource, /apiClient\.delete\('\/api\/portfolios\//);
  assert.match(desktopHtml, /vm\.archivePortfolio\(\)/);
  assert.match(desktopSource, /portfolioTreeRows = core\.tree/);
  assert.match(desktopHtml, /vm\.activeSection === 'portfolios'/);
  assert.match(desktopHtml, /scope="col"/);
  assert.match(desktopHtml, /row\.item\.canUpdateStatus/);
  assert.match(desktopHtml, /vm\.initiativeDraft\.canUpdateStatus/);
  assert.match(desktopCss, /grid-template-columns:/);
  assert.match(desktopCss, /overflow-x:\s*auto/);

  assert.match(mobileSource, /\/api\/portfolios/);
  assert.match(mobileSource, /apiClient\.delete\('\/api\/portfolios\//);
  assert.match(mobileHtml, /vm\.archivePortfolio\(\)/);
  assert.match(mobileSource, /portfolioTreeRows = core\.tree/);
  assert.match(mobileHtml, /templates\/portfolios\.html/);
  assert.match(mobileHtml, /vm\.portfolio\.canEdit/);
  assert.match(mobileHtml, /row\.item\.canUpdateStatus/);
  assert.match(mobileHtml, /vm\.initiativeDraft\.canUpdateStatus/);
  assert.match(mobileSource, /!vm\.initiativeDraft\.canUpdateStatus/);
  assert.match(mobileCss, /min-height:\s*44px/);
  assert.doesNotMatch(mobileCss, /overflow-x:\s*visible/);
  assert.match(apiClientSource, /dashboards\|portfolios/);
});
