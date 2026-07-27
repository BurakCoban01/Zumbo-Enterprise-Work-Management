import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import { clearTimeout as nodeClearTimeout } from 'node:timers';
import { URLSearchParams } from 'node:url';
import vmModule from 'node:vm';

const root = resolve(import.meta.dirname, '..');
const detailSource = await readFile(resolve(root, 'desktop-bulma/work-items.js'), 'utf8');
const appSource = await readFile(resolve(root, 'desktop-bulma/app.js'), 'utf8');
const apiClientSource = await readFile(resolve(root, 'shared/api-client.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/styles.css'), 'utf8');
const mobileApiSource = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileDetailSource = await readFile(resolve(root, 'mobile-ionic/details.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');

const angular = {
  copy: value => JSON.parse(JSON.stringify(value)),
  extend: (...values) => Object.assign(...values)
};
const q = {
  all: values => Promise.all(values),
  when: value => Promise.resolve(value),
  reject: value => Promise.reject(value)
};

function loadFactory(source, name, dependencies) {
  let provider;
  const module = {
    factory(actualName, factory) {
      assert.equal(actualName, name);
      provider = factory;
      return module;
    }
  };
  vmModule.runInNewContext(source, {
    angular: { ...angular, module: () => module },
    Date, URLSearchParams, encodeURIComponent, setTimeout, clearTimeout: nodeClearTimeout
  });
  return provider(...dependencies);
}

function task() {
  return {
    id: 'work-1', projectId: 'project-1', boardId: 'board-1', title: 'Güvenli detay',
    description: '<img src=x onerror=alert(1)>\nKabul ölçütü', type: 'Task', priority: 'High',
    status: 'To Do', labels: [], checklist: [], relations: [], customFields: [], version: 7
  };
}

function page(items, totalCount = items.length, pageNumber = 1) {
  return { items, page: pageNumber, pageSize: 2, totalCount };
}

function model({ role = 'Developer', put } = {}) {
  const current = task();
  const calls = [];
  const api = {
    get(path) {
      calls.push(['get', path]);
      if (path === '/api/work-items/work-1') return Promise.resolve({ ...current });
      if (path.endsWith('/collaboration')) return Promise.resolve({ workItemId: 'work-1', watcherCount: 2, voteCount: 1, watching: false, voted: true, version: 3 });
      if (path.includes('/activity')) return Promise.resolve(page([{ id: 'event-1', type: 'WorkItemUpdated', actorUserId: 'user-2', detail: 'Updated', createdAt: '2026-07-23T00:00:00Z' }], 3));
      if (path.includes('/comments')) return Promise.resolve(page([{ id: 'comment-1', body: 'İnceleme', authorUserId: 'user-2', mentions: [] }], 1));
      if (path.includes('/attachments')) return Promise.resolve(page([], 0));
      if (path.includes('/worklogs')) return Promise.resolve(page([], 0));
      if (path.includes('/approvals')) return Promise.resolve(page([], 0));
      if (path.includes('/timeline')) return Promise.resolve(page([], 0));
      if (path.includes('/audit/')) return Promise.resolve([]);
      return Promise.resolve([]);
    },
    put: put || ((path, body) => {
      calls.push(['put', path, body]);
      return Promise.resolve(path.endsWith('/watch')
        ? { workItemId: 'work-1', watcherCount: 3, voteCount: 1, watching: true, voted: true, version: 4 }
        : { ...current, ...body, version: 8 });
    }),
    post(path, body) { calls.push(['post', path, body]); return Promise.resolve({ ...current, version: 8 }); },
    patch(path, body) { calls.push(['patch', path, body]); return Promise.resolve({ ...current, version: 8 }); },
    delete(path) { calls.push(['delete', path]); return Promise.resolve({ ...current, version: 8 }); },
    upload(path) { calls.push(['upload', path]); return Promise.resolve({ ...current, version: 8 }); },
    download() { return Promise.resolve({}); }
  };
  const vm = {
    session: { currentUser: { id: 'user-1', roles: ['User'] } },
    project: {
      id: 'project-1', members: [{ userId: 'user-1', role }], components: [], versions: [], releases: [], milestones: []
    },
    projectMembership: { role }, tasks: [current], users: [{ id: 'user-1', username: 'Ada' }],
    teams: [], sprints: [], workItemSchema: { issueTypes: [], customFields: [], layouts: [] },
    workflow: { transitions: [] }, activeSection: 'board',
    userName: id => id === 'user-1' ? 'Ada' : 'Mert',
    projectTeams: () => [], customFieldsFor: () => [], customFieldRequests: () => [],
    loadTasks: () => Promise.resolve(), notify(kind, message) { vm.notice = { kind, message }; }
  };
  const feature = loadFactory(detailSource, 'desktopWorkItemFeature', [
    q,
    { document: { querySelectorAll: () => [] }, URL: { createObjectURL() {}, revokeObjectURL() {} } },
    callback => callback(),
    api
  ]);
  feature.install(vm, {
    updateLocation() {}, nextStatusFor: () => 'In Progress',
    apiActionError: (error, fallback) => error?.data?.error?.message || fallback
  });
  return { vm, api, calls, current };
}

test('detail composes collaboration and bounded activity streams without silent truncation', async () => {
  const { vm, calls } = model();
  await vm.selectTask(task());
  assert.equal(vm.taskDetail.loading, false);
  assert.equal(vm.taskDetail.collaboration.watcherCount, 2);
  assert.equal(vm.taskStreams.activity.items.length, 1);
  assert.equal(vm.taskStreams.activity.totalCount, 3);
  assert.equal(vm.taskStreamHasMore('activity'), true);
  assert.ok(calls.some(call => call[1] === '/api/work-items/work-1/comments?page=1&pageSize=50'));
});

test('watch is optimistic and restores collaboration snapshot on failure', async () => {
  const conflict = { data: { error: { code: 'WORK_ITEM_COLLABORATION_CONFLICT', message: 'Retry.' } } };
  const { vm } = model({ put: () => Promise.reject(conflict) });
  await vm.selectTask(task());
  const pending = vm.toggleTaskWatch();
  assert.equal(vm.taskDetail.collaboration.watching, true);
  assert.equal(vm.taskDetail.collaboration.watcherCount, 3);
  await pending;
  assert.equal(vm.taskDetail.collaboration.watching, false);
  assert.equal(vm.taskDetail.collaboration.watcherCount, 2);
  assert.match(vm.taskDetail.actionError, /Retry/);
});

test('permission model keeps Viewer comment/watch/vote access but removes edit and storage mutations', () => {
  const { vm } = model({ role: 'Viewer' });
  assert.equal(vm.canEditTaskDetail(), false);
  assert.equal(vm.canCommentOnTask(), true);
  assert.equal(vm.canUploadTaskAttachment(), false);
  assert.equal(vm.canLinkTask(), false);
  assert.equal(vm.canApproveTask(), false);
  assert.equal(vm.canArchiveTask(), false);
});

test('detail conflict contract preserves the local draft while refreshing authoritative data', () => {
  assert.match(appSource, /reloadSelectedTaskAfterConflict/);
  assert.match(appSource, /refreshSelectedTaskFromRealtime/);
  assert.match(detailSource, /taskConflictDraft = angular\.copy\(vm\.taskDraft\)/);
  assert.match(detailSource, /taskDraftHasChanges/);
  assert.match(detailSource, /draftPreserved = true/);
});

test('collaboration resources do not overwrite the work-item aggregate version', () => {
  assert.match(apiClientSource, /work-item-collaboration/);
  assert.match(apiClientSource, /collaboration\|watch\|vote\|activity/);
});

test('desktop surface provides drawer/page, safe text, complete states and activity filters', () => {
  for (const binding of [
    'vm.openTaskPage()', 'vm.collapseTaskDetail()', 'vm.toggleTaskWatch()', 'vm.toggleTaskVote()',
    'vm.loadMoreTaskStream(vm.taskActivityStreamName())', 'vm.addCommentMention()', 'vm.taskCatalogLinks()'
  ]) assert.ok(desktopHtml.includes(binding), `${binding} is missing`);
  assert.match(desktopHtml, /ng-bind="vm\.selectedTask\.description/);
  assert.doesNotMatch(desktopHtml, /ng-bind-html="vm\.selectedTask\.description/);
  assert.match(desktopHtml, /task-detail-loading/);
  assert.match(desktopHtml, /task-detail-permission/);
  assert.match(desktopCss, /data-detail-mode="page"/);
  assert.match(desktopCss, /\.task-detail-activity-tabs/);
});

test('mobile detail exposes collaboration parity, permission/offline states and bounded activity', () => {
  for (const method of [
    'taskCollaboration', 'setTaskWatch', 'setTaskVote', 'taskActivity', 'taskComments',
    'taskAttachments', 'taskWorkLogs', 'taskApprovals', 'taskTimeline', 'addTaskRelation'
  ]) assert.match(mobileApiSource, new RegExp(`${method}: function`));
  for (const method of ['toggleWatch', 'toggleVote', 'loadMoreStream', 'addCommentMention']) {
    assert.match(mobileDetailSource, new RegExp(`vm\\.${method} = function`));
  }
  assert.match(mobileHtml, /vm\.canEditTask\(\)/);
  assert.match(mobileHtml, /shell\.pwa\.offline/);
  assert.match(mobileHtml, /mobile-task-activity-tabs/);
  assert.match(mobileHtml, /ng-bind="vm\.task\.description/);
});
