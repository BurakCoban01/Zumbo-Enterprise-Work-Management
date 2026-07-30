import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-feature-007');
const checks = [];
const failures = [];
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });

const owner = user('owner-1', 'ada', 'Ada Yılmaz');
const viewer = user('viewer-1', 'deniz', 'Deniz Kaya');
const users = [owner, viewer];
const project = {
  id: 'project-atlas',
  organizationId: 'org-knowledge',
  key: 'ATL',
  name: 'Atlas Teslimat',
  visibility: 'Private',
  members: [
    { userId: owner.id, role: 'ProjectOwner' },
    { userId: viewer.id, role: 'Viewer' }
  ],
  teamIds: [],
  milestones: [],
  releases: [],
  components: [],
  versions: [],
  version: 2
};
const workItem = {
  id: 'work-release',
  key: 'ATL-42',
  title: 'Yayın kontrol listesini tamamla'
};
const versions = [{
  number: 2,
  title: 'Üretim yayın runbooku',
  changeSummary: 'Rollback ve güvenlik adımları eklendi.',
  authorUserId: owner.id,
  createdAt: '2026-07-29T09:30:00Z'
}, {
  number: 1,
  title: 'Üretim yayın runbooku',
  changeSummary: 'İlk doğrulanmış sürüm.',
  authorUserId: owner.id,
  createdAt: '2026-07-28T09:30:00Z'
}];
let comments = [{
  id: 'comment-risk',
  body: 'Rollback sahibi açıkça belirtilmeli.',
  authorUserId: viewer.id,
  resolved: false,
  resolvedByUserId: null,
  resolvedAt: null,
  createdAt: '2026-07-29T10:00:00Z'
}];

function user(id, username, displayName) {
  return {
    id,
    username,
    displayName,
    email: `${username}@zumbo.local`,
    organizationId: 'org-knowledge',
    roles: ['User']
  };
}

function knowledgeDocument(actor) {
  return {
    id: 'knowledge-release',
    scopeType: 'Project',
    scopeId: project.id,
    scopeName: project.name,
    ownerUserId: owner.id,
    title: 'Üretim yayın runbooku',
    contentMarkdown: [
      '# Üretim yayın runbooku',
      '',
      '> Değişiklik penceresi onaylandıktan sonra ilerleyin.',
      '',
      '- [Yayın kontrol listesi](/work-items/work-release)',
      '- **Rollback sahibi:** Ada Yılmaz',
      '- [Güvensiz bağlantı](javascript:unsafe)',
      '',
      '```sh',
      'pnpm test',
      '```'
    ].join('\n'),
    tags: ['runbook', 'güvenlik'],
    workItemIds: [workItem.id],
    userIds: [viewer.id],
    currentContentVersion: 2,
    versions,
    comments: comments.map(item => ({ ...item })),
    canEdit: actor.id === owner.id,
    canComment: true,
    archived: false,
    updatedAt: '2026-07-29T10:00:00Z',
    version: 4
  };
}

function summary(actor) {
  const document = knowledgeDocument(actor);
  return {
    id: document.id,
    scopeType: document.scopeType,
    scopeId: document.scopeId,
    scopeName: document.scopeName,
    ownerUserId: document.ownerUserId,
    title: document.title,
    excerpt: 'Değişiklik penceresi ve rollback adımları.',
    tags: document.tags,
    currentContentVersion: document.currentContentVersion,
    canEdit: document.canEdit,
    archived: false,
    updatedAt: document.updatedAt,
    version: document.version
  };
}

function envelope(data) {
  return JSON.stringify({
    success: true,
    data,
    error: null,
    correlationId: 'v3-feature-007'
  });
}

async function contextFor(actor, viewport) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth));
    sessionStorage.setItem('zumbo.csrfToken', 'csrf');
  }, actor);
  await context.route(`${apiBaseUrl}/**`, route => handle(route, actor));
  return context;
}

async function handle(route, actor) {
  const request = route.request();
  if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
  const url = new URL(request.url());
  const path = url.pathname;
  if (path === '/api/browser-auth/session') {
    return json(route, { user: actor, csrfToken: 'csrf' });
  }
  if (path === '/api/projects' || path === '/api/projects/') return json(route, [project]);
  if (path === `/api/projects/${project.id}`) return json(route, project);
  if (path === `/api/boards/by-project/${project.id}`) return json(route, []);
  if (path === '/api/auth/users') return json(route, users);
  if (path === '/api/teams' || path === '/api/teams/') return json(route, []);
  if (path === '/api/portfolios' || path === '/api/portfolios/') {
    return json(route, { items: [], page: 1, pageSize: 100, total: 0 });
  }
  if (path === '/api/knowledge-documents' || path === '/api/knowledge-documents/') {
    return json(route, {
      items: [summary(actor)],
      page: 1,
      pageSize: 100,
      visibleTotal: 1,
      scannedDocuments: 1,
      sourceStatus: actor.id === owner.id ? 'Partial' : 'Ready'
    });
  }
  if (path === '/api/knowledge-documents/scope-link-options') {
    return json(route, {
      workItems: [{ id: workItem.id, label: `${workItem.key} · ${workItem.title}`, context: project.name }],
      users: users.map(item => ({ id: item.id, label: item.displayName, context: item.email })),
      sourceStatus: 'Ready'
    });
  }
  if (path === '/api/knowledge-documents/knowledge-release/versions/1') {
    return json(route, {
      number: 1,
      title: 'Üretim yayın runbooku',
      contentMarkdown: '# İlk sürüm\n\nYayın sorumlusu ve kontrol listesi tanımlandı.',
      tags: ['runbook'],
      workItemIds: [workItem.id],
      userIds: [viewer.id],
      changeSummary: 'İlk doğrulanmış sürüm.',
      authorUserId: owner.id,
      createdAt: '2026-07-28T09:30:00Z'
    });
  }
  if (path === '/api/knowledge-documents/knowledge-release'
      && request.method() === 'GET') {
    return json(route, knowledgeDocument(actor), { ETag: '"4"' });
  }
  if (path === '/api/knowledge-documents/knowledge-release/comments'
      && request.method() === 'POST') {
    const body = request.postDataJSON();
    comments = [...comments, {
      id: `comment-${comments.length + 1}`,
      body: body.body,
      authorUserId: actor.id,
      resolved: false,
      resolvedByUserId: null,
      resolvedAt: null,
      createdAt: '2026-07-29T11:00:00Z'
    }];
    return json(route, knowledgeDocument(actor), { ETag: '"5"' });
  }
  if (path.endsWith('/comments/comment-risk/resolve')
      && request.method() === 'PATCH') {
    comments = comments.map(item => item.id === 'comment-risk'
      ? {
        ...item,
        resolved: true,
        resolvedByUserId: actor.id,
        resolvedAt: '2026-07-29T11:05:00Z'
      }
      : item);
    return json(route, knowledgeDocument(actor), { ETag: '"5"' });
  }
  if (path.startsWith('/api/notifications')) return json(route, []);
  if (path.startsWith('/api/work-items/reports/')) return json(route, []);
  if (path === '/api/work-items/search') {
    return json(route, { items: [], totalCount: 0, degraded: false });
  }
  if (path === `/api/sprints/projects/${project.id}`
      || path === `/api/sprints/projects/${project.id}/backlog`) {
    return json(route, { items: [], nextCursor: null });
  }
  if (path === `/api/workflows/${project.id}`) {
    return json(route, { projectId: project.id, statuses: [], transitions: [] });
  }
  if (path === `/api/work-item-schemas/${project.id}`) {
    return json(route, { issueTypes: [], customFields: [], layouts: [] });
  }
  return json(route, []);
}

function json(route, data, headers = {}) {
  return route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers,
    body: envelope(data)
  });
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const value = message.text();
    if (!/WebSocket|Failed to start|Failed to load resource/.test(value)) {
      failures.push(`${label}: ${value}`);
    }
  });
}

async function capture(page, name) {
  const path = resolve(output, name);
  await page.screenshot({ path, fullPage: true });
  const bytes = await readFile(path);
  assert.ok(bytes.length > 15_000, `${name} is unexpectedly small.`);
}

try {
  const desktopContext = await contextFor(owner, { width: 1440, height: 1000 });
  const desktop = await desktopContext.newPage();
  diagnostics(desktop, 'desktop-owner');
  await desktop.goto(
    `${server.origin}/desktop-bulma/index.html#section=knowledge&project=${project.id}`,
    { waitUntil: 'networkidle' }
  );
  await desktop.getByRole('heading', { name: 'Bilgi ve karar dokümanları' }).waitFor();
  await desktop.locator('#knowledge-document-title').waitFor();
  assert.match(await desktop.locator('.knowledge-render').innerText(), /Rollback sahibi: Ada Yılmaz/);
  assert.equal(await desktop.locator('.knowledge-render a[href^="javascript:"]').count(), 0);
  assert.equal(await desktop.locator('.knowledge-render a[href="/work-items/work-release"]').count(), 1);
  assert.match(await desktop.locator('.knowledge-link-list').innerText(), /ATL-42 · Yayın kontrol listesini tamamla/);
  assert.match(await desktop.locator('.knowledge-link-list').innerText(), /Deniz Kaya/);
  assert.doesNotMatch(await desktop.locator('.knowledge-workspace').innerText(), /owner-1|viewer-1|work-release/);
  checks.push('desktop-safe-markdown-and-named-links');

  await desktop.locator('.knowledge-partial').waitFor();
  await desktop.getByRole('button', { name: /v1 · Üretim yayın runbooku/ }).click();
  await desktop.locator('.knowledge-version-banner').waitFor();
  assert.match(await desktop.locator('.knowledge-render').innerText(), /İlk sürüm/);
  await desktop.getByRole('button', { name: 'Güncel sürüme dön' }).click();
  await desktop.getByRole('button', { name: 'Yorumu çöz' }).click();
  await desktop.getByText('Çözüldü', { exact: true }).waitFor();
  checks.push('desktop-partial-history-and-comment-resolution');
  await capture(desktop, 'desktop-owner.png');

  await desktop.getByRole('button', { name: 'Yeni sürüm' }).click();
  await desktop.getByLabel('Sürüm özeti').waitFor();
  assert.equal(await desktop.getByRole('button', { name: 'Yeni sürümü kaydet' }).count(), 1);
  checks.push('desktop-owner-version-authority');
  await desktopContext.close();

  const mobileContext = await contextFor(viewer, { width: 390, height: 844 });
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile-viewer');
  await mobile.goto(`${server.origin}/mobile-ionic/index.html#/knowledge`, {
    waitUntil: 'networkidle'
  });
  await mobile.getByText('Üretim yayın runbooku', { exact: true }).first().waitFor();
  await mobile.locator('.mobile-knowledge-readonly').waitFor();
  assert.equal(await mobile.getByRole('button', { name: 'Yeni sürüm' }).count(), 0);
  assert.equal(await mobile.locator('.mobile-knowledge-render a[href^="javascript:"]').count(), 0);
  await mobile.getByRole('tab', { name: 'Yorumlar' }).click();
  await mobile.getByLabel('Yeni yorum').fill('Mobil görüntüleyici bağlamı doğrulandı.');
  await mobile.getByRole('button', { name: 'Yorum ekle' }).click();
  await mobile.getByText('Mobil görüntüleyici bağlamı doğrulandı.', { exact: true }).waitFor();
  await mobile.getByRole('tab', { name: 'Bağlar' }).click();
  assert.match(await mobile.locator('.mobile-knowledge-links').innerText(), /ATL-42 · Yayın kontrol listesini tamamla/);
  assert.match(await mobile.locator('.mobile-knowledge-links').innerText(), /Deniz Kaya/);
  const dimensions = await mobile.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    minimumActionHeight: Math.min(...Array.from(
      document.querySelectorAll('.mobile-knowledge-tabs button, .mobile-knowledge-head button')
    ).map(element => element.getBoundingClientRect().height))
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  assert.ok(dimensions.minimumActionHeight >= 44);
  checks.push('mobile-viewer-comment-links-and-responsive-authority');
  await capture(mobile, 'mobile-viewer.png');
  await mobileContext.close();

  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-007',
    mode: 'deterministic-browser',
    passed: failures.length === 0 && checks.length === 4,
    viewports: ['1440x1000', '390x844'],
    checks,
    failures,
    noDeployment: true
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 4);
console.log('V3-FEATURE-007 browser passed: safe rendering, versions, links, comments and mobile authority.');
