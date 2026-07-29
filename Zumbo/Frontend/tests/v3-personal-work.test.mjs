import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import vmModule from 'node:vm';

const root = resolve(import.meta.dirname, '..');
const source = await readFile(resolve(root, 'desktop-bulma/personal-work.js'), 'utf8');
const appSource = await readFile(resolve(root, 'desktop-bulma/app.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const workspaceSource = await readFile(resolve(root, 'mobile-ionic/workspace.js'), 'utf8');

function createFeature(responses) {
  let provider;
  const requests = [];
  const storage = new Map();
  const module = {
    factory(name, factory) {
      assert.equal(name, 'desktopPersonalWorkFeature');
      provider = factory;
      return module;
    }
  };
  const angular = { module: () => module, extend: (...values) => Object.assign(...values) };
  vmModule.runInNewContext(source, {
    angular,
    window: {
      localStorage: {
        getItem: key => storage.get(key) || null,
        setItem: (key, value) => storage.set(key, value)
      }
    }
  });
  const apiClient = {
    post: (_, request) => {
      requests.push(request);
      const response = responses[request.projectId];
      const page = Array.isArray(response) ? response[request.page - 1] : response;
      return page instanceof Error ? Promise.reject(page) : Promise.resolve(page);
    }
  };
  return { feature: provider({ all: promises => Promise.all(promises), when: value => Promise.resolve(value) }, apiClient), requests, storage };
}

function viewModel() {
  return {
    session: { currentUser: { id: 'user-1' } },
    projects: [{ id: 'project-1', name: 'Platform' }, { id: 'project-2', name: 'Mobil' }],
    notifications: [
      { id: 'n1', type: 'Mention', message: 'Bir yorumda bahsedildiniz', read: false },
      { id: 'n2', type: 'Assignment', message: 'Görev atandı', read: true },
      { id: 'n3', type: 'Assignment', message: 'Onay kapsamı size atandı', read: false }
    ]
  };
}

test('personal work aggregates accessible projects and derives due, blocked, recent and approval queues', async () => {
  const now = Date.now();
  const responses = {
    'project-1': { items: [
      { id: 'due', projectId: 'project-1', title: 'Tarihli', status: 'In Progress', dueDate: new Date(now + 86400000).toISOString(), approvals: [] },
      { id: 'blocked', projectId: 'project-1', title: 'Engelli', status: 'Blocked', approvals: [] }
    ], totalCount: 2 },
    'project-2': { items: [
      { id: 'approval', projectId: 'project-2', title: 'Onay', status: 'Review', statusHistory: [{ changedAt: new Date(now).toISOString() }], approvals: [{ status: 'Pending' }] }
    ], totalCount: 1 }
  };
  const { feature } = createFeature(responses);
  const vm = viewModel();
  feature.install(vm, { membershipFor: () => ({ role: 'Developer' }) });
  await vm.loadPersonalWork();
  assert.deepEqual(Array.from(vm.personalTasks, task => task.projectName).sort(), ['Mobil', 'Platform', 'Platform']);
  assert.deepEqual(Array.from(vm.personalDue(), task => task.id), ['due']);
  assert.deepEqual(Array.from(vm.personalBlocked(), task => task.id), ['blocked']);
  assert.deepEqual(Array.from(vm.pendingApprovals(), task => task.id), ['approval']);
  assert.deepEqual(Array.from(vm.inboxNotifications(), item => item.id), ['n1', 'n3']);
  vm.setInboxMode('actions');
  assert.deepEqual(Array.from(vm.inboxNotifications(), item => item.id), ['n1']);
  assert.equal(vm.personalPartial, false);
});

test('personal work exposes partial freshness and persists bounded saved views', async () => {
  const { feature, storage } = createFeature({
    'project-1': { items: [], totalCount: 0 },
    'project-2': new Error('unavailable')
  });
  const vm = viewModel();
  feature.install(vm, { membershipFor: () => ({ role: 'Viewer' }) });
  await vm.loadPersonalWork();
  assert.equal(vm.personalPartial, true);
  assert.equal(Number.isNaN(new Date(vm.personalFreshAt).getTime()), false);
  vm.personalMode = 'blocked';
  vm.personalViewDraft = 'Takip';
  vm.savePersonalView();
  assert.equal(vm.savedPersonalViews[0].mode, 'blocked');
  assert.match(storage.get('zumbo.personalViews'), /Takip/);
});

test('personal work requests bounded pages, appends unique results and reports total failure', async () => {
  const { feature, requests } = createFeature({
    'project-1': [
      { items: [{ id: 'first', projectId: 'project-1', status: 'To Do', approvals: [] }], totalCount: 51 },
      { items: [
        { id: 'first', projectId: 'project-1', status: 'To Do', approvals: [] },
        { id: 'second', projectId: 'project-1', status: 'To Do', approvals: [] }
      ], totalCount: 51 }
    ],
    'project-2': new Error('unavailable')
  });
  const vm = viewModel();
  feature.install(vm, { membershipFor: project => project.id === 'project-1' ? { role: 'Developer' } : null });
  await vm.loadPersonalWork();
  assert.equal(vm.personalHasMore, true);
  await vm.loadMorePersonalWork();
  assert.deepEqual(Array.from(vm.personalTasks, task => task.id), ['first', 'second']);
  assert.deepEqual(requests.map(request => ({ page: request.page, pageSize: request.pageSize })), [
    { page: 1, pageSize: 50 },
    { page: 2, pageSize: 50 }
  ]);

  const failed = createFeature({
    'project-1': new Error('offline'),
    'project-2': new Error('offline')
  });
  const failedVm = viewModel();
  failed.feature.install(failedVm, { membershipFor: () => ({ role: 'Developer' }) });
  await failedVm.loadPersonalWork();
  assert.equal(failedVm.personalError, 'Kişisel iş görünümü yüklenemedi.');
  assert.equal(failedVm.personalPartial, true);
});

test('desktop and mobile templates expose personal navigation, triage filters and empty states', () => {
  for (const section of ['home', 'mywork', 'inbox']) {
    assert.match(desktopHtml, new RegExp(`vm\\.showSection\\('${section}'\\)`));
    assert.match(desktopHtml, new RegExp(`vm\\.activeSection === '${section}'`));
  }
  assert.match(desktopHtml, /vm\.savePersonalView\(\)/);
  assert.match(desktopHtml, /vm\.pendingApprovals\(\)/);
  assert.match(desktopHtml, /Bazı projeler yenilenemedi/);
  assert.match(desktopHtml, /class="board-skeleton" ng-if="vm\.activeSection === 'board' && \['overview','catalog','intake','automation','jobs'\]\.indexOf\(vm\.workMode\) < 0 && vm\.loading"/);
  assert.match(mobileHtml, /task in vm\.visibleTaskItems track by task\.id/);
  assert.match(workspaceSource, /function rebuildVisibleTasks\(\)/);
  assert.match(mobileHtml, /vm\.visibleNotifications\(\)/);
  assert.match(mobileHtml, /Bu filtrede bildirim yok\./);
  assert.match(appSource, /\.then\(function\(\) \{\s*loadSectionData\(vm\.activeSection\);\s*return \$q\.all\(\[vm\.loadNotifications\(\), vm\.loadTeams\(\), vm\.loadUsers\(\), vm\.loadAuditCapabilities\(\)\]\);/);
});
