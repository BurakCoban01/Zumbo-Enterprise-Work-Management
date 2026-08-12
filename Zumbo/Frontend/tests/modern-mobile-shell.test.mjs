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
  const projectPage = await read('projects/modern-mobile/src/app/features/work/mobile-project-work.page.ts');
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
  assert.match(projectPage, /realtime\.connect\(this\.projectId\)/);
  assert.match(projectPage, /realtime\.synchronize\(response\.result\.items\)/);
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
  assert.match(page, /realtime\.connect\(context\.detail\.projectId\)/);
  assert.match(page, /realtime\.resync\$/);
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
  assert.match(page, /realtime\.connect\(this\.projectId\)/);
  assert.match(page, /realtime\.resync\$/);
  assert.match(page, /realtime\.synchronize\(value\.tasks\)/);
  assert.equal((template.match(/<ion-segment-button/g) ?? []).length, 3);
  assert.match(template, /@for\(status of statuses\(\)/);
  assert.match(styles, /overflow-x:auto/);
  assert.match(styles, /grid-template-columns:minmax\(0,1fr\) auto/);
});

test('mobile portfolios and goals preserve strategic read and permission-driven update contracts', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const more = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.html');
  const tabs = await read('projects/modern-mobile/src/app/shell/mobile-tabs.page.ts');
  const portfolioPage = await read('projects/modern-mobile/src/app/features/strategy/mobile-portfolio.page.ts');
  const portfolioService = await read('projects/modern-mobile/src/app/features/strategy/mobile-portfolio.service.ts');
  const portfolioTemplate = await read('projects/modern-mobile/src/app/features/strategy/mobile-portfolio.page.html');
  const goalPage = await read('projects/modern-mobile/src/app/features/strategy/mobile-goal.page.ts');
  const goalService = await read('projects/modern-mobile/src/app/features/strategy/mobile-goal.service.ts');
  const goalTemplate = await read('projects/modern-mobile/src/app/features/strategy/mobile-goal.page.html');

  for (const path of ['portfolios', 'goals']) {
    assert.match(routes, new RegExp(`path: '${path}'`));
    assert.match(more, new RegExp(`/workspace/${path}`));
    assert.match(tabs, new RegExp(`'/workspace/${path}'`));
  }
  for (const endpoint of ['/api/portfolios?page=1&pageSize=100', '/roadmap', '/status-updates']) {
    assert.ok(portfolioService.includes(endpoint), `missing portfolio endpoint ${endpoint}`);
  }
  for (const endpoint of ['/api/goals?page=1&pageSize=100', '/rollup', '/progress-updates']) {
    assert.ok(goalService.includes(endpoint), `missing goal endpoint ${endpoint}`);
  }
  for (const service of [portfolioService, goalService]) {
    assert.match(service, /ifMatch:/);
    assert.match(service, /idempotencyKey:this\.api\.newIdempotencyKey\(\)/);
  }
  assert.match(portfolioPage, /canUpdateStatus===true/);
  assert.match(portfolioPage, /connectivity\.offline\(\)/);
  assert.match(goalPage, /result\?\.canUpdate/);
  assert.match(goalPage, /connectivity\.offline\(\)/);
  assert.match(portfolioTemplate, /value="Active">Aktif/);
  assert.match(portfolioTemplate, /value="NoUpdate">Güncelleme yok/);
  assert.match(portfolioTemplate, /\(ngModelChange\)="statusInitiativeChanged\(\)"/);
  for (const contract of ['/api/auth/users', 'savePortfolio', 'saveInitiative', 'saveDependency', 'api.delete']) assert.ok(portfolioService.includes(contract), `missing portfolio definition contract ${contract}`);
  for (const label of ['Yeni portföy', 'Portföyü düzenle', 'İnisiyatif ekle', 'Bağımlılık ekle']) assert.ok(portfolioTemplate.includes(label), `missing portfolio action ${label}`);
  for (const contract of ['/api/auth/users', 'saveGoal', 'saveKeyResult', 'api.delete']) assert.ok(goalService.includes(contract), `missing goal definition contract ${contract}`);
  for (const label of ['Yeni hedef', 'Hedefi düzenle', 'Anahtar sonuç ekle', 'Hedef durumu']) assert.ok(goalTemplate.includes(label), `missing goal action ${label}`);
  assert.match(goalPage, /OnTrack:'Yolunda'/);
  assert.match(goalTemplate, /sourceLabel\(value\.rollup\.sourceStatus\)/);
  assert.doesNotMatch(`${portfolioPage}\n${goalPage}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile capacity and knowledge preserve operational read and collaboration contracts', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const more = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.html');
  const tabs = await read('projects/modern-mobile/src/app/shell/mobile-tabs.page.ts');
  const capacityService = await read('projects/modern-mobile/src/app/features/capacity/mobile-capacity.service.ts');
  const capacityTemplate = await read('projects/modern-mobile/src/app/features/capacity/mobile-capacity.page.html');
  const knowledgePage = await read('projects/modern-mobile/src/app/features/knowledge/mobile-knowledge.page.ts');
  const knowledgeService = await read('projects/modern-mobile/src/app/features/knowledge/mobile-knowledge.service.ts');
  const knowledgeTemplate = await read('projects/modern-mobile/src/app/features/knowledge/mobile-knowledge.page.html');

  for (const path of ['capacity', 'knowledge']) {
    assert.match(routes, new RegExp(`path: '${path}'`));
    assert.match(more, new RegExp(`/workspace/${path}`));
    assert.match(tabs, new RegExp(`'/workspace/${path}'`));
  }
  for (const endpoint of ['/api/capacity-plans?page=1&pageSize=100', '/api/auth/users', '/snapshot']) {
    assert.ok(capacityService.includes(endpoint), `missing mobile capacity endpoint ${endpoint}`);
  }
  for (const endpoint of ['/api/knowledge-documents?page=1&pageSize=100', 'scope-link-options', '/comments', '/resolve']) {
    assert.ok(knowledgeService.includes(endpoint), `missing mobile knowledge endpoint ${endpoint}`);
  }
  assert.match(capacityTemplate, /Kişi yükü/);
  assert.match(capacityTemplate, /Proje dağılımı/);
  for (const contract of ['/scenarios', 'ifMatch:plan.version', "post<CapacityPlan>('/api/capacity-plans'", 'idempotencyKey:this.api.newIdempotencyKey()']) assert.ok(capacityService.includes(contract));
  assert.match(capacityTemplate, /Tahsis senaryosu/);
  assert.match(capacityTemplate, /Planı arşivle/);
  assert.match(knowledgePage, /document\.canComment/);
  assert.match(knowledgePage, /connectivity\.offline\(\)/);
  assert.match(knowledgeService, /ifMatch:document\.version/);
  assert.match(knowledgeService, /idempotencyKey:this\.api\.newIdempotencyKey\(\)/);
  assert.match(knowledgeService, /api\.put<KnowledgeDocument>/);
  assert.match(knowledgeService, /api\.delete/);
  assert.match(knowledgePage, /BoardManage/);
  assert.match(knowledgeTemplate, /Yeni doküman/);
  assert.match(knowledgeTemplate, /Yeni sürüm/);
  assert.equal((knowledgeTemplate.match(/<ion-segment-button/g) ?? []).length, 4);
  assert.doesNotMatch(`${capacityService}\n${knowledgePage}\n${knowledgeTemplate}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile public intake and teams preserve anonymous submission and membership authority', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const more = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.html');
  const tabs = await read('projects/modern-mobile/src/app/shell/mobile-tabs.page.ts');
  const intakeCore = await read('projects/modern-mobile/src/app/features/intake/mobile-public-intake.core.ts');
  const intakeService = await read('projects/modern-mobile/src/app/features/intake/mobile-public-intake.service.ts');
  const intakePage = await read('projects/modern-mobile/src/app/features/intake/mobile-public-intake.page.ts');
  const intakeTemplate = await read('projects/modern-mobile/src/app/features/intake/mobile-public-intake.page.html');
  const teamCore = await read('projects/modern-mobile/src/app/features/teams/mobile-team.core.ts');
  const teamService = await read('projects/modern-mobile/src/app/features/teams/mobile-team.service.ts');
  const teamPage = await read('projects/modern-mobile/src/app/features/teams/mobile-team.page.ts');

  assert.match(routes, /path: 'intake\/:publicId'/);
  assert.match(routes, /path: 'teams\/:teamId'/);
  assert.match(routes, /path: 'teams'/);
  assert.match(more, /\/workspace\/teams/);
  assert.match(tabs, /'\/workspace\/teams'/);
  for (const endpoint of ['/api/intake/public/forms/', '/submissions']) assert.ok(intakeService.includes(endpoint));
  assert.match(intakeService, /idempotencyKey:this\.api\.newIdempotencyKey\(\)/);
  assert.match(intakeCore, /files\.length>5/);
  assert.match(intakeCore, /25\*1024\*1024/);
  assert.match(intakePage, /connectivity\.offline\(\)/);
  assert.match(intakePage, /INTAKE_FORM_NOT_FOUND:'Paylaşılan form bulunamadı\.'/);
  for (const type of ['LongText', 'Email', 'Number', 'Date', 'Choice', 'Checkbox', 'Attachment']) assert.ok(intakeTemplate.includes(`@case('${type}')`));
  assert.match(intakeTemplate, /class="honeypot"/);
  for (const endpoint of ['/api/teams?organizationId=', '/api/auth/roles', '/api/audit/entity/Team/', '/members/']) assert.ok(teamService.includes(endpoint));
  assert.match(teamService, /ifMatch:team\.version/);
  assert.match(teamService, /idempotencyKey:this\.api\.newIdempotencyKey\(\)/);
  assert.match(teamCore, /teamMembership\(team,userId\)\?\.role/);
  assert.match(teamPage, /systemPermission\(ctx\.roles,user\.roles,'TeamManage'\)/);
  assert.match(teamPage, /connectivity\.offline\(\)/);
  assert.doesNotMatch(`${intakePage}\n${teamPage}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile project catalog preserves delivery lifecycle and runtime project authority', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const hub = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.page.html');
  const page = await read('projects/modern-mobile/src/app/features/catalog/mobile-project-catalog.page.ts');
  const service = await read('projects/modern-mobile/src/app/features/catalog/mobile-project-catalog.service.ts');
  const template = await read('projects/modern-mobile/src/app/features/catalog/mobile-project-catalog.page.html');
  const styles = await read('projects/modern-mobile/src/app/features/catalog/mobile-project-catalog.page.scss');

  assert.match(routes, /path: 'projects\/:projectId\/catalog'/);
  assert.match(hub, /\['\/workspace\/projects',projectId,'catalog'\]/);
  for (const endpoint of ['/api/projects/', '/api/auth/roles?scope=Project', '/api/auth/users', '/api/audit/entity/Project/', '/templates', '/components', '/versions', '/releases', '/milestones']) {
    assert.ok(service.includes(endpoint), `missing mobile catalog endpoint ${endpoint}`);
  }
  assert.match(page, /canManageProjectCatalog/);
  assert.match(page, /canReleaseProjectCatalog/);
  assert.match(page, /connectivity\.offline\(\)/);
  assert.match(page, /projectCatalogErrorMessage/);
  for (const tab of ['releases', 'milestones', 'components', 'templates', 'activity']) {
    assert.ok(template.includes(`value="${tab}"`) || template.includes(`'${tab}'`), `missing catalog tab ${tab}`);
  }
  for (const action of ['approveRelease', 'publishRelease', 'completeMilestone', 'archiveTemplate', 'archiveComponent', 'archiveVersion']) {
    assert.ok(page.includes(action), `missing catalog lifecycle action ${action}`);
  }
  assert.match(styles, /min-height:\s*44px/);
  assert.doesNotMatch(`${page}\n${template}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile project intake preserves form authoring, internal submission and triage authority', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const hub = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.page.html');
  const core = await read('projects/modern-mobile/src/app/features/intake/mobile-project-intake.core.ts');
  const service = await read('projects/modern-mobile/src/app/features/intake/mobile-project-intake.service.ts');
  const page = await read('projects/modern-mobile/src/app/features/intake/mobile-project-intake.page.ts');
  const template = await read('projects/modern-mobile/src/app/features/intake/mobile-project-intake.page.html');
  const styles = await read('projects/modern-mobile/src/app/features/intake/mobile-project-intake.page.scss');

  assert.match(routes, /path: 'projects\/:projectId\/intake'/);
  assert.match(hub, /\['\/workspace\/projects',projectId,'intake'\]/);
  for (const endpoint of ['/api/projects/', '/api/boards/by-project/', '/api/work-item-schemas/', '/api/intake/forms?projectId=', '/api/auth/roles?scope=Project', '/published', '/submissions?page=1&pageSize=100', '/triage']) {
    assert.ok(service.includes(endpoint), `missing mobile project-intake endpoint ${endpoint}`);
  }
  for (const permission of ['WorkflowManage', 'WorkItemCreate', 'WorkItemUpdate']) assert.ok(page.includes(permission), `missing intake permission ${permission}`);
  assert.match(page, /connectivity\.offline\(\)/);
  assert.match(service, /idempotencyKey:\s*this\.api\.newIdempotencyKey\(\)/);
  assert.match(core, /25\s*\*\s*1024\s*\*\s*1024/);
  for (const tab of ['forms', 'submit', 'triage']) assert.ok(template.includes(`value="${tab}"`) || template.includes(`'${tab}'`), `missing intake tab ${tab}`);
  for (const type of ['LongText', 'Email', 'Number', 'Date', 'Choice', 'Checkbox', 'Attachment']) assert.ok(template.includes(`'${type}'`), `missing intake field type ${type}`);
  for (const action of ['saveForm', 'publishForm', 'archiveForm', 'submitRequest', 'triage']) assert.ok(page.includes(action), `missing intake action ${action}`);
  assert.match(template, /class="honeypot"/);
  assert.match(styles, /min-height:\s*(?:44|46)px/);
  assert.doesNotMatch(`${page}\n${template}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile Jobs preserves durable operations, recovery and artifact authority', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const hub = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.page.html');
  const core = await read('projects/modern-mobile/src/app/features/jobs/mobile-jobs.core.ts');
  const service = await read('projects/modern-mobile/src/app/features/jobs/mobile-jobs.service.ts');
  const page = await read('projects/modern-mobile/src/app/features/jobs/mobile-jobs.page.ts');
  const template = await read('projects/modern-mobile/src/app/features/jobs/mobile-jobs.page.html');
  const styles = await read('projects/modern-mobile/src/app/features/jobs/mobile-jobs.page.scss');
  assert.match(routes, /path: 'projects\/:projectId\/jobs'/);
  assert.match(hub, /\['\/workspace\/projects',projectId,'jobs'\]/);
  assert.match(service, /bulk\/jobs\?projectId=.*page=1&pageSize=50/);
  for (const endpoint of ["'/api/work-items/bulk/jobs/import'", "'/api/work-items/bulk/jobs/export'", '/cancel', '/retry', "'errors'", "'result'"]) assert.ok(service.includes(endpoint), `missing Jobs contract ${endpoint}`);
  assert.match(service, /idempotencyKey:\s*this\.api\.newIdempotencyKey\(\)/);
  assert.match(service, /ifMatch:\s*job\.version/);
  for (const permission of ['WorkItemCreate', 'WorkItemView', 'WorkItemUpdate']) assert.ok(page.includes(permission), `missing Jobs permission ${permission}`);
  assert.match(core, /maxInputItems:\s*5000/);
  assert.match(core, /5\s*\*\s*1024\s*\*\s*1024/);
  assert.match(page, /setTimeout\(\(\) => this\.refreshJobs\(\),\s*2750\)/);
  assert.match(page, /connectivity\.offline\(\)/);
  assert.match(`${styles}\n${template}`, /min-height:\s*(?:44|46|48)px/);
  assert.doesNotMatch(`${page}\n${template}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile Reporting preserves freshness-aware reports and adaptive dashboard reading', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const hub = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.page.html');
  const core = await read('projects/modern-mobile/src/app/features/reporting/mobile-reporting.core.ts');
  const service = await read('projects/modern-mobile/src/app/features/reporting/mobile-reporting.service.ts');
  const page = await read('projects/modern-mobile/src/app/features/reporting/mobile-reporting.page.ts');
  const template = await read('projects/modern-mobile/src/app/features/reporting/mobile-reporting.page.html');
  const styles = await read('projects/modern-mobile/src/app/features/reporting/mobile-reporting.page.scss');
  assert.match(routes, /path: 'projects\/:projectId\/insights'/);
  assert.match(hub, /\['\/workspace\/projects',projectId,'insights'\]/);
  for (const report of ['project-summary', 'status-distribution', 'user-workload', 'due-date-risks', 'flow-time', 'completion-rate', 'team-performance']) assert.ok(service.includes(report), `missing report source ${report}`);
  assert.match(service, /rawResponse:\s*true/);
  assert.match(core, /X-Zumbo-Report-Generated-At/);
  assert.match(core, /X-Zumbo-Report-Stale/);
  for (const dashboard of ['/api/dashboards?page=1&pageSize=100', '/render', '/export']) assert.ok(service.includes(dashboard), `missing dashboard contract ${dashboard}`);
  for (const tab of ['workload', 'reports', 'dashboards']) assert.ok(template.includes(`'${tab}'`), `missing reporting tab ${tab}`);
  assert.match(page, /WorkItemView/);
  assert.match(page, /connectivity\.offline\(\)/);
  assert.match(page, /queryParams:\s*\{\s*mode:/);
  assert.match(`${styles}\n${template}`, /min-height:\s*(?:44|46|48)px/);
  assert.doesNotMatch(`${page}\n${template}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile Automation preserves rule, run, template and recurrence lifecycle contracts', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const hub = await read('projects/modern-mobile/src/app/features/project-hub/mobile-project-hub.page.html');
  const service = await read('projects/modern-mobile/src/app/features/automation/mobile-automation.service.ts');
  const page = await read('projects/modern-mobile/src/app/features/automation/mobile-automation.page.ts');
  const template = await read('projects/modern-mobile/src/app/features/automation/mobile-automation.page.html');
  const styles = await read('projects/modern-mobile/src/app/features/automation/mobile-automation.page.scss');
  assert.match(routes, /path: 'projects\/:projectId\/automation'/);
  assert.match(hub, /\['\/workspace\/projects',projectId,'automation'\]/);
  for (const endpoint of ['/api/automations?projectId=', '/api/automations/runs?projectId=', '/api/work-items/templates?projectId=', '/api/work-items/recurrences?projectId=', '/dry-run', '/publish', '/replay', '/preview', '/occurrences']) assert.ok(service.includes(endpoint), `missing Automation contract ${endpoint}`);
  for (const permission of ['WorkflowManage', 'WorkItemCreate', 'WorkItemUpdate']) assert.ok(page.includes(permission), `missing Automation permission ${permission}`);
  for (const tab of ['rules', 'runs', 'templates', 'recurrences']) assert.ok(template.includes(`'${tab}'`), `missing Automation tab ${tab}`);
  for (const action of ['saveRule', 'publishRule', 'setRuleState', 'archiveRule', 'replayRun', 'saveTemplate', 'archiveTemplate', 'previewRecurrence', 'createRecurrence', 'setRecurrenceState', 'archiveRecurrence']) assert.ok(page.includes(action), `missing Automation action ${action}`);
  assert.match(service, /ifMatch:/);
  assert.match(page, /connectivity\.offline\(\)/);
  assert.match(page, /queryParams:\s*\{\s*tab/);
  assert.match(styles, /min-height:\s*(?:44|46|48)px/);
  assert.doesNotMatch(`${page}\n${template}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile Integrations preserves system authority, delivery recovery and one-time secret handling', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const morePage = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.ts');
  const moreTemplate = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.html');
  const service = await read('projects/modern-mobile/src/app/features/integrations/mobile-integrations.service.ts');
  const page = await read('projects/modern-mobile/src/app/features/integrations/mobile-integrations.page.ts');
  const template = await read('projects/modern-mobile/src/app/features/integrations/mobile-integrations.page.html');
  assert.match(routes, /path: 'integrations'/);
  assert.match(routes, /path: 'profile\/integrations'/);
  assert.match(moreTemplate, /\/workspace\/integrations/);
  assert.match(morePage, /\/api\/auth\/roles\?scope=System/);
  assert.match(morePage, /IntegrationManage/);
  for (const endpoint of ['/api/integrations/webhooks', '/deliveries', '/test-delivery', 'rotate-secret', "active ? 'enable' : 'disable'", '/replay', '/api/integrations/development', '/health', '/repositories', '/mappings', '/rotate-credential', '/disconnect']) assert.ok(service.includes(endpoint), `missing Integrations contract ${endpoint}`);
  assert.match(service, /ifMatch:/);
  assert.match(service, /expectedVersion/);
  assert.match(page, /IntegrationManage/);
  assert.match(page, /connectivity\.offline\(\)/);
  assert.match(page, /secret\.set\(null\)/);
  assert.match(page, /queryParams:\s*\{\s*tab/);
  for (const tab of ['webhooks', 'development']) assert.ok(template.includes(`'${tab}'`), `missing Integrations tab ${tab}`);
  assert.doesNotMatch(`${morePage}\n${page}\n${template}`, /Administrator|SystemAdmin|ProjectOwner/);
});

test('mobile Operations preserves partial health reads and confirmed intervention authority', async () => {
  const routes = await read('projects/modern-mobile/src/app/app.routes.ts');
  const morePage = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.ts');
  const moreTemplate = await read('projects/modern-mobile/src/app/features/more/mobile-more.page.html');
  const service = await read('projects/modern-mobile/src/app/features/operations/mobile-operations.service.ts');
  const page = await read('projects/modern-mobile/src/app/features/operations/mobile-operations.page.ts');
  const template = await read('projects/modern-mobile/src/app/features/operations/mobile-operations.page.html');
  const styles = await read('projects/modern-mobile/src/app/features/operations/mobile-operations.page.scss');
  assert.match(routes, /path: 'operations'/);
  assert.match(routes, /path: 'profile\/operations'/);
  assert.match(moreTemplate, /\/workspace\/operations/);
  assert.match(morePage, /OperationsManage/);
  for (const endpoint of ['/api/operations/external-dependencies', '/api/work-items/durable-messaging/metrics', '/api/work-items/durable-messaging/dead-letters?pageSize=20', '/api/notifications/delivery/status?organizationId=', '/api/notifications/delivery/dead-letters?organizationId=', '/api/operations/storage/security?organizationId=', '/api/work-items/search/reconcile', '/replay', '/maintenance']) assert.ok(service.includes(endpoint), `missing Operations contract ${endpoint}`);
  assert.match(service, /catchError/);
  assert.match(page, /OperationsManage/);
  assert.match(page, /connectivity\.offline\(\)/);
  assert.match(page, /window\.confirm|confirm\(/);
  assert.match(styles, /min-height:\s*(?:44|46|48)px/);
  for (const label of ['Bağımlılık sağlığı', 'Sistem olayları', 'Bildirim teslimatı', 'Dosya güvenliği', 'Arama görünümü']) assert.ok(template.includes(label), `missing Operations surface ${label}`);
  assert.doesNotMatch(`${morePage}\n${page}\n${template}`, /Administrator|SystemAdmin|ProjectOwner/);
});

function transpileCommonJs(source) {
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
  const module = { exports: {} };
  Function('exports', 'module', output)(module.exports, module);
  return module.exports;
}
