import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import ts from 'typescript';

const root = resolve(import.meta.dirname, '..');
const read = path => readFile(resolve(root, path), 'utf8');

test('modern Ionic shell exposes five functional lazy routes and safe daily-work contracts', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const tabs = await read('projects/modern-mobile/src/app/shell/mobile-tabs.page.html');
  const store = await read('projects/modern-mobile/src/app/shell/mobile-workspace.store.ts');
  const create = await read('projects/modern-mobile/src/app/features/create/mobile-create.page.ts');
  const more = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.html');

  for (const path of ['home', 'work', 'create', 'inbox', 'more', 'projects']) assert.match(routes, new RegExp(`path: '${path}'`));
  assert.equal((tabs.match(/<ion-tab-button/g) ?? []).length, 5);
  assert.match(store, /\/api\/work-items\/search/);
  assert.match(store, /\/api\/notifications\?page=1&pageSize=50/);
  assert.match(store, /assigneeUserId: user\.id/);
  assert.match(create, /idempotencyKey\s*:\s*this\.api\.newIdempotencyKey\(\)/);
  assert.match(create, /WorkItemCreate/);
  assert.doesNotMatch(more, /M09|sonraki faz|sonraki görev|taşınacak/i);
});

test('mobile work projections keep open, blocked and due behavior deterministic', async () => {
  const source = await read('projects/modern-mobile/src/app/shell/mobile-workspace.models.ts');
  const model = transpileCommonJs(source);
  assert.equal(model.isOpen({ completedAt: null }), true);
  assert.equal(model.isOpen({ completedAt: '2026-08-11T10:00:00Z' }), false);
  assert.equal(model.isBlocked({ relations: [{ relationType: 'IsBlockedBy' }] }), true);
  assert.ok(model.dueTime({ dueDate: '2026-08-10' }) < model.dueTime({ dueDate: null }));
});

test('mobile search and project work preserve scoped paging and whole-card task navigation', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const service = await read('projects/modern-mobile/src/app/features/work/mobile-work.service.ts');
  const search = await read('projects/modern-mobile/src/app/features/work/mobile-search.page.ts');
  const searchTemplate = await read('projects/modern-mobile/src/app/features/work/mobile-search.page.html');
  const projectTemplate = await read('projects/modern-mobile/src/app/features/work/mobile-project-work.page.html');
  const detailService = await read('projects/modern-mobile/src/app/features/task-detail/mobile-task-detail.service.ts');
  const detailTemplate = await read('projects/modern-mobile/src/app/features/task-detail/mobile-task-detail.page.html');
  const tabs = await read('projects/modern-mobile/src/app/shell/mobile-tabs.page.ts');

  assert.match(routes, /path:\s*'search'/);
  assert.match(routes, /path:\s*'projects\/:projectId\/work'/);
  assert.match(routes, /path:\s*'tasks\/:taskId'/);
  assert.match(service, /projectId[\s\S]*text:\s*text\.trim\(\)[\s\S]*pageSize/);
  assert.match(service, /\/api\/workflows\/\$\{encodeURIComponent\(projectId\)\}/);
  assert.match(search, /query\.length\s*<\s*2/);
  assert.match(search, /result\.items\.length\s*===\s*50/);
  assert.match(searchTemplate, /<a class="work-card"[^>]*\[routerLink\]="\['\/tasks',item\.id\]"/);
  assert.match(projectTemplate, /<a class="work-card"[^>]*\[routerLink\]="\['\/tasks',item\.id\]"/);
  assert.match(detailService, /\/api\/work-items\/\$\{encodeURIComponent\(taskId\)\}/);
  assert.match(detailTemplate, /aria-label="Geri"/);
  assert.match(tabs, /'\/workspace\/search'/);
});

test('mobile work paging appends only unseen task identities', async () => {
  const source = await read('projects/modern-mobile/src/app/features/work/mobile-work.core.ts');
  const model = transpileCommonJs(source);
  const merged = model.mergeUniqueWorkItems([{ id: 'one' }, { id: 'two' }], [{ id: 'two' }, { id: 'three' }]);
  assert.deepEqual(merged.map(item => item.id), ['one', 'two', 'three']);
});

test('mobile task detail keeps permission, offline and bounded collaboration contracts', async () => {
  const page = await read('projects/modern-mobile/src/app/features/task-detail/mobile-task-detail.page.ts');
  const template = await read('projects/modern-mobile/src/app/features/task-detail/mobile-task-detail.page.html');
  const service = await read('projects/modern-mobile/src/app/features/task-detail/mobile-task-detail.service.ts');
  const models = await read('projects/modern-mobile/src/app/features/task-detail/mobile-task-detail.models.ts');

  for (const permission of ['WorkItemUpdate', 'WorkItemMove', 'CommentCreate', 'WorkLogCreate', 'AttachmentCreate']) {
    assert.match(page, new RegExp(`hasPermission\\('${permission}'\\)`));
  }
  assert.match(page, /connectivity\.offline\(\)/);
  assert.equal((template.match(/role="tab"/g) ?? []).length, 3);
  assert.match(template, /İlk \{\{context\(\)\?\.activity\?\.items\?\.length\|\|0\}\}/);
  for (const endpoint of ['/collaboration', '/checklist', '/status', '/watch', '/vote']) {
    assert.ok(service.includes(endpoint), `missing task-detail endpoint ${endpoint}`);
  }
  assert.match(service, /page=1&pageSize=50/);
  assert.match(models, /MobileTaskStream = 'activity' \| 'attachments' \| 'comments' \| 'worklogs'/);
  assert.match(models, /export type MobileTaskDetailTab = 'summary' \| 'work' \| 'activity'/);
});

test('mobile account and password recovery preserve real security contracts', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const session = await read('projects/modern-shared/src/lib/session.service.ts');
  const account = await read('projects/modern-mobile/src/app/features/account/mobile-account.page.ts');
  const service = await read('projects/modern-mobile/src/app/features/account/mobile-account.service.ts');
  const template = await read('projects/modern-mobile/src/app/features/account/mobile-account.page.html');
  const tabs = await read('projects/modern-mobile/src/app/shell/mobile-tabs.page.ts');

  for (const path of ['forgot-password', 'reset-password', 'account']) assert.match(routes, new RegExp(`path: '${path}'`));
  assert.match(session, /\/api\/auth\/forgot-password/);
  assert.match(session, /\/api\/auth\/reset-password/);
  for (const endpoint of ['/api/auth/mfa', '/api/auth/sessions', '/api/auth/api-keys', '/api/notifications/preferences/me', '/api/auth/privacy/export.ndjson']) {
    assert.ok(service.includes(endpoint), `missing account endpoint ${endpoint}`);
  }
  assert.match(account, /showAllSessions/);
  assert.match(account, /navigator\.onLine/);
  assert.match(tabs, /'\/workspace\/account'/);
  assert.equal((template.match(/<ion-segment-button/g) ?? []).length, 3);
  assert.match(template, /Tüm ' \+ sessions\(\)\.length \+ ' oturumu göster/);
  assert.match(template, /privacyDraft\.confirmation !== 'ANONYMIZE'/);
});

test('mobile account session projection bounds long histories until explicitly expanded', async () => {
  const source = await read('projects/modern-mobile/src/app/features/account/mobile-account.core.ts');
  const model = transpileCommonJs(source);
  const future = '2099-08-11T20:00:00Z';
  const past = '2020-08-11T20:00:00Z';
  const sessions = [
    { id: 'current', isCurrent: true, expiresAt: future, lastSeenAt: future },
    ...Array.from({ length: 8 }, (_, index) => ({ id: `active-${index}`, isCurrent: false, expiresAt: future, lastSeenAt: `2099-08-${String(10 - index).padStart(2, '0')}T20:00:00Z` })),
    ...Array.from({ length: 5 }, (_, index) => ({ id: `inactive-${index}`, isCurrent: false, expiresAt: past, lastSeenAt: `2020-08-${String(10 - index).padStart(2, '0')}T20:00:00Z` }))
  ];
  assert.equal(model.visibleSessions(sessions, false, Date.parse('2026-08-11T20:00:00Z')).length, 7);
  assert.equal(model.visibleSessions(sessions, true).length, sessions.length);
});

test('mobile project hub preserves overview, adaptive board and planning access', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const workspace = await read('projects/modern-mobile/src/app/workspace.page.html');
  const page = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.page.ts');
  const service = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.service.ts');
  const template = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.page.html');
  const styles = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub-board.scss');

  assert.match(routes, /path:\s*'projects\/:projectId'/);
  assert.match(workspace, /\['\/workspace\/projects',project\.id\]/);
  for (const endpoint of ['project-summary', 'due-date-risks', '/api/work-items/search', '/api/workflows/', '/api/sprints/projects/', '/backlog?pageSize=100', 'roles?scope=Project']) {
    assert.ok(service.includes(endpoint), `missing project-hub endpoint ${endpoint}`);
  }
  assert.match(page, /hasPermission\('WorkItemMove'\)/);
  assert.match(page, /tasks:snapshot\.tasks\.map/);
  assert.match(page, /this\.data\.set\(snapshot\)/);
  assert.equal((template.match(/<ion-segment-button/g) ?? []).length, 3);
  assert.match(template, /@for\(status of statuses\(\)/);
  assert.match(styles, /overflow-x:auto/);
  assert.match(styles, /grid-template-columns:minmax\(0,1fr\) auto/);
});

function transpileCommonJs(source) {
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
  const module = { exports: {} };
  Function('exports', 'module', output)(module.exports, module);
  return module.exports;
}
