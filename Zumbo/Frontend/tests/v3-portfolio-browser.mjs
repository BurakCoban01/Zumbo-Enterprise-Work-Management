import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-feature-004');
const checks = [];
const failures = [];
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });

const owner = user('owner-1', 'ada', 'Ada Yılmaz');
const initiativeOwner = user('initiative-owner-1', 'deniz', 'Deniz Kaya');
const users = [owner, initiativeOwner];
const projects = [
  project('project-atlas', 'ATL', 'Atlas Teslimat'),
  project('project-mobile', 'MOB', 'Mobil Dönüşüm')
];
const statusUpdates = [{
  id: 'update-1',
  status: 'Active',
  health: 'AtRisk',
  confidence: 62,
  note: 'Kimlik doğrulama bağımlılığı yakından izleniyor.',
  authorUserId: 'initiative-owner-1',
  createdAt: '2026-07-29T07:30:00Z'
}];

function user(id, username, displayName) {
  return {
    id,
    username,
    displayName,
    email: `${username}@zumbo.local`,
    organizationId: 'org-portfolio',
    roles: ['User']
  };
}

function project(id, key, name) {
  return {
    id,
    organizationId: 'org-portfolio',
    key,
    name,
    visibility: 'Private',
    members: users.map((item, index) => ({
      userId: item.id,
      role: index === 0 ? 'ProjectOwner' : 'Viewer'
    })),
    teamIds: [],
    milestones: [],
    releases: [],
    components: [],
    versions: [],
    version: 2
  };
}

function portfolio(id, name, actor, partial = false) {
  const canEdit = actor.id === owner.id;
  return {
    id,
    ownerUserId: owner.id,
    name,
    description: 'Çapraz proje teslimat planı',
    viewerUserIds: [initiativeOwner.id],
    initiatives: [{
      id: `${id}-platform`,
      name: 'Platform güvenilirliği',
      summary: 'Ortak platform hedefleri',
      parentInitiativeId: null,
      ownerUserId: owner.id,
      status: 'Active',
      health: partial ? 'OffTrack' : 'OnTrack',
      confidence: partial ? 44 : 82,
      targetAt: '2026-10-15T00:00:00Z',
      projectIds: projects.map(item => item.id),
      milestoneLinks: [],
      statusUpdates: [],
      canUpdateStatus: canEdit,
      statusUpdateRetentionLimit: 50
    }, {
      id: `${id}-mobile`,
      name: 'Mobil ekip deneyimi',
      summary: 'Mobil temel iş akışları',
      parentInitiativeId: `${id}-platform`,
      ownerUserId: initiativeOwner.id,
      status: 'Active',
      health: 'AtRisk',
      confidence: 62,
      targetAt: '2026-09-20T00:00:00Z',
      projectIds: ['project-mobile'],
      milestoneLinks: [],
      statusUpdates: [...statusUpdates],
      canUpdateStatus: canEdit || actor.id === initiativeOwner.id,
      statusUpdateRetentionLimit: 50
    }],
    dependencies: [{
      id: `${id}-dependency`,
      sourceProjectId: 'project-atlas',
      targetProjectId: 'project-mobile',
      description: 'Platform oturumu mobil yayını etkinleştirir.',
      status: 'Active',
      requiredBy: '2026-09-01T00:00:00Z'
    }],
    canEdit,
    archived: false,
    updatedAt: '2026-07-29T08:00:00Z',
    version: 4
  };
}

function roadmap(item, partial = false) {
  return {
    portfolioId: item.id,
    sourceStatus: partial ? 'Partial' : 'Ready',
    generatedAt: '2026-07-29T08:05:00Z',
    unavailableProjectIds: partial ? ['project-mobile'] : [],
    initiatives: item.initiatives.map((initiative, index) => ({
      id: initiative.id,
      name: initiative.name,
      parentInitiativeId: initiative.parentInitiativeId,
      ownerUserId: initiative.ownerUserId,
      status: initiative.status,
      health: initiative.health,
      confidence: initiative.confidence,
      targetAt: initiative.targetAt,
      totalWorkItems: index === 0 ? 16 : 7,
      completedWorkItems: index === 0 ? 10 : 3,
      overdueWorkItems: index === 0 ? 2 : 1,
      progress: index === 0 ? 63 : 43,
      projects: (partial && index === 1 ? [] : initiative.projectIds.map(projectId => {
        const source = projects.find(projectItem => projectItem.id === projectId);
        return {
          ...source,
          totalWorkItems: 8,
          completedWorkItems: projectId === 'project-atlas' ? 6 : 4,
          overdueWorkItems: projectId === 'project-atlas' ? 0 : 2,
          progress: projectId === 'project-atlas' ? 75 : 50,
          milestones: [{
            id: `${projectId}-milestone`,
            name: projectId === 'project-atlas' ? 'Platform hazır' : 'Mobil pilot',
            dueAt: '2026-09-01T00:00:00Z',
            status: 'Open',
            completedAt: null
          }],
          updatedAt: '2026-07-29T08:00:00Z'
        };
      }))
    })),
    dependencies: item.dependencies
  };
}

function envelope(data) {
  return JSON.stringify({
    success: true,
    data,
    error: null,
    correlationId: 'v3-feature-004'
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
  const path = new URL(request.url()).pathname;
  const ready = portfolio('portfolio-ready', 'Teslimat portföyü', actor);
  const partial = portfolio('portfolio-partial', 'Risk portföyü', actor, true);
  if (path === '/api/browser-auth/session') {
    return json(route, { user: actor, csrfToken: 'csrf' });
  }
  if (path === '/api/projects' || path === '/api/projects/') return json(route, projects);
  if (path === '/api/projects/project-atlas') return json(route, projects[0]);
  if (path === '/api/boards/by-project/project-atlas') return json(route, []);
  if (path === '/api/portfolios' || path === '/api/portfolios/') {
    return json(route, {
      items: actor.id === owner.id ? [ready, partial] : [ready],
      page: 1,
      pageSize: 100,
      total: actor.id === owner.id ? 2 : 1
    });
  }
  if (path === '/api/portfolios/portfolio-ready') return json(route, ready, { ETag: '"4"' });
  if (path === '/api/portfolios/portfolio-partial') return json(route, partial, { ETag: '"4"' });
  if (path === '/api/portfolios/portfolio-ready/roadmap') return json(route, roadmap(ready));
  if (path === '/api/portfolios/portfolio-partial/roadmap') return json(route, roadmap(partial, true));
  if (path.endsWith('/status-updates') && request.method() === 'POST') {
    const body = request.postDataJSON();
    ready.initiatives[1].health = body.health;
    ready.initiatives[1].confidence = body.confidence;
    ready.initiatives[1].statusUpdates.push({
      id: 'update-browser',
      ...body,
      authorUserId: actor.id,
      createdAt: '2026-07-29T08:10:00Z'
    });
    statusUpdates.push(ready.initiatives[1].statusUpdates.at(-1));
    return json(route, ready, { ETag: '"5"' });
  }
  if (path === '/api/auth/users') return json(route, users);
  if (path === '/api/teams' || path.startsWith('/api/notifications')) return json(route, []);
  if (path.startsWith('/api/work-items/reports/')) return json(route, []);
  if (path === '/api/work-items/search') {
    return json(route, { items: [], totalCount: 0, degraded: false });
  }
  if (path === '/api/sprints/projects/project-atlas'
      || path === '/api/sprints/projects/project-atlas/backlog') {
    return json(route, { items: [], nextCursor: null });
  }
  if (path === '/api/workflows/project-atlas') {
    return json(route, { projectId: 'project-atlas', statuses: [], transitions: [] });
  }
  if (path === '/api/work-item-schemas/project-atlas') {
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
    `${server.origin}/desktop-bulma/index.html#section=portfolios&project=project-atlas`,
    { waitUntil: 'networkidle' }
  );
  await desktop.getByRole('heading', { name: "Portföyler ve initiative'ler" }).waitFor();
  await desktop.getByText('Teslimat portföyü', { exact: true }).first().waitFor();
  assert.ok(await desktop.locator('.portfolio-definition').isVisible());
  assert.equal(await desktop.locator('.portfolio-table-wrap th[scope="col"]').count(), 5);
  const roadmapText = await desktop.locator('.portfolio-table-wrap').first().innerText();
  assert.match(roadmapText, /Atlas Teslimat/);
  assert.match(roadmapText, /Mobil Dönüşüm/);
  assert.doesNotMatch(roadmapText, /project-atlas|project-mobile/);
  await desktop.getByRole('tab', { name: 'Güncellemeler' }).click();
  await desktop.getByText('Her initiative için en yeni 50 durum kaydı korunur.', { exact: true }).waitFor();
  await capture(desktop, 'desktop-retention.png');
  await desktop.getByRole('tab', { name: 'Yol haritası' }).click();
  checks.push('desktop-owner-ready-named-roadmap-table');
  await capture(desktop, 'desktop-ready.png');

  await desktop.getByRole('button', { name: /Risk portföyü/ }).click();
  await desktop.locator('.portfolio-partial').waitFor();
  assert.match(await desktop.locator('.portfolio-partial').innerText(), /1 proje/);
  checks.push('desktop-partial-source-explicit');
  await desktop.getByRole('tab', { name: 'Bağımlılıklar' }).click();
  assert.match(await desktop.locator('.portfolio-panel').innerText(), /Atlas Teslimat/);
  assert.match(await desktop.locator('.portfolio-panel').innerText(), /Mobil Dönüşüm/);
  checks.push('desktop-directed-dependency-named');
  await capture(desktop, 'desktop-partial-dependencies.png');
  await desktopContext.close();

  const mobileContext = await contextFor(initiativeOwner, { width: 390, height: 844 });
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile-initiative-owner');
  await mobile.goto(
    `${server.origin}/mobile-ionic/index.html#/portfolios`,
    { waitUntil: 'networkidle' }
  );
  await mobile.getByText('Teslimat portföyü', { exact: true }).first().waitFor();
  await mobile.locator('.mobile-portfolio-readonly').waitFor();
  assert.match(
    await mobile.locator('.mobile-portfolio-readonly').innerText(),
    /initiative durumlarını güncelleyebilirsiniz/
  );
  await mobile.getByRole('tab', { name: 'Hiyerarşi' }).click();
  assert.equal(await mobile.getByRole('button', { name: 'Durum güncelle' }).count(), 1);
  await mobile.getByRole('button', { name: 'Durum güncelle' }).click();
  await mobile.getByLabel('Durum notu').fill('Mobil pilot bağımlılığı yeniden doğrulandı.');
  await mobile.getByRole('button', { name: 'Güncellemeyi yayınla' }).click();
  await mobile.getByText('Mobil pilot bağımlılığı yeniden doğrulandı.', { exact: true }).waitFor();
  await mobile.getByText('Her initiative için en yeni 50 durum kaydı korunur.', { exact: true }).waitFor();
  checks.push('mobile-initiative-owner-status-with-readonly-portfolio');
  const dimensions = await mobile.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    minimumActionHeight: Math.min(...Array.from(
      document.querySelectorAll('.mobile-portfolio-tabs button, .mobile-initiative-list button')
    ).map(element => element.getBoundingClientRect().height))
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  assert.ok(dimensions.minimumActionHeight >= 44);
  checks.push('mobile-no-overflow-touch-targets');
  await capture(mobile, 'mobile-initiative-owner.png');
  await mobileContext.close();

  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-004',
    mode: 'deterministic-browser',
    passed: failures.length === 0 && checks.length === 5,
    viewports: ['1440x1000', '390x844'],
    checks,
    failures,
    noDeployment: true
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 5);
console.log('V3-FEATURE-004 browser passed: roadmap, partial source, dependencies and initiative-owner mobile flow.');
