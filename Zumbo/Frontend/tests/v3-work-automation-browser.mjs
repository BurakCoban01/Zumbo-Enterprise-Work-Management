import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-surface-002');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const now = Date.now();
const owner = { id: 'owner-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const viewer = { id: 'viewer-1', username: 'deniz', email: 'deniz@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const project = {
  id: 'project-1',
  organizationId: 'org-1',
  key: 'AUT',
  name: 'Operasyon Akışı',
  visibility: 'Private',
  version: 3,
  members: [{ userId: owner.id, role: 'ProjectOwner' }, { userId: viewer.id, role: 'Viewer' }],
  teamIds: [],
  templates: [],
  components: [],
  versions: [],
  releases: [],
  milestones: [],
  archived: false
};
const board = {
  id: 'board-1',
  projectId: project.id,
  name: 'Operasyon Panosu',
  type: 'Kanban',
  swimlaneMode: 'None',
  version: 1,
  views: [],
  columns: [{ id: 'todo', name: 'To Do', category: 'Todo', position: 0 }]
};
let templates = [{
  id: 'template-1',
  projectId: project.id,
  boardId: board.id,
  name: 'Haftalık kontrol',
  title: 'Teslimat risklerini incele',
  description: 'Açık riskleri gözden geçir.',
  type: 'Task',
  priority: 'Medium',
  assigneeUserId: owner.id,
  teamId: null,
  dueAfterDays: 1,
  labels: ['operasyon'],
  issueTypeSchemaVersion: 1,
  customFields: [],
  archived: false,
  version: 2
}];
let recurrences = [{
  id: 'recurrence-1',
  projectId: project.id,
  templateId: 'template-1',
  frequency: 'Weekly',
  interval: 1,
  startAtUtc: new Date(now + 86400000).toISOString(),
  endAtUtc: null,
  nextRunAtUtc: new Date(now + 86400000).toISOString(),
  maxOccurrences: 12,
  scheduledOccurrences: 1,
  generatedOccurrences: 1,
  active: true,
  archived: false,
  version: 2
}];
const occurrences = {
  'recurrence-1': [{
    id: 'occurrence-1',
    scheduledForUtc: new Date(now - 86400000).toISOString(),
    status: 'Generated',
    createdWorkItemId: 'work-1',
    generatedAt: new Date(now - 86000000).toISOString(),
    version: 1
  }]
};
const audits = {
  'WorkItemTemplate:template-1': [{ id: 'audit-template-1', action: 'WorkItemTemplateCreated', actorUserId: owner.id, createdAt: new Date(now - 7200000).toISOString() }],
  'WorkItemRecurrence:recurrence-1': [{ id: 'audit-recurrence-1', action: 'WorkItemRecurrenceCreated', actorUserId: owner.id, createdAt: new Date(now - 3600000).toISOString() }]
};
let staleNextState = false;
let sequence = 10;
const checks = [];
const failures = [];

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-surface-002' });
}

function errorEnvelope(code, message) {
  return JSON.stringify({ success: false, data: null, error: { code, message }, correlationId: 'v3-surface-002' });
}

function nextId(prefix) {
  sequence += 1;
  return `${prefix}-${sequence}`;
}

function rememberAudit(type, id, action) {
  const key = `${type}:${id}`;
  audits[key] ||= [];
  audits[key].unshift({ id: nextId('audit'), action, actorUserId: owner.id, createdAt: new Date().toISOString() });
}

function assertVersion(request, entity, action) {
  assert.equal(request.headers()['if-match'], `"${entity.version}"`, `${action} did not carry current If-Match`);
}

function baseData(url, request) {
  const path = url.pathname;
  const method = request.method();
  if (path === '/api/projects' && method === 'GET') return [project];
  if (path === `/api/projects/${project.id}` && method === 'GET') return project;
  if (path === `/api/boards/by-project/${project.id}`) return url.searchParams.get('archived') === 'true' ? [] : [board];
  if (path === `/api/workflows/${project.id}`) return { projectId: project.id, statuses: [], transitions: [] };
  if (path === `/api/work-item-schemas/${project.id}`) return { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
  if (path === '/api/work-items/search') return { items: [], totalCount: 0, degraded: false };
  if (path.startsWith('/api/work-items/reports/')) return { total: 0, inProgress: 0, done: 0, overdue: 0 };
  if (path === `/api/sprints/projects/${project.id}` || path === `/api/sprints/projects/${project.id}/backlog`) return { items: [], totalCount: 0 };
  if (path === '/api/teams' || path === '/api/auth/users' || path.startsWith('/api/notifications')) return [];
  if (path === `/api/audit/entity/Project/${project.id}`) return [];
  return undefined;
}

function automationData(url, request) {
  const path = url.pathname;
  const method = request.method();
  const body = request.postData() ? request.postDataJSON() : {};
  if (path === '/api/work-items/templates' && method === 'GET') {
    return { items: templates, page: 1, pageSize: 100, totalCount: templates.length };
  }
  if (path === '/api/work-items/recurrences' && method === 'GET') {
    return { items: recurrences, page: 1, pageSize: 100, totalCount: recurrences.length };
  }
  const occurrenceMatch = path.match(/^\/api\/work-items\/recurrences\/([^/]+)\/occurrences$/);
  if (occurrenceMatch && method === 'GET') {
    const items = occurrences[occurrenceMatch[1]] || [];
    return { items, page: 1, pageSize: 50, totalCount: items.length };
  }
  const auditMatch = path.match(/^\/api\/audit\/entity\/(WorkItemTemplate|WorkItemRecurrence)\/([^/]+)$/);
  if (auditMatch) return audits[`${auditMatch[1]}:${auditMatch[2]}`] || [];
  if (path === '/api/work-items/recurrences/preview' && method === 'POST') {
    const start = new Date(body.startAtUtc);
    const step = body.frequency === 'Monthly' ? null : (body.frequency === 'Weekly' ? 7 : 1) * body.interval;
    const dates = Array.from({ length: Math.min(body.previewCount, body.maxOccurrences) }, (_, index) => {
      const value = new Date(start);
      if (step === null) value.setUTCMonth(value.getUTCMonth() + index * body.interval);
      else value.setUTCDate(value.getUTCDate() + index * step);
      return value.toISOString();
    });
    return { frequency: body.frequency, interval: body.interval, startAtUtc: body.startAtUtc, endAtUtc: body.endAtUtc, maxOccurrences: body.maxOccurrences, occurrencesUtc: dates };
  }
  if (path === '/api/work-items/templates' && method === 'POST') {
    const created = {
      id: nextId('template'),
      projectId: project.id,
      issueTypeSchemaVersion: 1,
      archived: false,
      version: 1,
      ...body
    };
    templates = templates.concat(created);
    rememberAudit('WorkItemTemplate', created.id, 'WorkItemTemplateCreated');
    return created;
  }
  const templateMatch = path.match(/^\/api\/work-items\/templates\/([^/]+)$/);
  if (templateMatch && method === 'PUT') {
    const current = templates.find(item => item.id === templateMatch[1]);
    assertVersion(request, current, 'template update');
    const updated = { ...current, ...body, version: current.version + 1 };
    templates = templates.map(item => item.id === updated.id ? updated : item);
    rememberAudit('WorkItemTemplate', updated.id, 'WorkItemTemplateUpdated');
    return updated;
  }
  if (templateMatch && method === 'DELETE') {
    const current = templates.find(item => item.id === templateMatch[1]);
    assertVersion(request, current, 'template archive');
    templates = templates.map(item => item.id === current.id ? { ...item, archived: true, version: item.version + 1 } : item);
    rememberAudit('WorkItemTemplate', current.id, 'WorkItemTemplateArchived');
    return null;
  }
  if (path === '/api/work-items/recurrences' && method === 'POST') {
    const created = {
      id: nextId('recurrence'),
      projectId: project.id,
      generatedOccurrences: 0,
      scheduledOccurrences: 1,
      nextRunAtUtc: body.startAtUtc,
      active: true,
      archived: false,
      version: 1,
      ...body
    };
    recurrences = [created].concat(recurrences);
    occurrences[created.id] = [{
      id: nextId('occurrence'),
      scheduledForUtc: body.startAtUtc,
      status: 'Failed',
      createdWorkItemId: null,
      generatedAt: null,
      version: 1
    }];
    rememberAudit('WorkItemRecurrence', created.id, 'WorkItemRecurrenceCreated');
    return created;
  }
  const stateMatch = path.match(/^\/api\/work-items\/recurrences\/([^/]+)\/state$/);
  if (stateMatch && method === 'PATCH') {
    const current = recurrences.find(item => item.id === stateMatch[1]);
    assertVersion(request, current, 'recurrence state change');
    if (staleNextState) {
      staleNextState = false;
      recurrences = recurrences.map(item => item.id === current.id
        ? { ...item, active: false, version: item.version + 1 }
        : item);
      return { conflict: true };
    }
    const updated = { ...current, active: body.active, version: current.version + 1 };
    recurrences = recurrences.map(item => item.id === updated.id ? updated : item);
    rememberAudit('WorkItemRecurrence', updated.id, body.active ? 'WorkItemRecurrenceResumed' : 'WorkItemRecurrencePaused');
    return updated;
  }
  const recurrenceMatch = path.match(/^\/api\/work-items\/recurrences\/([^/]+)$/);
  if (recurrenceMatch && method === 'DELETE') {
    const current = recurrences.find(item => item.id === recurrenceMatch[1]);
    assertVersion(request, current, 'recurrence archive');
    recurrences = recurrences.map(item => item.id === current.id
      ? { ...item, active: false, archived: true, version: item.version + 1 }
      : item);
    rememberAudit('WorkItemRecurrence', current.id, 'WorkItemRecurrenceArchived');
    return null;
  }
  return undefined;
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
    const automation = automationData(url, route.request());
    if (automation && automation.conflict) {
      return route.fulfill({ status: 409, contentType: 'application/json', body: errorEnvelope('CONCURRENCY_CONFLICT', 'The recurrence changed concurrently.') });
    }
    const data = automation === undefined ? baseData(url, route.request()) : automation;
    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data === undefined ? [] : data) });
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
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&view=automation`, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: 'Yinelenen işler ve iş şablonları' }).waitFor();
  assert.equal(await page.getByRole('tab', { name: 'Otomasyon', exact: true }).getAttribute('aria-selected'), 'true');
  checks.push('normal-navigation-timezone');

  await page.getByRole('tab', { name: 'İş şablonları' }).click();
  await page.getByLabel('Şablon adı').fill('Günlük triage');
  await page.getByLabel('Üretilecek iş başlığı').fill('Triage kuyruğunu incele');
  await page.getByLabel(/Etiketler/).fill(Array.from({ length: 51 }, (_, index) => `etiket-${index + 1}`).join(','));
  assert.equal(await page.getByRole('button', { name: 'Şablon oluştur' }).isDisabled(), true);
  await page.getByLabel(/Etiketler/).fill('operasyon, triage');
  await page.getByRole('button', { name: 'Şablon oluştur' }).click();
  await page.getByText('Günlük triage', { exact: true }).waitFor();
  checks.push('template-create-explicit-limits');

  const createdTemplate = templates.find(item => item.name === 'Günlük triage');
  await page.getByRole('button', { name: 'Günlük triage şablonuyla yineleme oluştur' }).click();
  await page.getByLabel('Sıklık').selectOption('Daily');
  await page.getByLabel('Aralık').fill('1');
  await page.getByRole('button', { name: 'Takvimi önizle' }).click();
  await page.getByText('Sunucu takvim önizlemesi', { exact: true }).waitFor();
  assert.equal(await page.locator('.automation-preview li').count(), 5);
  checks.push('authoritative-schedule-preview');
  assert.equal(createdTemplate.title, 'Triage kuyruğunu incele');

  await page.getByRole('button', { name: 'Etkinleştir' }).click();
  await page.waitForTimeout(250);
  assert.equal(recurrences.length, 2, 'Recurrence create request did not reach the mock API');
  const createdRecurrence = recurrences[0];
  const createdRow = page.locator('.automation-row').filter({ hasText: 'Günlük triage' }).first();
  assert.ok(await page.locator('.automation-row').count() >= 2, 'Created recurrence did not render in the register');
  await createdRow.locator('.automation-row-main').click();
  await page.getByText('Başarısız', { exact: true }).waitFor();
  checks.push('recurrence-create-failure-state');

  staleNextState = true;
  await createdRow.getByRole('button', { name: 'Yinelemeyi duraklat' }).click();
  await createdRow.getByText('Duraklatıldı', { exact: true }).waitFor();
  assert.equal(recurrences.find(item => item.id === createdRecurrence.id).active, false);
  checks.push('stale-authoritative-reload');

  await createdRow.getByRole('button', { name: 'Yinelemeyi devam ettir' }).click();
  await createdRow.getByText('Etkin', { exact: true }).waitFor();
  checks.push('pause-resume-current-version');

  await page.getByRole('tab', { name: 'Çalıştırma geçmişi' }).click();
  await page.getByText('WorkItemRecurrenceResumed', { exact: true }).waitFor();
  checks.push('occurrence-and-audit');
  await page.screenshot({ path: resolve(output, 'desktop-owner.png'), fullPage: true });

  const viewerContext = await createContext(viewer, { width: 1280, height: 900 });
  const viewerPage = await viewerContext.newPage();
  diagnostics(viewerPage, 'desktop-viewer');
  await viewerPage.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=${project.id}&view=automation`, { waitUntil: 'networkidle' });
  await viewerPage.getByText(/salt okunur gösteriliyor/i).waitFor();
  assert.equal(await viewerPage.getByRole('button', { name: 'Etkinleştir' }).count(), 0);
  assert.equal(await viewerPage.getByRole('button', { name: /duraklat/i }).count(), 0);
  checks.push('viewer-read-only');
  await viewerPage.screenshot({ path: resolve(output, 'desktop-viewer.png'), fullPage: true });

  const mobileContext = await createContext(owner, { width: 390, height: 844 });
  const mobilePage = await mobileContext.newPage();
  diagnostics(mobilePage, 'mobile-owner');
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/projects/${project.id}/automation?tab=schedules`, { waitUntil: 'networkidle' });
  await mobilePage.getByRole('heading', { name: 'İş otomasyonu' }).waitFor();
  await mobilePage.locator('.mobile-automation-row').filter({ hasText: 'Günlük triage' }).first().waitFor();
  const dimensions = await mobilePage.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile automation overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  const tabsFit = await mobilePage.locator('.mobile-automation-tabs [role="tab"]').evaluateAll((tabs, width) => tabs.every(tab => {
    const bounds = tab.getBoundingClientRect();
    return bounds.left >= -1 && bounds.right <= width + 1;
  }), dimensions.width);
  assert.equal(tabsFit, true);
  checks.push('mobile-parity-no-overflow');
  await mobilePage.screenshot({ path: resolve(output, 'mobile-owner.png'), fullPage: true });

  assert.deepEqual(failures, [], failures.join('\n'));
  await mobileContext.close();
  await viewerContext.close();
  await ownerContext.close();
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-SURFACE-002',
    passed: failures.length === 0 && checks.length === 9,
    checks,
    failures
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 9, `Expected 9 checks, received ${checks.length}`);
console.log('V3-SURFACE-002 browser passed: templates, preview, lifecycle, conflict, audit, failure, Viewer and mobile parity.');
