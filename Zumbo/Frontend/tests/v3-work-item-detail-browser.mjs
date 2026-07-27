import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-ux-006');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });

const owner = { id: 'user-1', username: 'ada', email: 'ada@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const teammate = { id: 'user-2', username: 'mert', email: 'mert@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const board = {
  id: 'board-1', projectId: 'project-1', name: 'Teslimat Panosu', type: 'Kanban', swimlaneMode: 'None', views: [],
  columns: [
    { id: 'todo', name: 'To Do', category: 'Todo', position: 0, wipLimit: null },
    { id: 'doing', name: 'In Progress', category: 'InProgress', position: 1, wipLimit: null }
  ]
};

function workItem(id, title) {
  return {
    id, projectId: 'project-1', boardId: board.id, columnId: 'todo', title,
    description: '<img src=x onerror=alert(1)>\nKimlik akışını uçtan uca doğrula.', acceptanceCriteria: '<script>alert(2)</script> Tamamlanmış olmalı.',
    type: 'Task', priority: 'High', status: 'To Do', assigneeUserId: owner.id, teamId: 'team-1',
    dueDate: '2026-08-05T00:00:00Z', sprintId: 'sprint-1', estimatePoints: 5, parentId: null,
    labels: ['güvenlik', 'pilot'], checklist: [{ id: 'check-1', text: 'Yetki matrisini doğrula', completed: false }],
    relations: [{ relatedWorkItemId: 'task-2', relatedWorkItemKey: 'PLT-2', relationType: 'RelatesTo' }],
    customFields: [{ fieldKey: 'risk', type: 'Select', optionKey: 'high' }], version: 7
  };
}

function createFixture(role = 'ProjectOwner') {
  const task = workItem('task-1', 'Kimlik akışını doğrula');
  const related = { ...workItem('task-2', 'Oturum yenilemeyi izle'), description: '', acceptanceCriteria: '', relations: [], customFields: [] };
  const project = {
    id: 'project-1', organizationId: 'org-1', key: 'PLT', name: 'Platform Teslimatı', visibility: 'Private',
    members: [{ userId: owner.id, role }, { userId: teammate.id, role: 'Developer' }], teamIds: ['team-1'],
    components: [{ id: 'component-1', name: 'Kimlik', description: 'Oturum ve erişim', archived: false }],
    versions: [{ id: 'version-1', name: '1.4', status: 'Planned', archived: false }],
    releases: [{ id: 'release-1', name: 'Pilot yayını', status: 'Approved' }],
    milestones: [{ id: 'milestone-1', name: 'Pilot çıkışı', status: 'Open' }]
  };
  const comments = Array.from({ length: 61 }, (_, index) => ({
    id: `comment-${index + 1}`, body: index === 0 ? '<b>Güvenli metin</b>' : `İnceleme yorumu ${index + 1}`,
    authorUserId: index % 2 ? teammate.id : owner.id, mentions: [], createdAt: new Date(2026, 6, 23, 10, index % 60).toISOString()
  }));
  const activity = Array.from({ length: 61 }, (_, index) => ({
    id: `activity-${index + 1}`, type: index % 2 ? 'WorkItemUpdated' : 'WorkItemCommentAdded',
    actorUserId: index % 2 ? teammate.id : owner.id, detail: `Etkinlik ${index + 1}`, createdAt: new Date(2026, 6, 23, 11, index % 60).toISOString()
  }));
  return {
    role, project, tasks: [task, related], comments, activity,
    attachments: [{ id: 'attachment-1', fileName: 'tehdit-modeli.txt', contentType: 'text/plain', sizeBytes: 42 }],
    worklogs: [{ id: 'worklog-1', userId: teammate.id, hours: 1.5, note: 'Yetki akışı incelendi', createdAt: '2026-07-23T09:00:00Z' }],
    approvals: [{ id: 'approval-1', fromStatus: 'To Do', toStatus: 'In Progress', status: 'Pending', requestedByUserId: teammate.id, requestedAt: '2026-07-23T09:30:00Z' }],
    timeline: [{ id: 'timeline-1', fromStatus: null, toStatus: 'To Do', userId: owner.id, createdAt: '2026-07-23T08:00:00Z' }],
    collaboration: { workItemId: task.id, watcherCount: 2, voteCount: 1, watching: false, voted: false, version: 4 },
    conflictOnce: true, commentPosts: [], uploadCount: 0
  };
}

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-006' });
}

function errorEnvelope(code, message) {
  return JSON.stringify({ success: false, data: null, error: { code, message }, correlationId: 'v3-ux-006' });
}

function paged(items, url) {
  const page = Number(url.searchParams.get('page') || 1);
  const pageSize = Number(url.searchParams.get('pageSize') || 50);
  return { items: items.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: items.length };
}

async function createContext(viewport, role = 'ProjectOwner') {
  const fixture = createFixture(role);
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user: owner, csrfToken: 'csrf' });
  await context.route(`${apiBaseUrl}/**`, route => handleApi(route, fixture));
  return { context, fixture };
}

async function handleApi(route, fixture) {
  const request = route.request();
  if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
  const url = new URL(request.url());
  const path = url.pathname;
  const method = request.method();
  let body = null;
  if (request.postData() && !String(request.headers()['content-type']).includes('multipart/form-data')) {
    try { body = request.postDataJSON(); } catch { body = null; }
  }
  const stream = path.match(/^\/api\/work-items\/task-1\/(comments|attachments|worklogs|approvals|timeline|activity)$/);

  if (path === '/api/work-items/task-1' && method === 'PUT') {
    if (fixture.conflictOnce) {
      fixture.conflictOnce = false;
      fixture.tasks[0].title = 'Sunucudaki güncel başlık';
      fixture.tasks[0].version += 1;
      return json(route, 409, errorEnvelope('CONCURRENCY_CONFLICT', 'Work item version changed.'));
    }
    Object.assign(fixture.tasks[0], body, { version: fixture.tasks[0].version + 1 });
    return json(route, 200, envelope(fixture.tasks[0]));
  }
  if (path === '/api/work-items/task-1/comments' && method === 'POST') {
    fixture.commentPosts.push(body);
    fixture.comments.unshift({ id: `comment-new-${fixture.commentPosts.length}`, body: body.body, authorUserId: owner.id, mentions: body.mentions, createdAt: new Date().toISOString() });
    return json(route, 200, envelope(fixture.tasks[0]));
  }
  if (path === '/api/work-items/task-1/attachments/upload' && method === 'POST') {
    fixture.uploadCount += 1;
    fixture.attachments.unshift({ id: `attachment-new-${fixture.uploadCount}`, fileName: 'kanıt.txt', contentType: 'text/plain', sizeBytes: 5 });
    return json(route, 201, envelope(fixture.attachments[0]));
  }
  if (path === '/api/work-items/task-1/watch' && method === 'PUT') {
    const next = !!body.watching;
    fixture.collaboration.watcherCount += next === fixture.collaboration.watching ? 0 : (next ? 1 : -1);
    fixture.collaboration.watching = next;
    fixture.collaboration.version += 1;
    return json(route, 200, envelope(fixture.collaboration));
  }
  if (path === '/api/work-items/task-1/vote' && method === 'PUT') {
    const next = !!body.voted;
    fixture.collaboration.voteCount += next === fixture.collaboration.voted ? 0 : (next ? 1 : -1);
    fixture.collaboration.voted = next;
    fixture.collaboration.version += 1;
    return json(route, 200, envelope(fixture.collaboration));
  }
  if (stream && method === 'GET') {
    const values = stream[1] === 'comments' ? fixture.comments : fixture[stream[1]];
    return json(route, 200, envelope(paged(values, url)));
  }

  let data;
  if (path === '/api/browser-auth/session') data = { user: owner, csrfToken: 'csrf' };
  else if (path === '/api/projects') data = [fixture.project];
  else if (path === '/api/projects/project-1') data = fixture.project;
  else if (path === '/api/boards/by-project/project-1') data = [board];
  else if (path === '/api/work-items/search' && method === 'POST') data = { items: fixture.tasks, totalCount: fixture.tasks.length, degraded: false };
  else if (path === '/api/work-items/task-1') data = fixture.tasks[0];
  else if (path === '/api/work-items/task-2') data = fixture.tasks[1];
  else if (path === '/api/work-items/task-1/collaboration') data = fixture.collaboration;
  else if (path === '/api/work-items/reports/project-summary/project-1') data = { total: 2, inProgress: 0, done: 0, overdue: 0 };
  else if (path.startsWith('/api/work-items/reports/')) data = [];
  else if (path === '/api/workflows/project-1') data = {
    projectId: 'project-1', statuses: board.columns.map(column => ({ name: column.name, category: column.category })),
    transitions: [{ fromStatus: 'To Do', toStatus: 'In Progress', requiresApproval: true }]
  };
  else if (path === '/api/work-item-schemas/project-1') data = {
    issueTypes: [{ key: 'Task', name: 'Görev', active: true }],
    customFields: [{ key: 'risk', name: 'Risk', type: 'Select', options: [{ key: 'high', name: 'Yüksek' }] }],
    layouts: [{ issueTypeKey: 'Task', fieldKeys: ['risk'] }]
  };
  else if (path === '/api/sprints/projects/project-1') data = { items: [{ id: 'sprint-1', name: 'Sprint 14', status: 'Active' }], nextCursor: null };
  else if (path === '/api/teams') data = [{ id: 'team-1', name: 'Platform Ekibi' }];
  else if (path === '/api/auth/users') data = [owner, teammate];
  else if (path.startsWith('/api/audit/entity/')) data = [];
  else if (path === '/api/notifications' || path === `/api/notifications/${owner.id}`) data = [];
  else if (method !== 'GET') data = fixture.tasks[0];
  else data = [];
  return json(route, 200, envelope(data));
}

function json(route, status, body) {
  return route.fulfill({ status, contentType: 'application/json', body });
}

function watchConsole(page) {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const value = message.text();
    if (/WebSocket connection .*\/hubs\/work-items|Failed to start the connection/.test(value)) return;
    if (/Failed to load resource: the server responded with a status of 409/.test(value)) return;
    errors.push(value);
  });
  return errors;
}

async function openDesktopTask(page) {
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=board&project=project-1&board=board-1&view=list`, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: 'Kimlik akışını doğrula', exact: true }).click();
  await page.locator('.inspector[data-detail-mode="drawer"] #task-detail-title').waitFor();
}

try {
  const desktop = await createContext({ width: 1440, height: 1000 });
  const page = await desktop.context.newPage();
  const desktopErrors = watchConsole(page);
  await openDesktopTask(page);
  assert.match(await page.getByLabel('Görev açıklaması ve kabul ölçütleri').inputValue(), /<img src=x onerror=alert\(1\)>/);
  assert.match(await page.locator('.task-section-heading', { hasText: 'Dosyalar' }).innerText(), /1 \/ 1/);

  await page.getByRole('button', { name: 'Görev takibini değiştir' }).click();
  assert.equal(desktop.fixture.collaboration.watching, true);
  assert.equal(desktop.fixture.collaboration.watcherCount, 3);
  await page.getByRole('button', { name: 'Görevi tam sayfada aç' }).click();
  await page.waitForTimeout(300);
  const detailMode = await page.locator('.inspector').evaluate(element => ({
    className: element.className, mode: element.getAttribute('data-detail-mode')
  }));
  assert.equal(detailMode.mode, 'page', `Detail mode did not change: ${JSON.stringify(detailMode)} ${page.url()}`);
  await page.locator('.inspector[data-detail-mode="page"]').waitFor();
  assert.match(page.url(), /detail=page/);
  await page.screenshot({ path: resolve(output, 'desktop-owner-full-page.png'), fullPage: true });

  await page.locator('#task-title').fill('Yerel taslak başlık');
  await page.getByRole('button', { name: 'Ayrıntıları kaydet' }).click();
  await page.getByText('Yerel form değişiklikleriniz korunuyor.', { exact: false }).waitFor();
  assert.equal(await page.locator('#task-title').inputValue(), 'Yerel taslak başlık');

  await page.getByLabel('Yorumda bahsedilecek kişi').selectOption({ label: teammate.username });
  await page.getByRole('button', { name: 'Kişiyi yoruma ekle' }).click();
  await page.locator('#task-comment').fill('Mert, lütfen son akışı doğrula.');
  await page.getByRole('button', { name: 'Yorum gönder' }).click();
  assert.deepEqual(desktop.fixture.commentPosts.at(-1).mentions, [teammate.id]);

  const fileInput = page.locator('.task-upload input[type="file"]');
  await fileInput.setInputFiles({ name: 'kanıt.txt', mimeType: 'text/plain', buffer: Buffer.from('kanıt') });
  await fileInput.dispatchEvent('change');
  const uploadButton = page.getByRole('button', { name: 'Yükle', exact: true });
  await uploadButton.waitFor();
  await uploadButton.click();
  assert.equal(desktop.fixture.uploadCount, 1);
  await page.getByRole('tab', { name: 'Yorumlar', exact: true }).click();
  assert.equal(await page.locator('.task-comment').count(), 50);
  await page.getByRole('button', { name: 'Daha fazla etkinlik yükle' }).click();
  await page.waitForFunction(() => document.querySelectorAll('.task-comment').length > 50);
  assert.equal(await page.locator('.task-comment').count(), 62);
  assert.deepEqual(desktopErrors, []);
  await desktop.context.close();

  const viewer = await createContext({ width: 1280, height: 900 }, 'Viewer');
  const viewerPage = await viewer.context.newPage();
  await openDesktopTask(viewerPage);
  assert.equal(await viewerPage.locator('#task-title').count(), 0);
  assert.ok(await viewerPage.getByText('Bu görevde alanlar salt okunur.', { exact: false }).isVisible());
  assert.equal(await viewerPage.locator('.safe-rich-text img').count(), 0);
  assert.match(await viewerPage.locator('.safe-rich-text').first().innerText(), /<img src=x onerror=alert\(1\)>/);
  assert.ok(await viewerPage.getByRole('button', { name: 'Görev takibini değiştir' }).isVisible());
  assert.ok(await viewerPage.locator('#task-comment').isVisible());
  await viewerPage.screenshot({ path: resolve(output, 'desktop-viewer-drawer.png'), fullPage: true });
  await viewer.context.close();

  const mobile = await createContext({ width: 390, height: 844 });
  const mobilePage = await mobile.context.newPage();
  const mobileErrors = watchConsole(mobilePage);
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/tasks/task-1`, { waitUntil: 'networkidle' });
  await mobilePage.locator('.mobile-task-header h1').waitFor();
  assert.equal(await mobilePage.locator('.mobile-task-description img').count(), 0);
  assert.match(await mobilePage.locator('.mobile-task-description p').first().innerText(), /<img src=x onerror=alert\(1\)>/);
  await mobilePage.screenshot({ path: resolve(output, 'mobile-owner-detail.png'), fullPage: true });
  await mobilePage.getByRole('button', { name: /Etkinlik/ }).click();
  await mobilePage.locator('.mobile-activity-entry').first().waitFor();
  assert.equal(await mobilePage.locator('.mobile-activity-entry').count(), 50);
  await mobilePage.getByRole('button', { name: 'Daha fazla etkinlik' }).click();
  await mobilePage.waitForFunction(() => document.querySelectorAll('.mobile-activity-entry').length > 50);
  assert.equal(await mobilePage.locator('.mobile-activity-entry').count(), 61);
  assert.match(await mobilePage.locator('.mobile-activity-entry').first().innerText(), /Yorum eklendi/);
  assert.match(await mobilePage.locator('.mobile-activity-entry').first().innerText(), /ada/);
  const dimensions = await mobilePage.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1, `Mobile detail overflowed: ${dimensions.scrollWidth}/${dimensions.width}`);
  assert.deepEqual(mobileErrors, []);
  await mobilePage.locator('.mobile-task-detail').evaluate(element => { element.scrollTop = 0; });
  await mobilePage.screenshot({ path: resolve(output, 'mobile-activity.png'), fullPage: true });
  await mobile.context.close();
} finally {
  await browser.close();
  await server.close();
}

console.log('V3-UX-006 browser passed: safe detail, drawer/page parity, permissions, conflict draft, collaboration, upload, mentions, bounded activity and mobile overflow.');
