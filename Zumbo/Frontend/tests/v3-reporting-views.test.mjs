import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/reporting-core.js'));
const desktop = await readFile(resolve(root, 'desktop-bulma/reporting-views.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobile = await readFile(resolve(root, 'mobile-ionic/reporting-views.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const backend = await readFile(resolve(root, '../Backend/src/Zumbo.Modules.WorkItems/Application/Features/Reports/StatusDistribution/StatusDistributionPipeline.cs'), 'utf8');

test('report response metadata preserves freshness, source version and stale state', () => {
  const values = {
    'X-Zumbo-Report-Generated-At': '2026-07-23T02:00:00Z',
    'X-Zumbo-Report-Source-Version': '42',
    'X-Zumbo-Report-Stale': 'true',
    'X-Zumbo-Report-Age-Seconds': '12.5'
  };
  const result = core.snapshot({ data: { data: [{ status: 'Open', count: 2 }] }, headers: name => values[name] });
  assert.deepEqual(result, {
    data: [{ status: 'Open', count: 2 }], generatedAt: '2026-07-23T02:00:00Z',
    sourceVersion: 42, stale: true, ageSeconds: 12.5
  });
});

test('workload uses complete large scope without ranking or invented capacity', () => {
  const tasks = Array.from({ length: 1205 }, (_, index) => ({
    id: `task-${index}`, status: 'Open', assigneeUserId: index % 2 ? 'user-b' : 'user-a',
    estimatePoints: index % 5 ? 2 : null, archived: false
  }));
  const model = core.workloadModel({
    workload: [
      { userId: 'user-b', openItems: 602, overdueItems: 3, loggedHours: 18 },
      { userId: 'user-a', openItems: 603, overdueItems: 0, loggedHours: 21 }
    ],
    tasks, scopeComplete: true, userName: id => id
  });
  assert.equal(model.capacityConfigured, false);
  assert.equal(model.scopeComplete, true);
  assert.equal(model.rows[0].id, 'user-b', 'server/member order must not become a productivity ranking');
  assert.equal(model.totals.openItems, 1205);
  assert.equal(model.rows.reduce((total, row) => total + row.tasks.length, 0), 1205);
  assert.ok(model.totals.unestimatedItems > 0);
});

test('report model keeps explicit calculations and alphabetical team comparison', () => {
  const model = core.reportingModel({
    status: [{ status: 'Open', count: 3 }, { status: 'Done', count: 1 }],
    completion: { createdItems: 4, completedItems: 1, completionRatePercent: 25 },
    flow: { completedItems: 1, cycleTimeSampleSize: 1, medianLeadTimeHours: 48, medianCycleTimeHours: 24 },
    teams: [{ teamId: '2', teamName: 'Zeta' }, { teamId: '1', teamName: 'Alfa' }],
    rangeDays: 30
  });
  assert.deepEqual(model.status.map(row => row.percent), [75, 25]);
  assert.equal(model.completion.completionRatePercent, 25);
  assert.deepEqual(model.teams.map(team => team.teamName), ['Alfa', 'Zeta']);
});

test('desktop and mobile expose freshness, drill-down, tables and permission-scoped APIs', () => {
  assert.match(desktop, /rawResponse: true/);
  assert.match(desktop, /loadCompleteTaskScope/);
  assert.match(desktopHtml, /class="workload-table"/);
  assert.match(desktopHtml, /class="reporting-table"/);
  assert.match(desktopHtml, /Kapasite eşiği yapılandırılmadı/);
  assert.match(desktopHtml, /kişi performansı sıralaması değildir/);
  assert.match(mobile, /controller\('ProjectReportingController'/);
  assert.match(mobile, /result\.totalCount/);
  assert.match(mobile, /return vm\.project && vm\.project\.id \|\| \$stateParams\.projectId \|\| null/);
  assert.doesNotMatch(mobile, /projectId: vm\.project\.id/);
  assert.match(mobileHtml, /templates\/project-reporting\.html/);
  assert.match(mobileHtml, /aria-label="Mobil içgörü görünümü"/);
  assert.match(backend, /EnsurePermissionAsync\(query\.ProjectId, PermissionCatalog\.WorkItemView, ct\)/);
});
