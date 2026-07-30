import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-ux-002');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const user = { id: 'user-1', username: 'deniz', email: 'deniz@zumbo.local', organizationId: 'org-1', roles: ['User'] };
const project = { id: 'project-1', organizationId: 'org-1', key: 'UX', name: 'Deneyim Platformu', members: [{ userId: user.id, role: 'Developer' }], teamIds: [] };
const board = { id: 'board-1', projectId: project.id, name: 'Ürün Panosu', type: 'Kanban', swimlaneMode: 'None', views: [], columns: [{ id: 'todo', name: 'To Do', category: 'Todo', position: 0 }] };
const now = Date.now();
const tasks = [
  task('task-due', 'Erişilebilirlik turunu tamamla', { dueDate: new Date(now + 86400000).toISOString() }),
  task('task-blocked', 'Bağımlılık kararını bekle', { status: 'Blocked', priority: 'Critical' }),
  task('task-approval', 'Yayın kapsamını onayla', { approvals: [{ id: 'approval-1', status: 'Pending' }] })
];
const notifications = [
  { id: 'notification-1', userId: user.id, type: 'Mention', message: 'Bir yorumda sizden bahsedildi.', read: false, createdAt: new Date(now).toISOString() },
  { id: 'notification-2', userId: user.id, type: 'Assignment', message: 'Yeni görev atandı.', read: true, createdAt: new Date(now - 3600000).toISOString() }
];

function task(id, title, extra = {}) {
  return {
    id, title, projectId: project.id, boardId: board.id, columnId: 'todo', type: 'Task', status: 'In Progress', priority: 'High',
    assigneeUserId: user.id, description: '', labels: [], checklist: [], comments: [], attachments: [], workLogs: [], relations: [], approvals: [],
    customFields: [], statusHistory: [{ changedAt: new Date(now - 1000).toISOString() }], rank: 1000, version: 1, ...extra
  };
}

function envelope(data) { return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-ux-002' }); }
function response(path) {
  if (path === '/api/browser-auth/session') return { user, csrfToken: 'csrf' };
  if (path === '/api/projects') return [project];
  if (path === '/api/boards/by-project/project-1') return [board];
  if (path === '/api/work-items/search') return { items: tasks, totalCount: tasks.length, degraded: false };
  if (path === '/api/work-items/reports/project-summary/project-1') return { total: 3, inProgress: 3, done: 0, overdue: 0 };
  if (path.startsWith('/api/work-items/reports/')) return [];
  if (path === '/api/notifications' || path === `/api/notifications/${user.id}`) return notifications;
  if (path === '/api/teams' || path === '/api/auth/users') return [];
  if (path === '/api/workflows/project-1') return { projectId: project.id, statuses: [], transitions: [] };
  if (path === '/api/work-item-schemas/project-1') return { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
  if (path.startsWith('/api/sprints/projects/project-1')) return { items: [], total: 0 };
  return [];
}

async function context(viewport) {
  const value = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await value.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, { user, csrfToken: 'csrf' });
  await value.route(`${apiBaseUrl}/**`, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const path = new URL(route.request().url()).pathname;
    await route.fulfill({ status: 200, contentType: 'application/json', body: envelope(response(path)) });
  });
  return value;
}

try {
  const desktop = await context({ width: 1440, height: 1000 });
  const page = await desktop.newPage();
  await page.goto(`${server.origin}/desktop-bulma/index.html#section=home&project=${project.id}&board=${board.id}`, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: `Merhaba, ${user.username}` }).waitFor();
  assert.equal(await page.getByRole('button', { name: /Açık 3/ }).count(), 1);
  assert.ok(await page.getByText(tasks[0].title, { exact: true }).isVisible());
  await page.getByRole('button', { name: 'İşlerim', exact: true }).click();
  await page.getByRole('tab', { name: 'Engelli', exact: true }).click();
  assert.ok(await page.getByText(tasks[1].title, { exact: true }).isVisible());
  await page.getByLabel('Kişisel görünüm adı').fill('Takip listem');
  await page.getByRole('button', { name: 'Kişisel görünümü kaydet' }).click();
  assert.ok(await page.getByRole('button', { name: /Takip listem/ }).isVisible());
  await page.getByRole('button', { name: 'Gelen kutusu', exact: true }).click();
  await page.getByText(notifications[0].message, { exact: true }).waitFor();
  await page.getByText(tasks[2].title, { exact: true }).waitFor();
  await page.screenshot({ path: resolve(output, 'desktop-inbox.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  const dimensions = await page.evaluate(() => ({ width: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  await page.screenshot({ path: resolve(output, 'desktop-responsive.png'), fullPage: true });
  await desktop.close();

  const mobile = await context({ width: 390, height: 844 });
  const mobilePage = await mobile.newPage();
  await mobilePage.goto(`${server.origin}/mobile-ionic/index.html#/app/dashboard`, { waitUntil: 'networkidle' });
  await mobilePage.getByRole('tab', { name: 'Tarihli', exact: true }).click();
  await mobilePage.getByText(tasks[0].title, { exact: true }).waitFor();
  await mobilePage.locator('a.tab-item').filter({ hasText: 'Bildirimler' }).click();
  await mobilePage.getByRole('tab', { name: 'Eylem', exact: true }).click();
  await mobilePage.getByText(notifications[0].message, { exact: true }).waitFor();
  await mobilePage.screenshot({ path: resolve(output, 'mobile-triage.png'), fullPage: true });
  await mobile.close();
} finally {
  await browser.close();
  await server.close();
}

console.log('V3-UX-002 browser passed: desktop home/my-work/inbox, saved view, responsive reflow and mobile triage entry.');
