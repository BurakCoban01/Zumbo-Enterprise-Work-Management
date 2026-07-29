import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/goal-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/goal-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/goal-center.css'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/goal-center.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/goal-center.css'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');

test('goal core emits date-only definition and unique source links', () => {
  const draft = core.goal(new Date(2026, 7, 15));
  draft.name = ' Activation ';
  draft.viewerUserIds = ['viewer', 'viewer'];
  draft.initiativeLinks = [
    { portfolioId: 'portfolio-1', initiativeId: 'initiative-1' },
    { portfolioId: 'portfolio-1', initiativeId: 'initiative-1' }
  ];
  draft.projectIds = ['project-1', 'project-1'];
  assert.deepEqual(core.goalPayload(draft), {
    name: 'Activation',
    description: null,
    periodStart: '2026-07-01',
    periodEnd: '2026-09-30',
    viewerUserIds: ['viewer'],
    initiativeLinks: [{ portfolioId: 'portfolio-1', initiativeId: 'initiative-1' }],
    projectIds: ['project-1']
  });
});

test('goal core validates measurable direction and update confidence', () => {
  const result = core.keyResult('owner-1');
  result.name = 'Lead time';
  result.direction = 'Decrease';
  result.baselineValue = 10;
  result.targetValue = 2;
  result.initialValue = 10;
  result.unit = 'gün';
  assert.equal(core.validateKeyResult(result), null);
  result.targetValue = 12;
  assert.match(core.validateKeyResult(result), /küçük/);
  assert.match(core.validateUpdate({ note: 'Update', confidence: 101 }, 'İlerleme'), /0 ile 100/);
});

test('desktop and mobile expose complete goal history and permission workflows', () => {
  for (const source of [desktopSource, mobileSource]) {
    assert.match(source, /\/api\/goals/);
    assert.match(source, /key-results/);
    assert.match(source, /progress-updates/);
    assert.match(source, /status-updates/);
    assert.match(source, /apiClient\.delete\('\/api\/goals\//);
  }
  assert.match(desktopHtml, /vm\.activeSection === 'goals'/);
  assert.match(desktopHtml, /item\.canUpdate/);
  assert.match(desktopHtml, /vm\.goal\.canUpdateStatus/);
  assert.match(desktopHtml, /progressUpdates/);
  assert.match(desktopCss, /grid-template-columns:/);
  assert.match(mobileHtml, /templates\/goals\.html/);
  assert.match(mobileHtml, /ui-sref="goal-center"/);
  assert.match(mobileHtml, /item\.canUpdate/);
  assert.match(mobileHtml, /vm\.goal\.canUpdateStatus/);
  assert.match(mobileCss, /min-height:\s*44px/);
  assert.match(apiClientSource, /portfolios\|goals/);
});
