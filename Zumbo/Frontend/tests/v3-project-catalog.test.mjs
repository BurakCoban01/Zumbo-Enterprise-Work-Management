import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import vmModule from 'node:vm';

const require = createRequire(import.meta.url);
const root = resolve(import.meta.dirname, '..');
const core = require(resolve(root, 'shared/project-catalog-core.js'));
const desktopSource = await readFile(resolve(root, 'desktop-bulma/project-catalog.js'), 'utf8');
const desktopManagement = await readFile(resolve(root, 'desktop-bulma/management.js'), 'utf8');
const desktopHtml = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const desktopCss = await readFile(resolve(root, 'desktop-bulma/project-catalog.css'), 'utf8');
const mobileApp = await readFile(resolve(root, 'mobile-ionic/app.js'), 'utf8');
const mobileApi = await readFile(resolve(root, 'mobile-ionic/api.js'), 'utf8');
const mobileSource = await readFile(resolve(root, 'mobile-ionic/project-catalog.js'), 'utf8');
const mobileHtml = await readFile(resolve(root, 'mobile-ionic/index.html'), 'utf8');
const mobileCss = await readFile(resolve(root, 'mobile-ionic/project-catalog.css'), 'utf8');
const backend = await readFile(resolve(root, '../Backend/src/Zumbo.Modules.Projects/ProjectCatalogLifecycle.cs'), 'utf8');

function project(overrides = {}) {
  return {
    id: 'project-1',
    version: 4,
    members: [{ userId: 'owner-1', role: 'ProjectOwner' }],
    templates: [],
    components: [],
    versions: [],
    releases: [],
    milestones: [],
    ...overrides
  };
}

test('template defaults expose the exact backend limit without silent truncation', () => {
  const fifty = Array.from({ length: 50 }, (_, index) => `Component ${index + 1}`).join('\n');
  const fiftyOne = `${fifty}\nComponent 51`;
  assert.equal(core.normalizeComponentNames(fifty).tooMany, false);
  assert.equal(core.normalizeComponentNames(fiftyOne).tooMany, true);
  assert.deepEqual(core.normalizeComponentNames('API, Web\napi').values, ['API', 'Web']);
  assert.equal(core.limits.defaultComponentCount, 50);
  assert.doesNotMatch(backend, /\.Take\(50\)/);
  assert.match(backend, /ProjectCatalogLimits\.MaximumDefaultComponentNames/);
});

test('catalog permission and projection preserve lifecycle state', () => {
  assert.equal(core.canManage('ProjectOwner'), true);
  assert.equal(core.canManage('ProjectAdmin'), true);
  assert.equal(core.canManage('Developer'), false);
  assert.equal(core.canRelease('ProjectAdmin'), false);
  assert.equal(core.canRelease('ProjectOwner'), true);

  const model = core.snapshot(project({
    templates: [{ id: 't1', archived: false }, { id: 't2', archived: true }],
    components: [{ id: 'c1', archived: false }],
    versions: [{ id: 'v1', status: 'Planned' }, { id: 'v2', status: 'Released' }],
    milestones: [
      { id: 'm2', status: 'Completed', dueAt: '2026-09-02T00:00:00Z' },
      { id: 'm1', status: 'Open', dueAt: '2026-08-01T00:00:00Z' }
    ]
  }));
  assert.equal(model.activeTemplates.length, 1);
  assert.equal(model.plannedVersions.length, 1);
  assert.deepEqual(model.milestones.map(item => item.id), ['m1', 'm2']);
  assert.equal(model.openMilestones.length, 1);
});

function desktopFeature(apiOverrides = {}) {
  let provider;
  const module = {
    factory(name, factory) {
      assert.equal(name, 'desktopProjectCatalogFeature');
      provider = factory;
      return module;
    }
  };
  const angular = {
    module: () => module,
    noop() {}
  };
  const window = { ZumboProjectCatalogCore: core };
  vmModule.runInNewContext(desktopSource, { angular, window, Date, Object, Array, String });
  const api = {
    get: () => Promise.resolve(project({ version: 8 })),
    post: () => Promise.resolve(project({ version: 5, versions: [{ id: 'v1', name: '3.2', status: 'Planned' }] })),
    put: () => Promise.resolve(project()),
    delete: () => Promise.resolve(project()),
    ...apiOverrides
  };
  return provider({ when: value => Promise.resolve(value) }, api);
}

test('desktop catalog applies authoritative mutation responses and reloads stale projects', async () => {
  const feature = desktopFeature();
  const current = project();
  const vm = {
    project: current,
    projects: [current],
    session: { currentUser: { id: 'owner-1' } },
    entityAudit: [],
    notify(kind, message) { vm.notification = { kind, message }; },
    loadProjectAudit: () => Promise.resolve([])
  };
  feature.install(vm, {
    setProjectState(next) {
      vm.project = next;
      vm.syncProjectCatalog(next);
      return next;
    }
  });
  vm.projectVersionDraft.name = '3.2';
  await vm.createProjectVersion();
  assert.equal(vm.project.version, 5);
  assert.equal(vm.projectCatalog.versions[0].name, '3.2');
  assert.match(vm.notification.message, /Sürüm oluşturuldu/);

  await vm.reloadProjectAfterConflict();
  assert.equal(vm.project.version, 8);
  assert.equal(vm.projectVersionDraft.name, '');
});

test('desktop and mobile surfaces expose lifecycle, audit, limits and normal navigation', () => {
  assert.match(desktopHtml, /vm\.workMode === 'catalog'/);
  assert.match(desktopHtml, /role="tablist" aria-label="Proje teslimat alanları"/);
  assert.match(desktopHtml, /vm\.approveProjectRelease/);
  assert.match(desktopHtml, /vm\.publishProjectRelease/);
  assert.match(desktopHtml, /vm\.projectCatalogAudit\(\)/);
  assert.match(desktopHtml, /defaultComponentCount/);
  assert.match(desktopCss, /grid-template-columns: minmax\(260px, 340px\)/);
  assert.match(desktopSource, /reloadProjectAfterConflict/);
  assert.match(desktopManagement, /apiClient\.remember\('\/api\/projects\/' \+ project\.id, project\)/);

  assert.match(mobileApp, /state\('project-catalog'/);
  assert.match(mobileHtml, /templates\/project-catalog\.html/);
  assert.match(mobileHtml, /aria-label="Mobil proje teslimat alanları"/);
  assert.match(mobileHtml, /vm\.approveRelease/);
  assert.match(mobileHtml, /vm\.publishRelease/);
  assert.match(mobileSource, /controller\('ProjectCatalogController'/);
  assert.match(mobileApi, /upsertProjectTemplate/);
  assert.match(mobileApi, /completeProjectMilestone/);
  assert.match(mobileCss, /\.mobile-catalog-tabs button\s*\{[\s\S]*flex: 1 1 0;[\s\S]*min-width: 0;/);
});
