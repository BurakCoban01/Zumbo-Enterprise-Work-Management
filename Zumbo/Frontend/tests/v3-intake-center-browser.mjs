import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-feature-001');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });

const owner = { id: 'owner-1', username: 'selin', email: 'selin@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const viewer = { id: 'viewer-1', username: 'mert', email: 'mert@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const project = {
  id: 'project-1',
  organizationId: 'org-1',
  key: 'OPS',
  name: 'Operasyon Merkezi',
  visibility: 'Internal',
  version: 3,
  members: [
    { userId: owner.id, role: 'ProjectOwner' },
    { userId: viewer.id, role: 'Viewer' }
  ],
  milestones: [],
  releases: [],
  templates: [],
  components: [],
  versions: [],
  teamIds: []
};
const board = { id: 'board-1', projectId: project.id, name: 'Talep panosu', type: 'Kanban', version: 2, columns: [], views: [] };
let nextId = 10;
let forms = [
  intakeForm({
    id: 'form-internal',
    name: 'Ekip destek talepleri',
    accessPolicy: 'Internal',
    state: 'Published',
    publishedVersion: 1,
    publicId: null,
    version: 2
  }),
  intakeForm({
    id: 'form-public',
    name: 'Müşteri geri bildirimi',
    accessPolicy: 'Public',
    state: 'Published',
    publishedVersion: 2,
    publicId: 'pub-feedback',
    version: 4
  })
];
let submissions = [];
const checks = [];
const failures = [];

function intakeForm({ id, name, accessPolicy, state, publishedVersion, publicId, version }) {
  return {
    id,
    projectId: project.id,
    name,
    description: 'Talebinizi doğru ekibe yönlendiren yapılandırılmış form.',
    state,
    publicId,
    publishedVersion,
    draft: {
      accessPolicy,
      boardId: board.id,
      workItemType: 'Task',
      defaultPriority: 'Medium',
      confirmationMessage: 'Talebiniz alındı ve ilgili ekibe yönlendirildi.',
      fields: [
        { key: 'baslik', label: 'Talep başlığı', type: 'Text', required: true, helpText: 'Kısa ve ayırt edici bir başlık yazın.', options: [] },
        { key: 'aciklama', label: 'Açıklama', type: 'LongText', required: false, helpText: null, options: [] }
      ],
      mapping: {
        titleFieldKey: 'baslik',
        descriptionFieldKey: 'aciklama',
        priorityFieldKey: null,
        dueDateFieldKey: null,
        customFields: []
      }
    },
    createdAt: '2026-07-24T08:00:00Z',
    updatedAt: '2026-07-24T08:00:00Z',
    publishedAt: state === 'Published' ? '2026-07-24T08:00:00Z' : null,
    version
  };
}

function published(form) {
  return {
    formId: form.id,
    version: form.publishedVersion,
    name: form.name,
    description: form.description,
    accessPolicy: form.draft.accessPolicy,
    confirmationMessage: form.draft.confirmationMessage,
    fields: form.draft.fields
  };
}

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-feature-001' });
}

function routeData(url, request) {
  const path = url.pathname;
  const method = request.method();
  if (path === '/api/projects' && method === 'GET') return [project];
  if (path === `/api/projects/${project.id}` && method === 'GET') return project;
  if (path === `/api/boards/by-project/${project.id}`) return [board];
  if (path === `/api/workflows/${project.id}`) return { projectId: project.id, statuses: [], transitions: [] };
  if (path === `/api/work-item-schemas/${project.id}`) {
    return { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
  }
  if (path === '/api/work-items/search') return { items: [], totalCount: 0, degraded: false };
  if (path.startsWith('/api/work-items/reports/')) return [];
  if (path === `/api/sprints/projects/${project.id}` || path === `/api/sprints/projects/${project.id}/backlog`) {
    return { items: [], totalCount: 0 };
  }
  if (path.startsWith('/api/audit/') || path === '/api/teams' || path === '/api/auth/users'
    || path.startsWith('/api/notifications')) return [];

  if (path === '/api/intake/forms' && method === 'GET') return forms;
  if (path === '/api/intake/forms' && method === 'POST') {
    const body = request.postDataJSON();
    const form = intakeForm({
      id: `form-${++nextId}`,
      name: body.name,
      accessPolicy: body.definition.accessPolicy,
      state: 'Draft',
      publishedVersion: 0,
      publicId: null,
      version: 1
    });
    form.description = body.description || '';
    form.draft = body.definition;
    forms = [form, ...forms];
    return form;
  }
  const formMatch = path.match(/^\/api\/intake\/forms\/([^/]+)$/);
  if (formMatch && method === 'PUT') {
    const body = request.postDataJSON();
    const form = forms.find(item => item.id === formMatch[1]);
    Object.assign(form, { name: body.name, description: body.description || '', draft: body.definition, version: form.version + 1 });
    return form;
  }
  const publishMatch = path.match(/^\/api\/intake\/forms\/([^/]+)\/publish$/);
  if (publishMatch) {
    const form = forms.find(item => item.id === publishMatch[1]);
    assert.equal(request.headers()['if-match'], `"${form.version}"`, 'Publish must carry the current form version');
    Object.assign(form, {
      state: 'Published',
      publishedVersion: form.publishedVersion + 1,
      publicId: form.draft.accessPolicy === 'Public' ? `pub-${form.id}` : null,
      publishedAt: new Date().toISOString(),
      version: form.version + 1
    });
    return form;
  }
  const archiveMatch = path.match(/^\/api\/intake\/forms\/([^/]+)\/archive$/);
  if (archiveMatch) {
    const form = forms.find(item => item.id === archiveMatch[1]);
    Object.assign(form, { state: 'Archived', version: form.version + 1 });
    return form;
  }
  const publishedMatch = path.match(/^\/api\/intake\/forms\/([^/]+)\/published$/);
  if (publishedMatch) return published(forms.find(item => item.id === publishedMatch[1]));
  const queueMatch = path.match(/^\/api\/intake\/forms\/([^/]+)\/submissions$/);
  if (queueMatch && method === 'GET') {
    const state = url.searchParams.get('state');
    const items = submissions.filter(item => item.formId === queueMatch[1] && (!state || item.state === state));
    return { items, page: 1, pageSize: 100, totalCount: items.length };
  }
  if (queueMatch && method === 'POST') {
    const form = forms.find(item => item.id === queueMatch[1]);
    const submission = {
      id: `submission-${++nextId}`,
      formId: form.id,
      formVersion: form.publishedVersion,
      projectId: project.id,
      state: 'New',
      confirmationCode: `ZMB-${nextId}`,
      workItemId: `work-${nextId}`,
      values: [{ fieldKey: 'baslik', value: 'VPN erişim talebi' }],
      attachments: [],
      triageNote: null,
      triagedByUserId: null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      version: 1
    };
    submissions = [submission, ...submissions];
    return {
      submissionId: submission.id,
      confirmationCode: submission.confirmationCode,
      message: form.draft.confirmationMessage,
      state: submission.state,
      workItemId: submission.workItemId
    };
  }
  const triageMatch = path.match(/^\/api\/intake\/forms\/([^/]+)\/submissions\/([^/]+)\/triage$/);
  if (triageMatch) {
    const body = request.postDataJSON();
    const submission = submissions.find(item => item.id === triageMatch[2]);
    Object.assign(submission, {
      state: body.state,
      triageNote: body.note,
      triagedByUserId: owner.id,
      updatedAt: new Date().toISOString(),
      version: submission.version + 1
    });
    return submission;
  }
  const publicMatch = path.match(/^\/api\/intake\/public\/forms\/([^/]+)$/);
  if (publicMatch) {
    const form = forms.find(item => item.publicId === publicMatch[1]);
    return published(form);
  }
  const publicSubmit = path.match(/^\/api\/intake\/public\/forms\/([^/]+)\/submissions$/);
  if (publicSubmit) {
    const form = forms.find(item => item.publicId === publicSubmit[1]);
    return {
      submissionId: `submission-${++nextId}`,
      confirmationCode: `ZMB-PUBLIC-${nextId}`,
      message: form.draft.confirmationMessage,
      state: 'New',
      workItemId: null
    };
  }
  return [];
}

async function createContext(user, viewport) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce', timezoneId: 'Europe/Istanbul' });
  if (user) {
    await context.addInitScript(auth => {
      localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
      sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
    }, { user, csrfToken: 'csrf' });
  }
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    if (url.pathname === '/api/browser-auth/session') {
      if (!user) {
        return route.fulfill({
          status: 401,
          contentType: 'application/json',
          body: JSON.stringify({ success: false, data: null, error: { code: 'AUTHENTICATION_REQUIRED', message: 'Authentication required.' } })
        });
      }
      return route.fulfill({ status: 200, contentType: 'application/json', body: envelope({ user, csrfToken: 'csrf' }) });
    }
    const data = routeData(url, request);
    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (/WebSocket connection|Failed to start the connection|Failed to load resource/.test(detail)) return;
    failures.push(`${label}: ${detail}`);
  });
}

try {
  const ownerContext = await createContext(owner, { width: 1440, height: 1000 });
  const ownerPage = await ownerContext.newPage();
  diagnostics(ownerPage, 'desktop-owner');
  await ownerPage.goto(
    `${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&view=intake`,
    { waitUntil: 'networkidle' }
  );
  await ownerPage.getByRole('heading', { name: 'Intake ve triage merkezi' }).waitFor();
  assert.equal(await ownerPage.getByRole('tab', { name: 'Intake', exact: true }).getAttribute('aria-selected'), 'true');
  checks.push('desktop-deep-link');

  await ownerPage.getByRole('button', { name: 'Yeni form' }).click();
  await ownerPage.getByLabel('Form adı').fill('BT erişim talepleri');
  await ownerPage.getByRole('button', { name: 'Form oluştur' }).click();
  await ownerPage.locator('.intake-feedback').getByText('Form taslağı oluşturuldu.').waitFor();
  await ownerPage.getByRole('button', { name: 'Yayınla', exact: true }).click();
  await ownerPage.locator('.intake-feedback').getByText('Formun yeni sürümü yayınlandı.').waitFor();
  checks.push('desktop-create-publish');

  await ownerPage.getByRole('tab', { name: 'Talep oluştur' }).click();
  await ownerPage.getByLabel('Talep başlığı').fill('VPN erişim talebi');
  await ownerPage.getByLabel('Açıklama').fill('Saha ekibi için süreli erişim gerekiyor.');
  await ownerPage.getByRole('button', { name: 'Talebi gönder' }).click();
  await ownerPage.getByText('Talep iş kaydına dönüştürüldü').waitFor();
  checks.push('desktop-internal-submit');

  await ownerPage.getByRole('tab', { name: 'Triage' }).click();
  await ownerPage.getByText('VPN erişim talebi', { exact: true }).waitFor();
  await ownerPage.getByRole('button', { name: 'İncelemede' }).click();
  await ownerPage.locator('.intake-feedback').getByText('Talep durumu güncellendi.').waitFor();
  checks.push('desktop-triage');
  await ownerPage.screenshot({ path: resolve(output, 'desktop-triage.png'), fullPage: true });

  const viewerContext = await createContext(viewer, { width: 1280, height: 900 });
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'desktop-viewer');
  await viewerPage.goto(
    `${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&view=intake`,
    { waitUntil: 'networkidle' }
  );
  await viewerPage.getByText(/formlar salt okunur/i).waitFor();
  assert.equal(await viewerPage.getByRole('button', { name: 'Yeni form' }).count(), 0);
  checks.push('desktop-viewer-read-only');

  const publicContext = await createContext(null, { width: 1024, height: 900 });
  const publicPage = await publicContext.newPage();
  diagnostics(publicPage, 'desktop-public');
  await publicPage.goto(
    `${server.origin}/desktop-bulma/index.html#public=pub-feedback`,
    { waitUntil: 'networkidle' }
  );
  await publicPage.getByRole('heading', { name: 'Müşteri geri bildirimi' }).waitFor();
  await publicPage.getByLabel('Talep başlığı').fill('Rapor geri bildirimi');
  await publicPage.getByRole('button', { name: 'Talebi gönder' }).click();
  await publicPage.getByText('Talebiniz alındı', { exact: true }).waitFor();
  assert.equal(await publicPage.getByText(/work-/).count(), 0, 'Public confirmation must not expose a work-item id');
  checks.push('desktop-public-anonymous');
  await publicPage.screenshot({ path: resolve(output, 'desktop-public-confirmation.png'), fullPage: true });

  const mobileContext = await createContext(owner, { width: 390, height: 844 });
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-owner');
  await mobilePage.goto(
    `${server.origin}/mobile-ionic/index.html#/projects/${project.id}/intake?tab=forms`,
    { waitUntil: 'networkidle' }
  );
  await mobilePage.getByRole('heading', { name: 'Intake ve triage' }).waitFor();
  const mobileSize = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(mobileSize.scrollWidth <= mobileSize.width + 1, `Mobile intake overflowed: ${mobileSize.scrollWidth}/${mobileSize.width}`);
  checks.push('mobile-project-parity');
  await mobilePage.screenshot({ path: resolve(output, 'mobile-forms.png'), fullPage: true });

  const mobilePublicContext = await createContext(null, { width: 360, height: 800 });
  const mobilePublicPage = await mobilePublicContext.newPage();
  diagnostics(mobilePublicPage, 'mobile-public');
  await mobilePublicPage.goto(
    `${server.origin}/mobile-ionic/index.html#/intake/pub-feedback`,
    { waitUntil: 'networkidle' }
  );
  await mobilePublicPage.getByRole('heading', { name: 'Müşteri geri bildirimi' }).waitFor();
  const publicSize = await mobilePublicPage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(publicSize.scrollWidth <= publicSize.width + 1);
  checks.push('mobile-public-anonymous');

  assert.deepEqual(failures, [], failures.join('\n'));
  await mobilePublicContext.close();
  await mobileContext.close();
  await publicContext.close();
  await viewerContext.close();
  await ownerContext.close();
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-001',
    passed: failures.length === 0 && checks.length === 8,
    checks,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 8, `Expected 8 checks, received ${checks.length}`);
console.log('V3-FEATURE-001 browser passed: desktop lifecycle, triage, Viewer, public intake and mobile parity.');
