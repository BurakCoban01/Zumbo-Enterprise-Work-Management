import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/capacity-planning-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/capacity-center.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/capacity-center.css'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/capacity-center.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/capacity-center.css'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');

function validPlan() {
  return {
    id: 'plan-1',
    name: 'Teslimat kapasitesi',
    description: '',
    periodStart: new Date(2026, 6, 6),
    periodEnd: new Date(2026, 6, 19),
    portfolioId: '',
    projectIds: ['project-1', 'project-2'],
    members: [{ userId: 'user-1', teamId: '', weeklyCapacityHours: 40 }],
    allocations: [{
      id: null,
      userId: 'user-1',
      projectId: 'project-1',
      startDate: new Date(2026, 6, 6),
      endDate: new Date(2026, 6, 19),
      percent: 60
    }],
    viewerUserIds: ['viewer-1'],
    version: 2
  };
}

test('capacity core emits date-only provider-neutral payload', () => {
  const draft = validPlan();
  assert.deepEqual(core.payload(draft), {
    name: 'Teslimat kapasitesi',
    description: null,
    periodStart: '2026-07-06',
    periodEnd: '2026-07-19',
    portfolioId: null,
    projectIds: ['project-1', 'project-2'],
    members: [{ userId: 'user-1', teamId: null, weeklyCapacityHours: 40 }],
    allocations: [{
      id: null,
      userId: 'user-1',
      projectId: 'project-1',
      startDate: '2026-07-06',
      endDate: '2026-07-19',
      percent: 60
    }],
    viewerUserIds: ['viewer-1']
  });
  assert.equal(core.validate(draft), null);
});

test('capacity core rejects invalid bounds and cross-plan allocations', () => {
  const draft = validPlan();
  draft.members[0].weeklyCapacityHours = 169;
  assert.match(core.validate(draft), /168/);
  draft.members[0].weeklyCapacityHours = 40;
  draft.allocations[0].projectId = 'foreign-project';
  assert.match(core.validate(draft), /plan kapsamındaki/);
  draft.allocations[0].projectId = 'project-1';
  draft.allocations[0].percent = 101;
  assert.match(core.validate(draft), /%100/);
});

test('capacity core keeps scenario and persisted allocation models separate', () => {
  const draft = validPlan();
  const scenario = draft.allocations.map(core.hydrateAllocation);
  scenario.push({
    id: null,
    userId: 'user-1',
    projectId: 'project-2',
    startDate: new Date(2026, 6, 6),
    endDate: new Date(2026, 6, 19),
    percent: 50
  });
  assert.equal(draft.allocations.length, 1);
  assert.equal(core.validate(draft, scenario), null);
  assert.equal(core.scenarioDelta({
    baseline: { summary: { allocatedHours: 48 } },
    candidate: { summary: { allocatedHours: 88 } }
  }, 'allocatedHours'), 40);
  assert.equal(core.stateLabel('OverCapacity'), 'Kapasite üstü');
});

test('desktop and mobile expose complete capacity workflows and states', () => {
  for (const source of [desktopSource, mobileSource]) {
    assert.match(source, /\/api\/capacity-plans/);
    assert.match(source, /\/snapshot/);
    assert.match(source, /\/scenarios/);
    assert.match(source, /\/sharing/);
    assert.match(source, /apiClient\.delete\('\/api\/capacity-plans\//);
    assert.match(source, /canEdit/);
    assert.match(source, /BoardManage/);
    assert.doesNotMatch(source, /\['ProjectOwner', 'ProjectAdmin'\]/);
    assert.match(source, /teamMember\.status === 'Active'/);
  }
  assert.match(desktopHtml, /vm\.activeSection === 'capacity'/);
  assert.match(desktopHtml, /Kapasite ve tahsis saat cinsindedir/);
  assert.match(desktopHtml, /sourceStatus==='Partial'/);
  assert.match(desktopHtml, /caption class="sr-only">Proje kapasite tahsisleri/);
  assert.match(desktopCss, /grid-template-columns:/);
  assert.match(mobileHtml, /templates\/capacity\.html/);
  assert.match(mobileHtml, /ui-sref="capacity-center"/);
  assert.match(mobileHtml, /shell\.pwa\.offline/);
  assert.match(desktopHtml, /ng-if="vm\.canCreateCapacityPlan\(\)"/);
  assert.match(mobileHtml, /ng-if="vm\.canCreatePlan\(\)"/);
  assert.match(mobileCss, /min-height:\s*44px/);
  assert.match(apiClientSource, /goals\|capacity-plans/);
});
