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
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/work-automation.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/work-automation.css'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');

function ruleSummary(overrides = {}) {
  return {
    id: 'rule-1',
    projectId: 'project-1',
    name: 'Triage labels',
    triggerType: 'Event',
    eventType: 'WorkItemCreated',
    active: true,
    archived: false,
    nextRunAtUtc: null,
    publishedVersion: 1,
    hasDraft: true,
    version: 4,
    ...overrides
  };
}

function ruleDetail(overrides = {}) {
  return {
    id: 'rule-1',
    projectId: 'project-1',
    active: true,
    archived: false,
    nextRunAtUtc: null,
    publishedVersion: 1,
    hasDraft: true,
    version: 4,
    definition: {
      number: 2,
      state: 'Draft',
      name: 'Triage labels',
      description: 'Keep intake work visible.',
      trigger: {
        type: 'Event',
        eventType: 'WorkItemCreated',
        intervalMinutes: null,
        startAtUtc: null
      },
      condition: {
        kind: 'Field',
        field: 'Priority',
        operator: 'Equals',
        value: 'High',
        children: []
      },
      actions: [{ type: 'AddLabel', value: 'triage' }],
      maximumExecutionsPerHour: 100,
      maximumChainDepth: 3
    },
    ...overrides
  };
}

function deadLetterRun() {
  return {
    id: 'run-1',
    projectId: 'project-1',
    ruleId: 'rule-1',
    ruleVersion: 1,
    ruleName: 'Triage labels',
    triggerType: 'Event',
    eventType: 'WorkItemCreated',
    sourceId: 'work-1',
    actorUserId: 'user-1',
    rootRunId: 'run-1',
    chainDepth: 0,
    status: 'DeadLetter',
    outcome: 'DeadLetter',
    attempt: 3,
    maximumAttempts: 3,
    failureCategory: 'Conflict',
    createdAtUtc: '2026-07-28T10:00:00Z',
    nextAttemptAtUtc: null,
    steps: [{
      index: 0,
      actionType: 'AddLabel',
      status: 'Failed',
      attempt: 3,
      failureCategory: 'Conflict'
    }],
    version: 5
  };
}

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
  vmModule.runInNewContext(desktopSource, {
    angular,
    window,
    Date,
    Object,
    Array,
    String,
    Number
  });
  return provider({
    all: values => Promise.all(values),
    when: value => Promise.resolve(value),
    reject: error => Promise.reject(error)
  }, api);
}

test('automation core builds bounded typed event and schedule rule requests', () => {
  const draft = core.newRuleDraft();
  Object.assign(draft, {
    name: 'Escalate high priority work',
    triggerType: 'Schedule',
    intervalMinutes: 30,
    startAtLocal: new Date('2026-07-28T12:00:00Z'),
    conditionMode: 'Any',
    conditions: [
      { field: 'Priority', operator: 'Equals', value: 'High' },
      { field: 'Labels', operator: 'Contains', value: 'urgent' }
    ],
    actions: [
      { type: 'AddLabel', value: 'escalated' },
      { type: 'AssignToActor', value: '' }
    ]
  });

  assert.equal(core.validRule(draft), true);
  const request = core.ruleRequest('project-1', draft);
  assert.equal(request.trigger.type, 'Schedule');
  assert.equal(request.trigger.intervalMinutes, 30);
  assert.match(request.trigger.startAtUtc, /Z$/);
  assert.equal(request.condition.kind, 'Any');
  assert.equal(request.condition.children.length, 2);
  assert.equal(request.actions[0].value, 'escalated');
  assert.equal(request.actions[1].value, null);

  draft.intervalMinutes = 4;
  assert.equal(core.validRule(draft), false);
  const projectRoles = [{ name: 'Developer', permissions: ['WorkItemUpdate'] }];
  const systemRoles = [{ name: 'SystemAdmin', permissions: ['*'] }];
  assert.equal(core.canEdit('Developer', { roles: [] }, projectRoles, systemRoles), false);
  assert.equal(core.canEdit('Viewer', { roles: ['SystemAdmin'] }, projectRoles, systemRoles), true);
});

test('desktop rule workflow loads details, saves a draft, dry-runs and replays dead letters', async () => {
  const calls = [];
  const summary = ruleSummary();
  let detail = ruleDetail();
  const run = deadLetterRun();
  const api = {
    remember(url, value) { calls.push(['remember', url, value && value.version]); },
    get(url) {
      calls.push(['get', url]);
      if (url.includes('/work-items/templates?')) return Promise.resolve({ items: [] });
      if (url.includes('/work-items/recurrences?')) return Promise.resolve({ items: [] });
      if (url.includes('/automations/runs?')) return Promise.resolve({ items: [run], total: 1 });
      if (url.includes('/automations?')) return Promise.resolve({ items: [summary], total: 1 });
      if (url === '/api/automations/rule-1?draft=true') return Promise.resolve(detail);
      throw new Error(`Unexpected GET ${url}`);
    },
    post(url, body) {
      calls.push(['post', url, body]);
      if (url.endsWith('/dry-run')) {
        return Promise.resolve({
          ruleId: 'rule-1',
          ruleVersion: 2,
          triggerMatched: true,
          conditionMatched: true,
          plannedActions: [{ type: 'AddLabel', value: 'triage' }],
          outcome: 'WouldExecute'
        });
      }
      if (url.endsWith('/replay')) {
        run.status = 'RetryScheduled';
        return Promise.resolve(run);
      }
      return Promise.resolve(detail);
    },
    put(url, body) {
      calls.push(['put', url, body]);
      detail = ruleDetail({
        version: 5,
        definition: { ...detail.definition, name: body.name }
      });
      summary.name = body.name;
      summary.version = 5;
      return Promise.resolve(detail);
    },
    patch(url, body) { calls.push(['patch', url, body]); return Promise.resolve(detail); },
    delete(url) { calls.push(['delete', url]); return Promise.resolve(); }
  };
  const feature = desktopFeature(api);
  const vm = {
    project: {
      id: 'project-1',
      key: 'AUT',
      members: [{ userId: 'user-1', role: 'ProjectOwner' }]
    },
    projectMembership: { role: 'ProjectOwner' },
    session: { currentUser: { id: 'user-1', roles: [] } },
    projectRoles: [{ name: 'ProjectOwner', permissions: ['BoardManage'] }],
    roles: [],
    boards: [],
    activeIssueTypes: () => [{ key: 'Task', name: 'Task', active: true }],
    notify(kind, message) { vm.feedback = { kind, message }; }
  };

  feature.install(vm);
  await vm.loadWorkAutomation();
  assert.equal(vm.automationRuleDraft.name, 'Triage labels');
  assert.equal(vm.workAutomation.runs[0].status, 'DeadLetter');

  vm.automationRuleDraft.name = 'Triage labels v2';
  await vm.saveAutomationRule();
  assert.ok(calls.some(call => call[0] === 'put'
    && call[1] === '/api/automations/rule-1/draft'
    && call[2].name === 'Triage labels v2'));

  await vm.runAutomationDryRun();
  assert.equal(vm.automationDryRunResult.outcome, 'WouldExecute');
  assert.ok(calls.some(call => call[0] === 'post'
    && call[1] === '/api/automations/rule-1/dry-run'));

  run.status = 'DeadLetter';
  await vm.replayAutomationRun(run);
  assert.ok(calls.some(call => call[0] === 'post'
    && call[1] === '/api/automations/runs/run-1/replay'));
  assert.equal(vm.workAutomation.runs[0].status, 'RetryScheduled');
});

test('desktop and mobile expose rule, run, offline, audit-error and responsive states', () => {
  assert.match(desktopHtml, /vm\.workAutomationTab === 'rules'/);
  assert.match(desktopHtml, /vm\.runAutomationDryRun\(\)/);
  assert.match(desktopHtml, /vm\.replayAutomationRun/);
  assert.match(desktopHtml, /vm\.workAutomationAuditError/);
  assert.match(desktopHtml, /vm\.pwa\.offline/);
  assert.match(desktopCss, /\.automation-run-layout\s*\{[\s\S]*grid-template-columns:/);
  assert.match(desktopCss, /\.automation-rule-line\s*\{[\s\S]*grid-template-columns:/);

  assert.match(mobileApi, /automationRules:/);
  assert.match(mobileApi, /replayAutomationRun:/);
  assert.match(mobileSource, /var tabs = \['rules', 'runs', 'schedules', 'templates', 'activity'\]/);
  assert.match(mobileHtml, /vm\.tab==='rules'/);
  assert.match(mobileHtml, /vm\.tab==='runs'/);
  assert.match(mobileHtml, /shell\.pwa\.offline/);
  assert.match(mobileHtml, /vm\.auditError/);
  assert.match(mobileCss, /\.mobile-automation-tabs button\s*\{[^}]*flex: 1 1 0;[^}]*min-width: 0;/);
  assert.match(mobileCss, /@media \(max-width: 340px\)/);

  assert.match(apiClientSource, /kind: 'automation-runs'/);
  assert.match(apiClientSource, /kind: 'automations'/);
});
