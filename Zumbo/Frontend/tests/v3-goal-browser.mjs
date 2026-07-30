import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-feature-005');
const checks = [];
const failures = [];
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });

const owner = user('owner-1', 'ada', 'Ada Yılmaz');
const resultOwner = user('result-owner-1', 'deniz', 'Deniz Kaya');
const users = [owner, resultOwner];
const projects = [
  project('project-atlas', 'ATL', 'Atlas Teslimat'),
  project('project-mobile', 'MOB', 'Mobil Dönüşüm')
];
const portfolios = [{
  id: 'portfolio-1',
  ownerUserId: owner.id,
  name: 'Teslimat portföyü',
  viewerUserIds: [resultOwner.id],
  initiatives: [{
    id: 'initiative-1',
    name: 'Ekip aktivasyonu',
    ownerUserId: owner.id,
    status: 'Active',
    health: 'OnTrack',
    confidence: 80,
    projectIds: projects.map(item => item.id),
    statusUpdates: []
  }],
  dependencies: [],
  canEdit: true,
  archived: false,
  updatedAt: '2026-07-29T08:00:00Z',
  version: 2
}];
const progressUpdates = [{
  id: 'progress-1',
  previousValue: 20,
  currentValue: 45,
  confidence: 70,
  note: 'Pilot ekipler hedef akışa geçti.',
  authorUserId: resultOwner.id,
  createdAt: '2026-07-29T08:15:00Z'
}];
const statusUpdates = [{
  id: 'status-1',
  status: 'Active',
  health: 'OnTrack',
  confidence: 74,
  note: 'Çeyrek hedefi planla uyumlu ilerliyor.',
  authorUserId: owner.id,
  createdAt: '2026-07-29T08:00:00Z'
}];

function user(id, username, displayName) {
  return {
    id,
    username,
    displayName,
    email: `${username}@zumbo.local`,
    organizationId: 'org-goals',
    roles: ['User']
  };
}

function project(id, key, name) {
  return {
    id,
    organizationId: 'org-goals',
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

function goal(id, name, actor, partial = false) {
  const canEdit = actor.id === owner.id;
  return {
    id,
    ownerUserId: owner.id,
    name,
    description: 'Ölçülebilir çeyrek hedefi',
    periodStart: '2026-07-01',
    periodEnd: '2026-09-30',
    status: 'Active',
    health: partial ? 'AtRisk' : 'OnTrack',
    confidence: partial ? 52 : 74,
    progress: partial ? 28 : 45,
    viewerUserIds: [resultOwner.id],
    initiativeLinks: [{ portfolioId: 'portfolio-1', initiativeId: 'initiative-1' }],
    projectIds: projects.map(item => item.id),
    keyResults: [{
      id: `${id}-activation`,
      ownerUserId: resultOwner.id,
      name: 'Aktif ekip oranı',
      description: 'Pilot ekip aktivasyonu',
      baselineValue: 0,
      targetValue: 100,
      currentValue: partial ? 28 : 45,
      unit: '%',
      direction: 'Increase',
      progress: partial ? 28 : 45,
      confidence: 70,
      progressUpdates: [...progressUpdates],
      canUpdate: canEdit || actor.id === resultOwner.id,
      progressUpdateRetentionLimit: 50
    }, {
      id: `${id}-lead-time`,
      ownerUserId: owner.id,
      name: 'Teslimat çevrim süresi',
      description: 'Gün bazında azalış',
      baselineValue: 10,
      targetValue: 4,
      currentValue: 7,
      unit: 'gün',
      direction: 'Decrease',
      progress: 50,
      confidence: 68,
      progressUpdates: [],
      canUpdate: canEdit,
      progressUpdateRetentionLimit: 50
    }],
    statusUpdates: [...statusUpdates],
    canEdit,
    canUpdateStatus: canEdit,
    archived: false,
    updatedAt: '2026-07-29T08:15:00Z',
    version: 5,
    statusUpdateRetentionLimit: 50
  };
}

function rollup(item, partial = false) {
  return {
    goalId: item.id,
    sourceStatus: partial ? 'Partial' : 'Ready',
    progress: item.progress,
    confidence: item.confidence,
    generatedAt: '2026-07-29T08:20:00Z',
    initiatives: [{
      portfolioId: 'portfolio-1',
      id: 'initiative-1',
      name: 'Ekip aktivasyonu',
      status: 'Active',
      health: partial ? 'AtRisk' : 'OnTrack',
      confidence: partial ? 52 : 80
    }],
    projects: partial ? [projects[0]] : projects,
    unavailableSources: partial ? ['project:project-mobile'] : []
  };
}

function envelope(data) {
  return JSON.stringify({
    success: true,
    data,
    error: null,
    correlationId: 'v3-feature-005'
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
  const ready = goal('goal-ready', 'Ekip aktivasyonunu artır', actor);
  const partial = goal('goal-partial', 'Mobil yayılımı hızlandır', actor, true);
  if (path === '/api/browser-auth/session') {
    return json(route, { user: actor, csrfToken: 'csrf' });
  }
  if (path === '/api/projects' || path === '/api/projects/') return json(route, projects);
  if (path === '/api/projects/project-atlas') return json(route, projects[0]);
  if (path === '/api/boards/by-project/project-atlas') return json(route, []);
  if (path === '/api/auth/users') return json(route, users);
  if (path === '/api/portfolios' || path === '/api/portfolios/') {
    return json(route, { items: portfolios, page: 1, pageSize: 100, total: 1 });
  }
  if (path === '/api/goals' || path === '/api/goals/') {
    return json(route, {
      items: actor.id === owner.id ? [ready, partial] : [ready],
      page: 1,
      pageSize: 100,
      total: actor.id === owner.id ? 2 : 1
    });
  }
  if (path === '/api/goals/goal-ready') return json(route, ready, { ETag: '"5"' });
  if (path === '/api/goals/goal-partial') return json(route, partial, { ETag: '"5"' });
  if (path === '/api/goals/goal-ready/rollup') return json(route, rollup(ready));
  if (path === '/api/goals/goal-partial/rollup') return json(route, rollup(partial, true));
  if (path.endsWith('/progress-updates') && request.method() === 'POST') {
    const body = request.postDataJSON();
    progressUpdates.unshift({
      id: 'progress-browser',
      previousValue: 45,
      currentValue: body.currentValue,
      confidence: body.confidence,
      note: body.note,
      authorUserId: actor.id,
      createdAt: '2026-07-29T08:30:00Z'
    });
    return json(route, goal('goal-ready', 'Ekip aktivasyonunu artır', actor), { ETag: '"6"' });
  }
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
    `${server.origin}/desktop-bulma/index.html#section=goals&project=project-atlas`,
    { waitUntil: 'networkidle' }
  );
  await desktop.getByRole('heading', { name: "Hedefler ve key result'lar" }).waitFor();
  await desktop.getByText('Ekip aktivasyonunu artır', { exact: true }).first().waitFor();
  assert.match(await desktop.locator('.goal-summary').innerText(), /45%/);
  assert.match(await desktop.locator('.goal-result-list').innerText(), /Aktif ekip oranı/);
  await desktop.locator('.goal-result-list details summary').first().click();
  assert.match(await desktop.locator('.goal-result-list').innerText(), /En yeni 50 ilerleme kaydı korunur/);
  assert.doesNotMatch(await desktop.locator('.goal-workspace').innerText(), /project-atlas|initiative-1/);
  checks.push('desktop-owner-progress-history-named');
  await capture(desktop, 'desktop-ready.png');

  await desktop.getByRole('button', { name: /Mobil yayılımı hızlandır/ }).click();
  await desktop.locator('.goal-partial').waitFor();
  assert.match(await desktop.locator('.goal-partial').innerText(), /1 bağlantı/);
  checks.push('desktop-partial-source-explicit');
  await desktop.getByRole('tab', { name: 'Bağlantılar' }).click();
  assert.match(await desktop.locator('.goal-source-grid').innerText(), /Ekip aktivasyonu/);
  assert.match(await desktop.locator('.goal-source-grid').innerText(), /Atlas Teslimat/);
  checks.push('desktop-source-links-readable');
  await capture(desktop, 'desktop-partial-sources.png');
  await desktopContext.close();

  const mobileContext = await contextFor(resultOwner, { width: 390, height: 844 });
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile-result-owner');
  await mobile.goto(`${server.origin}/mobile-ionic/index.html#/goals`, { waitUntil: 'networkidle' });
  await mobile.getByText('Ekip aktivasyonunu artır', { exact: true }).first().waitFor();
  await mobile.locator('.mobile-goal-readonly').waitFor();
  assert.equal(await mobile.getByRole('button', { name: 'İlerleme güncelle' }).count(), 1);
  await mobile.getByRole('button', { name: 'İlerleme güncelle' }).click();
  await mobile.getByLabel('Güncel değer').fill('58');
  await mobile.getByLabel('İlerleme notu').fill('Mobil pilot aktivasyonu yüzde elli sekize ulaştı.');
  await mobile.getByRole('button', { name: 'İlerlemeyi yayınla' }).click();
  await mobile.getByText('Mobil pilot aktivasyonu yüzde elli sekize ulaştı.', { exact: true }).waitFor();
  await mobile.getByText('En yeni 50 ilerleme kaydı korunur.', { exact: true }).waitFor();
  checks.push('mobile-key-result-owner-progress-readonly-goal');
  const dimensions = await mobile.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    minimumActionHeight: Math.min(...Array.from(
      document.querySelectorAll('.mobile-goal-tabs button, .mobile-goal-row button')
    ).map(element => element.getBoundingClientRect().height))
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  assert.ok(dimensions.minimumActionHeight >= 44);
  checks.push('mobile-no-overflow-touch-targets');
  await capture(mobile, 'mobile-result-owner.png');
  await mobileContext.close();

  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-005',
    mode: 'deterministic-browser',
    passed: failures.length === 0 && checks.length === 5,
    viewports: ['1440x1000', '390x844'],
    checks,
    failures,
    noDeployment: true
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 5);
console.log('V3-FEATURE-005 browser passed: progress history, partial links and key-result-owner mobile flow.');
