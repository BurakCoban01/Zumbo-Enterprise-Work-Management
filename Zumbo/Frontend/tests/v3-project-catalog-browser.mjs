import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-surface-001');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = Date.now();
const owner = { id: 'owner-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const viewer = { id: 'viewer-1', username: 'deniz', email: 'deniz@zumbo.local', organizationId: 'org-1', roles: ['User'] };
let project = {
  id: 'project-1', organizationId: 'org-1', key: 'REL', name: 'Yayın Merkezi', visibility: 'Private', version: 4,
  members: [{ userId: owner.id, role: 'ProjectOwner' }, { userId: viewer.id, role: 'Viewer' }], teamIds: [],
  templates: [{ id: 'template-1', name: 'Teslimat', isDefault: true, archived: false, defaultComponentNames: ['API', 'Web'] }],
  components: [{ id: 'component-1', name: 'API', description: 'Genel API', archived: false }],
  versions: [{ id: 'version-1', name: '3.1', status: 'Planned', releasedAt: null }],
  releases: [],
  milestones: [{ id: 'milestone-1', name: 'Pilot', dueAt: new Date(now + 864000000).toISOString(), status: 'Open', completedAt: null }],
  archived: false
};
const audit = [
  { id: 'audit-1', action: 'ProjectVersionCreated', actorUserId: owner.id, createdAt: new Date(now - 3600000).toISOString() }
];
let staleNextComponent = false;
let id = 10;
const checks = [];
const failures = [];

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-surface-001' });
}

function errorEnvelope(code, message) {
  return JSON.stringify({ success: false, data: null, error: { code, message }, correlationId: 'v3-surface-001' });
}

function nextId(prefix) {
  id += 1;
  return `${prefix}-${id}`;
}

function record(action) {
  audit.unshift({ id: nextId('audit'), action, actorUserId: owner.id, createdAt: new Date().toISOString() });
}

function mutate(request, action) {
  const expected = request.headers()['if-match'];
  assert.equal(expected, `"${project.version}"`, `${action} did not carry current If-Match`);
  action();
  project = { ...project, version: project.version + 1 };
  return project;
}

function routeData(url, request) {
  const path = url.pathname;
  const method = request.method();
  const body = request.postData() ? request.postDataJSON() : {};
  if (path === '/api/projects' && method === 'GET') return [project];
  if (path === `/api/projects/${project.id}` && method === 'GET') return project;
  if (path === `/api/audit/entity/Project/${project.id}`) return audit;
  if (path === `/api/boards/by-project/${project.id}`) return [];
  if (path === `/api/workflows/${project.id}`) return { projectId: project.id, statuses: [], transitions: [] };
  if (path === `/api/work-item-schemas/${project.id}`) return { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
  if (path === '/api/work-items/search') return { items: [], totalCount: 0, degraded: false };
  if (path.startsWith('/api/work-items/reports/')) return [];
  if (path === `/api/sprints/projects/${project.id}` || path === `/api/sprints/projects/${project.id}/backlog`) return { items: [], totalCount: 0 };
  if (path === '/api/teams' || path === '/api/auth/users' || path.startsWith('/api/notifications')) return [];

  if (path === `/api/projects/${project.id}/versions` && method === 'POST') {
    return mutate(request, () => {
      project = { ...project, versions: project.versions.concat({
        id: nextId('version'), name: body.name, status: 'Planned', releasedAt: null
      }) };
      record('ProjectVersionCreated');
    });
  }
  if (/\/versions\/[^/]+$/.test(path) && method === 'DELETE') {
    return mutate(request, () => {
      const versionId = path.split('/').at(-1);
      project = { ...project, versions: project.versions.map(item => item.id === versionId ? { ...item, status: 'Archived' } : item) };
      record('ProjectVersionArchived');
    });
  }
  if (path === `/api/projects/${project.id}/releases` && method === 'POST') {
    return mutate(request, () => {
      project = { ...project, releases: project.releases.concat({
        id: nextId('release'), versionId: body.versionId, name: body.name, status: 'Draft',
        scheduledAt: body.scheduledAt, approvedAt: null, publishedAt: null
      }) };
      record('ProjectReleaseCreated');
    });
  }
  if (/\/releases\/[^/]+\/approve$/.test(path)) {
    return mutate(request, () => {
      const releaseId = path.split('/').at(-2);
      project = { ...project, releases: project.releases.map(item => item.id === releaseId ? { ...item, status: 'Approved', approvedAt: new Date().toISOString() } : item) };
      record('ProjectReleaseApproved');
    });
  }
  if (/\/releases\/[^/]+\/publish$/.test(path)) {
    return mutate(request, () => {
      const releaseId = path.split('/').at(-2);
      const release = project.releases.find(item => item.id === releaseId);
      project = {
        ...project,
        releases: project.releases.map(item => item.id === releaseId ? { ...item, status: 'Published', publishedAt: new Date().toISOString() } : item),
        versions: project.versions.map(item => item.id === release.versionId ? { ...item, status: 'Released', releasedAt: new Date().toISOString() } : item)
      };
      record('ProjectReleasePublished');
    });
  }
  if (path === `/api/projects/${project.id}/milestones` && method === 'POST') {
    return mutate(request, () => {
      project = { ...project, milestones: project.milestones.concat({
        id: nextId('milestone'), name: body.name, dueAt: body.dueAt, status: 'Open', completedAt: null
      }) };
      record('ProjectMilestoneCreated');
    });
  }
  if (/\/milestones\/[^/]+$/.test(path) && method === 'PUT') {
    return mutate(request, () => {
      const milestoneId = path.split('/').at(-1);
      project = { ...project, milestones: project.milestones.map(item => item.id === milestoneId ? { ...item, name: body.name, dueAt: body.dueAt } : item) };
      record('ProjectMilestoneUpdated');
    });
  }
  if (/\/milestones\/[^/]+\/complete$/.test(path)) {
    return mutate(request, () => {
      const milestoneId = path.split('/').at(-2);
      project = { ...project, milestones: project.milestones.map(item => item.id === milestoneId ? { ...item, status: 'Completed', completedAt: new Date().toISOString() } : item) };
      record('ProjectMilestoneCompleted');
    });
  }
  if (path === `/api/projects/${project.id}/components` && method === 'POST') {
    if (staleNextComponent) return { stale: true };
    return mutate(request, () => {
      project = { ...project, components: project.components.concat({
        id: nextId('component'), name: body.name, description: body.description, archived: false
      }) };
      record('ProjectComponentCreated');
    });
  }
  if (/\/components\/[^/]+$/.test(path) && method === 'PUT') {
    return mutate(request, () => {
      const componentId = path.split('/').at(-1);
      project = { ...project, components: project.components.map(item => item.id === componentId ? { ...item, name: body.name, description: body.description } : item) };
      record('ProjectComponentUpdated');
    });
  }
  if (/\/components\/[^/]+$/.test(path) && method === 'DELETE') {
    return mutate(request, () => {
      const componentId = path.split('/').at(-1);
      project = { ...project, components: project.components.map(item => item.id === componentId ? { ...item, archived: true } : item) };
      record('ProjectComponentArchived');
    });
  }
  if (path === `/api/projects/${project.id}/templates` && method === 'POST') {
    return mutate(request, () => {
      const template = { id: nextId('template'), name: body.name, isDefault: body.isDefault, archived: false, defaultComponentNames: body.defaultComponentNames };
      project = {
        ...project,
        templates: project.templates.map(item => ({ ...item, isDefault: body.isDefault ? false : item.isDefault })).concat(template)
      };
      record('ProjectTemplateCreated');
    });
  }
  if (/\/templates\/[^/]+$/.test(path) && method === 'PUT') {
    return mutate(request, () => {
      const templateId = path.split('/').at(-1);
      project = {
        ...project,
        templates: project.templates.map(item => item.id === templateId
          ? { ...item, name: body.name, isDefault: body.isDefault, defaultComponentNames: body.defaultComponentNames }
          : { ...item, isDefault: body.isDefault ? false : item.isDefault })
      };
      record('ProjectTemplateUpdated');
    });
  }
  if (/\/templates\/[^/]+$/.test(path) && method === 'DELETE') {
    return mutate(request, () => {
      const templateId = path.split('/').at(-1);
      project = { ...project, templates: project.templates.map(item => item.id === templateId ? { ...item, archived: true } : item) };
      record('ProjectTemplateArchived');
    });
  }
  return [];
}

async function createContext(user, viewport) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(route.request().url());
    if (url.pathname === '/api/browser-auth/session') {
      return route.fulfill({ status: 200, contentType: 'application/json', body: envelope({ user, csrfToken: 'csrf' }) });
    }
    const data = routeData(url, route.request());
    if (data && data.stale) {
      staleNextComponent = false;
      project = {
        ...project,
        version: project.version + 1,
        components: project.components.concat({ id: nextId('component'), name: 'Dış değişiklik', description: 'Başka kullanıcı', archived: false })
      };
      return route.fulfill({ status: 409, contentType: 'application/json', body: errorEnvelope('CONCURRENCY_CONFLICT', 'Stale project version.') });
    }
    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (/WebSocket connection .*\/hubs\/work-items|Failed to start the connection|Failed to load resource/.test(detail)) return;
    failures.push(`${label}: ${detail}`);
  });
}

try {
  const ownerContext = await createContext(owner, { width: 1440, height: 1000 });
  const page = await ownerContext.newPage();
  diagnostics(page, 'desktop-owner');
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&view=catalog`, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: 'Sürümler, yayınlar ve proje kataloğu' }).waitFor();
  assert.equal(await page.getByRole('tab', { name: 'Teslimat', exact: true }).getAttribute('aria-selected'), 'true');
  checks.push('normal-navigation');

  await page.getByLabel('Sürüm adı').fill('3.2');
  await page.getByRole('button', { name: 'Sürüm oluştur' }).click();
  await page.getByLabel('Sürümler').getByText('3.2', { exact: true }).waitFor();
  await page.getByLabel('Planlanan sürüm').selectOption({ label: '3.2' });
  await page.getByLabel('Yayın adı').fill('Sürüm 3.2');
  await page.getByRole('button', { name: 'Taslak oluştur' }).click();
  await page.getByRole('button', { name: 'Onayla' }).click();
  await page.getByRole('button', { name: 'Yayınla' }).click();
  await page.getByText('Published', { exact: true }).waitFor();
  checks.push('version-release-lifecycle');
  await page.screenshot({ path: resolve(output, 'desktop-releases.png'), fullPage: true });

  await page.getByRole('tab', { name: 'Kilometre taşları' }).click();
  await page.getByRole('button', { name: 'Pilot kilometre taşını düzenle' }).click();
  await page.getByLabel('Ad', { exact: true }).fill('Pilot doğrulama');
  await page.getByRole('button', { name: 'Değişiklikleri kaydet' }).click();
  await page.getByRole('button', { name: 'Tamamla' }).click();
  await page.getByText('Completed', { exact: true }).waitFor();
  checks.push('milestone-lifecycle');

  await page.getByRole('tab', { name: 'Bileşenler' }).click();
  await page.getByLabel('Ad', { exact: true }).fill('Mobil');
  await page.getByLabel('Açıklama').fill('Mobil istemci');
  await page.getByRole('button', { name: 'Bileşen ekle' }).click();
  await page.getByText('Mobil', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Mobil bileşenini düzenle' }).click();
  await page.getByLabel('Ad', { exact: true }).fill('Mobil uygulama');
  await page.getByRole('button', { name: 'Değişiklikleri kaydet' }).click();
  await page.getByRole('button', { name: 'Mobil uygulama bileşenini arşivle' }).click();
  await page.getByRole('button', { name: 'Evet' }).click();
  await page.getByLabel('Sorumluluk alanları').getByText('Arşiv', { exact: true }).waitFor();
  checks.push('component-crud-confirmation');

  staleNextComponent = true;
  await page.getByLabel('Ad', { exact: true }).fill('Çakışan bileşen');
  await page.getByRole('button', { name: 'Bileşen ekle' }).click();
  await page.getByText(/güncel kayıt yeniden yüklendi/i).waitFor();
  await page.getByText('Dış değişiklik', { exact: true }).waitFor();
  assert.equal(await page.getByLabel('Ad', { exact: true }).inputValue(), '');
  checks.push('stale-conflict-authoritative-reload');

  await page.getByRole('tab', { name: 'Şablonlar' }).click();
  const fiftyOne = Array.from({ length: 51 }, (_, index) => `Bileşen ${index + 1}`).join('\n');
  await page.getByLabel('Varsayılan bileşen adları').fill(fiftyOne);
  assert.equal(await page.getByRole('button', { name: 'Şablon ekle' }).isDisabled(), true);
  await page.getByLabel('Varsayılan bileşen adları').fill('API\nWeb\nMobil uygulama');
  await page.getByLabel('Ad', { exact: true }).fill('Mobil teslimat');
  await page.getByRole('button', { name: 'Şablon ekle' }).click();
  await page.getByText('Mobil teslimat', { exact: true }).waitFor();
  checks.push('template-limit-and-create');

  await page.getByRole('tab', { name: 'Etkinlik' }).click();
  await page.getByText('ProjectReleasePublished', { exact: true }).waitFor();
  await page.getByText('ProjectTemplateCreated', { exact: true }).waitFor();
  checks.push('catalog-audit');

  const viewerContext = await createContext(viewer, { width: 1280, height: 900 });
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'desktop-viewer');
  await viewerPage.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&view=catalog`, { waitUntil: 'networkidle' });
  await viewerPage.getByText(/salt okunur gösteriliyor/i).waitFor();
  assert.equal(await viewerPage.getByRole('button', { name: 'Sürüm oluştur' }).count(), 0);
  assert.equal(await viewerPage.getByRole('button', { name: 'Yayınla' }).count(), 0);
  checks.push('viewer-read-only');
  await viewerPage.screenshot({ path: resolve(output, 'desktop-viewer.png'), fullPage: true });

  const mobileContext = await createContext(owner, { width: 390, height: 844 });
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-owner');
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/projects/${project.id}/catalog?tab=components`, { waitUntil: 'networkidle' });
  await mobilePage.getByRole('heading', { name: 'Teslimat kataloğu' }).waitFor();
  await mobilePage.getByRole('tab', { name: 'Bileşen' }).click();
  await mobilePage.getByText('Dış değişiklik', { exact: true }).waitFor();
  const size = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(size.scrollWidth <= size.width + 1, `Mobile catalog overflowed: ${size.scrollWidth}/${size.width}`);
  const tabsFit = await mobilePage.getByRole('tab').evaluateAll((tabs, width) => tabs.every(tab => {
    const bounds = tab.getBoundingClientRect();
    return bounds.left >= -1 && bounds.right <= width + 1;
  }), size.width);
  assert.equal(tabsFit, true, 'Mobile catalog tabs must remain fully visible without horizontal scrolling');
  checks.push('mobile-parity-no-overflow');
  await mobilePage.screenshot({ path: resolve(output, 'mobile-components.png'), fullPage: true });

  assert.deepEqual(failures, [], failures.join('\n'));
  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-001',
    passed: failures.length === 0 && checks.length === 9,
    checks,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 9, `Expected 9 checks, received ${checks.length}`);
console.log('V3-SURFACE-001 browser passed: full catalog lifecycle, limits, conflict reload, audit, Viewer and mobile parity.');
