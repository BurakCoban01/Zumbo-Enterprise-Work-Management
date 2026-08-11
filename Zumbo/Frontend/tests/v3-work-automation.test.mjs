import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import vmModule from 'node:vm';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/work-automation-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/work-automation.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/work-automation.css'), 'utf8');
const overviewSource = await readFile(resolve(root, 'desktop-bulma/project-overview.js'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');
const mobileApp = await readFile(resolve(root, 'mobile-ionic/app.js'), 'utf8');
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/work-automation.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/work-automation.css'), 'utf8');
const backend = (await Promise.all([
  '../Backend/src/Zumbo.Modules.WorkItems/Application/Compatibility/Recurrences/WorkItemTemplateRecurrenceService/RecurrenceFacade.cs',
  '../Backend/src/Zumbo.Modules.WorkItems/Application/Features/Recurrences/RecurrenceSchedulePolicy.cs',
  '../Backend/src/Zumbo.Modules.WorkItems/Application/Features/Recurrences/PreviewWorkItemRecurrenceSlice.cs'
].map(path => readFile(resolve(root, path), 'utf8')))).join('\n');
const endpoints = await readFile(
  resolve(root, '../Backend/src/Zumbo.Api/Presentation/Endpoints/WorkItems/Recurrences/PreviewRecurrenceEndpoint.cs'),
  'utf8'
);

function project(role = 'ProjectOwner') {
  return {
    id: 'project-1',
    key: 'AUT',
    members: [{ userId: 'user-1', role }]
  };
}

function template(overrides = {}) {
  return {
    id: 'template-1',
    projectId: 'project-1',
    boardId: 'board-1',
    name: 'Weekly review',
    title: 'Review delivery risks',
    description: 'Inspect the current queue.',
    type: 'Task',
    priority: 'Medium',
    assigneeUserId: null,
    teamId: null,
    dueAfterDays: 1,
    labels: ['operations'],
    customFields: [],
    archived: false,
    version: 3,
    ...overrides
  };
}

function recurrence(overrides = {}) {
  return {
    id: 'recurrence-1',
    projectId: 'project-1',
    templateId: 'template-1',
    frequency: 'Weekly',
    interval: 1,
    startAtUtc: '2026-08-01T07:00:00Z',
    endAtUtc: null,
    nextRunAtUtc: '2099-08-08T07:00:00Z',
    maxOccurrences: 12,
    scheduledOccurrences: 2,
    generatedOccurrences: 2,
    active: true,
    archived: false,
    version: 4,
    ...overrides
  };
}

test('shared automation core preserves template fields, limits, roles and lifecycle states', () => {
  const roles = [
    { name: 'ProjectOwner', permissions: ['BoardManage'] },
    { name: 'ProjectAdmin', permissions: ['BoardManage'] },
    { name: 'Developer', permissions: ['WorkItemUpdate'] },
    { name: 'Viewer', permissions: ['WorkItemView'] }
  ];
  assert.equal(core.canEdit('ProjectOwner', {}, roles, []), true);
  assert.equal(core.canEdit('ProjectAdmin', {}, roles, []), true);
  assert.equal(core.canEdit('Developer', {}, roles, []), false);
  assert.equal(core.canEdit('Viewer', {}, roles, []), false);
  assert.equal(core.roleOf(project('Viewer'), 'user-1'), 'Viewer');

  const labels = core.normalizeLabels('Ops, API\nops');
  assert.deepEqual(labels.values, ['Ops', 'API']);
  assert.equal(core.normalizeLabels(Array.from({ length: 51 }, (_, index) => `L${index}`).join(',')).tooMany, true);
  assert.equal(core.limits.labelCount, 50);
  assert.equal(core.limits.recurrenceOccurrences, 1000);

  const draft = core.templateDraft(template({
    customFields: [{ fieldKey: 'region', textValue: 'emea', type: 'Text' }]
  }));
  assert.equal(draft.customFields[0].textValue, 'emea');
  assert.equal(draft.customFields[0].type, undefined);
  assert.match(core.toUtcIso('2026-08-01T10:00'), /Z$/);
  assert.equal(core.recurrenceState(recurrence()).id, 'active');
  assert.equal(core.recurrenceState(recurrence({ active: false })).id, 'paused');
  assert.equal(core.recurrenceState(recurrence({ nextRunAtUtc: null })).id, 'completed');
  assert.equal(core.occurrenceState({ status: 'Generated' }).id, 'generated');
  assert.equal(core.occurrenceState({ status: 'Failed' }).id, 'failed');
});

function desktopFeature(api) {
  let provider;
  const module = {
    factory(name, factory) {
      assert.equal(name, 'desktopWorkAutomationFeature');
      provider = factory;
      return module;
    }
  };
  const angular = {
    module: () => module,
    extend: (...values) => Object.assign({}, ...values),
    noop() {}
  };
  const window = { ZumboWorkAutomationCore: core };
  vmModule.runInNewContext(desktopSource, { angular, window, Date, Object, Array, String, Number });
  return provider({
    all: values => Promise.all(values),
    when: value => Promise.resolve(value)
  }, api);
}

test('desktop automation uses authoritative pages, previews and versioned lifecycle mutations', async () => {
  const calls = [];
  const currentTemplate = template();
  const currentRecurrence = recurrence();
  const api = {
    remember(url, value) { calls.push(['remember', url, value.version]); },
    get(url) {
      calls.push(['get', url]);
      if (url.includes('/templates?')) return Promise.resolve({ items: [currentTemplate] });
      if (url.includes('/recurrences?')) return Promise.resolve({ items: [currentRecurrence] });
      if (url.includes('/api/automations/runs?')) return Promise.resolve({ items: [], total: 0 });
      if (url.includes('/api/automations?')) return Promise.resolve({ items: [], total: 0 });
      if (url.includes('/occurrences?')) return Promise.resolve({ items: [{ id: 'occ-1', status: 'Generated', scheduledForUtc: '2026-08-01T07:00:00Z', createdWorkItemId: 'work-1' }] });
      if (url.includes('/api/audit/entity/')) return Promise.resolve([{ id: 'audit-1', action: 'WorkItemRecurrenceCreated', createdAt: '2026-08-01T07:00:00Z' }]);
      throw new Error(`Unexpected GET ${url}`);
    },
    post(url, body) {
      calls.push(['post', url, body]);
      if (url.endsWith('/preview')) return Promise.resolve({ frequency: body.frequency, interval: body.interval, occurrencesUtc: ['2026-08-01T07:00:00Z'] });
      return Promise.resolve(currentTemplate);
    },
    put(url, body) { calls.push(['put', url, body]); return Promise.resolve(currentTemplate); },
    patch(url, body) { calls.push(['patch', url, body]); return Promise.resolve(recurrence({ active: body.active, version: 5 })); },
    delete(url) { calls.push(['delete', url]); return Promise.resolve(); }
  };
  const feature = desktopFeature(api);
  const vm = {
    project: project(),
    projectMembership: { role: 'ProjectOwner' },
    session: { currentUser: { id: 'user-1', roles: [] } },
    projectRoles: [{ name: 'ProjectOwner', permissions: ['BoardManage'] }],
    roles: [],
    boards: [{ id: 'board-1', name: 'Delivery' }],
    activeIssueTypes: () => [{ key: 'Task', name: 'Task', active: true }],
    userName: id => id,
    notify(kind, message) { vm.feedback = { kind, message }; }
  };
  feature.install(vm);
  await vm.loadWorkAutomation();
  assert.equal(vm.workAutomation.templates.length, 1);
  assert.equal(vm.workAutomation.recurrences.length, 1);
  assert.equal(vm.workAutomation.occurrences[0].status, 'Generated');
  assert.ok(calls.some(call => call[0] === 'remember' && call[1] === '/api/work-items/templates/template-1'));
  assert.ok(calls.some(call => call[0] === 'remember' && call[1] === '/api/work-items/recurrences/recurrence-1'));

  await vm.previewWorkRecurrence();
  assert.equal(vm.recurrencePreview.occurrencesUtc.length, 1);
  assert.ok(calls.some(call => call[0] === 'post' && call[1] === '/api/work-items/recurrences/preview'));

  await vm.setWorkRecurrenceState(currentRecurrence, false);
  assert.ok(calls.some(call => call[0] === 'patch' && call[1].endsWith('/recurrence-1/state') && call[2].active === false));

  vm.workTemplateDraft = core.templateDraft(null, 'board-1');
  Object.assign(vm.workTemplateDraft, { name: 'Triage', title: 'Review triage', labelsText: 'ops' });
  await vm.saveWorkTemplate();
  assert.ok(calls.some(call => call[0] === 'post' && call[1] === '/api/work-items/templates' && call[2].projectId === 'project-1'));
});

test('backend preview reuses authoritative validation and cadence without mutation', () => {
  assert.match(backend, /PreviewWorkItemRecurrenceRequest/);
  assert.match(backend, /PreviewRecurrenceAsync/);
  assert.match(backend, /schedulePolicy\.Validate\(new CreateWorkItemRecurrenceRequest/);
  assert.match(backend, /next = RecurrenceSchedulePolicy\.Next\(next, schedule\.Frequency, schedule\.Interval\)/);
  assert.match(backend, /Math\.Clamp\(request\.PreviewCount, 1, 10\)/);
  assert.match(endpoints, /MapPost\("\/recurrences\/preview"/);
  assert.match(endpoints, /PermissionCatalog\.WorkItemCreate/);
});

test('desktop and mobile expose normal navigation, timezone, preview, failure, audit and read-only states', () => {
  assert.match(overviewSource, /view\('automation'/);
  assert.match(desktopHtml, /vm\.workMode === 'automation'/);
  assert.match(desktopHtml, /aria-label="Proje otomasyon alanları"/);
  assert.match(desktopHtml, /vm\.previewWorkRecurrence/);
  assert.match(desktopHtml, /vm\.setWorkRecurrenceState/);
  assert.match(desktopHtml, /vm\.automationOccurrenceState/);
  assert.match(desktopHtml, /vm\.workAutomation\.audit/);
  assert.match(desktopCss, /\.automation-layout\s*\{[\s\S]*grid-template-columns:/);
  assert.match(apiClientSource, /kind: 'work-item-templates'/);
  assert.match(apiClientSource, /kind: 'work-item-recurrences'/);

  assert.match(mobileApp, /state\('project-automation'/);
  assert.match(mobileApi, /previewWorkRecurrence/);
  assert.match(mobileApi, /workRecurrenceOccurrences/);
  assert.match(mobileSource, /controller\('WorkAutomationController'/);
  assert.match(mobileHtml, /templates\/work-automation\.html/);
  assert.match(mobileHtml, /aria-label="Mobil otomasyon alanları"/);
  assert.match(mobileHtml, /vm\.occurrenceState/);
  assert.match(mobileHtml, /vm\.model\.audit/);
  assert.match(mobileCss, /\.mobile-automation-tabs button\s*\{[^}]*flex: 1 1 0;[^}]*min-width: 0;/);
});
