import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-feature-006');
const checks = [];
const failures = [];
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });

const owner = user('owner-1', 'ada', 'Ada Yılmaz');
const viewer = user('viewer-1', 'deniz', 'Deniz Kaya');
const users = [owner, viewer];
const team = {
  id: 'team-delivery',
  organizationId: 'org-capacity',
  name: 'Teslimat Ekibi',
  ownerUserId: owner.id,
  members: [{ userId: owner.id, status: 'Active' }],
  version: 2
};
const projects = [
  project('project-atlas', 'ATL', 'Atlas Teslimat'),
  project('project-mobile', 'MOB', 'Mobil Dönüşüm')
];
const portfolio = {
  id: 'portfolio-1',
  ownerUserId: owner.id,
  name: 'Teslimat portföyü',
  viewerUserIds: [viewer.id],
  initiatives: [],
  dependencies: [],
  canEdit: true,
  archived: false,
  updatedAt: '2026-07-29T08:00:00Z',
  version: 2
};

function user(id, username, displayName) {
  return {
    id,
    username,
    displayName,
    email: `${username}@zumbo.local`,
    organizationId: 'org-capacity',
    roles: ['User']
  };
}

function project(id, key, name) {
  return {
    id,
    organizationId: 'org-capacity',
    key,
    name,
    visibility: 'Private',
    members: users.map((item, index) => ({
      userId: item.id,
      role: index === 0 ? 'ProjectOwner' : 'Viewer'
    })),
    teamIds: [team.id],
    milestones: [],
    releases: [],
    components: [],
    versions: [],
    version: 2
  };
}

function plan(id, name, actor, partial = false) {
  return {
    id,
    ownerUserId: owner.id,
    name,
    description: 'Dört haftalık sentetik tahsis planı',
    periodStart: '2026-07-06',
    periodEnd: '2026-07-19',
    portfolioId: portfolio.id,
    projectIds: projects.map(item => item.id),
    members: [{
      userId: owner.id,
      teamId: team.id,
      weeklyCapacityHours: 40
    }],
    allocations: [{
      id: `${id}-allocation`,
      userId: owner.id,
      projectId: projects[0].id,
      startDate: '2026-07-06',
      endDate: '2026-07-19',
      percent: 60
    }],
    viewerUserIds: [viewer.id],
    canEdit: actor.id === owner.id,
    archived: false,
    updatedAt: '2026-07-29T08:30:00Z',
    version: 3,
    partial
  };
}

function snapshot(item, partial = false, allocatedHours = 48) {
  const over = allocatedHours > 80;
  return {
    planId: item.id,
    planVersion: item.version,
    sourceStatus: partial ? 'Partial' : 'Ready',
    periodStart: item.periodStart,
    periodEnd: item.periodEnd,
    generatedAt: '2026-07-29T08:35:00Z',
    truncated: partial,
    unavailableProjectIds: partial ? [projects[1].id] : [],
    summary: {
      people: 1,
      capacityHours: 80,
      allocatedHours,
      remainingHours: 80 - allocatedHours,
      overCapacityPeople: over ? 1 : 0,
      openItems: 2,
      estimatedPoints: 5,
      unestimatedItems: 1,
      unscheduledItems: 1
    },
    members: [{
      userId: owner.id,
      teamId: team.id,
      weeklyCapacityHours: 40,
      capacityHours: 80,
      allocatedHours,
      remainingHours: 80 - allocatedHours,
      allocationPercent: Math.round(allocatedHours / 80 * 100),
      state: over ? 'OverCapacity' : 'Available',
      estimatedPoints: 5,
      unestimatedItems: 1,
      unscheduledItems: 1,
      openItems: 2,
      weeks: [{
        weekStart: '2026-07-06',
        capacityHours: 40,
        allocatedHours: allocatedHours / 2,
        remainingHours: 40 - allocatedHours / 2,
        allocationPercent: Math.round(allocatedHours / 80 * 100),
        state: over ? 'OverCapacity' : 'Available',
        estimatedPoints: 5,
        unestimatedItems: 0,
        scheduledItems: 1
      }, {
        weekStart: '2026-07-13',
        capacityHours: 40,
        allocatedHours: allocatedHours / 2,
        remainingHours: 40 - allocatedHours / 2,
        allocationPercent: Math.round(allocatedHours / 80 * 100),
        state: over ? 'OverCapacity' : 'Available',
        estimatedPoints: 0,
        unestimatedItems: 1,
        scheduledItems: 0
      }],
      tasks: []
    }],
    teams: [{
      teamId: team.id,
      members: 1,
      capacityHours: 80,
      allocatedHours,
      remainingHours: 80 - allocatedHours,
      state: over ? 'OverCapacity' : 'Available',
      openItems: 2,
      unestimatedItems: 1
    }],
    projects: projects.map((projectItem, index) => ({
      projectId: projectItem.id,
      key: projectItem.key,
      name: projectItem.name,
      allocatedPeople: index === 0 ? 1 : 0,
      allocatedHours: index === 0 ? allocatedHours : 0,
      openItems: index === 0 ? 2 : 0,
      estimatedPoints: index === 0 ? 5 : 0,
      unestimatedItems: index === 0 ? 1 : 0
    }))
  };
}

function envelope(data) {
  return JSON.stringify({
    success: true,
    data,
    error: null,
    correlationId: 'v3-feature-006'
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
  const ready = plan('capacity-ready', 'Teslimat kapasitesi', actor);
  const partial = plan('capacity-partial', 'Mobil yayılım kapasitesi', actor, true);
  if (path === '/api/browser-auth/session') {
    return json(route, { user: actor, csrfToken: 'csrf' });
  }
  if (path === '/api/projects' || path === '/api/projects/') return json(route, projects);
  if (path === '/api/projects/project-atlas') return json(route, projects[0]);
  if (path === '/api/boards/by-project/project-atlas') return json(route, []);
  if (path === '/api/auth/users') return json(route, users);
  if (path === '/api/teams' || path === '/api/teams/') return json(route, [team]);
  if (path === '/api/portfolios' || path === '/api/portfolios/') {
    return json(route, { items: [portfolio], page: 1, pageSize: 100, total: 1 });
  }
  if (path === '/api/capacity-plans' || path === '/api/capacity-plans/') {
    return json(route, {
      items: actor.id === owner.id ? [ready, partial] : [ready],
      page: 1,
      pageSize: 100,
      total: actor.id === owner.id ? 2 : 1
    });
  }
  if (path === '/api/capacity-plans/capacity-ready') {
    return json(route, ready, { ETag: '"3"' });
  }
  if (path === '/api/capacity-plans/capacity-partial') {
    return json(route, partial, { ETag: '"3"' });
  }
  if (path === '/api/capacity-plans/capacity-ready/snapshot') {
    return json(route, snapshot(ready));
  }
  if (path === '/api/capacity-plans/capacity-partial/snapshot') {
    return json(route, snapshot(partial, true));
  }
  if (path === '/api/capacity-plans/capacity-ready/scenarios'
      && request.method() === 'POST') {
    return json(route, {
      planId: ready.id,
      planVersion: ready.version,
      baseline: snapshot(ready),
      candidate: snapshot(ready, false, 88)
    });
  }
  if (path.startsWith('/api/notifications')) return json(route, []);
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
    `${server.origin}/desktop-bulma/index.html#section=capacity&project=project-atlas`,
    { waitUntil: 'networkidle' }
  );
  await desktop.getByRole('heading', { name: 'Kapasite planları' }).waitFor();
  await desktop.getByText('Teslimat kapasitesi', { exact: true }).first().waitFor();
  const summaryText = await desktop.locator('.capacity-summary').innerText();
  assert.match(summaryText, /80[.,]0 sa/);
  assert.match(summaryText, /5[.,]0 puan/);
  assert.match(await desktop.locator('.capacity-method').innerText(), /dönüştürülmez/);
  assert.doesNotMatch(await desktop.locator('.capacity-workspace').innerText(), /owner-1|project-atlas/);
  checks.push('desktop-hours-points-named-summary');

  assert.equal(await desktop.locator('.capacity-week').count(), 2);
  await desktop.getByRole('tab', { name: 'Projeler' }).click();
  assert.match(await desktop.locator('.capacity-table').innerText(), /Atlas Teslimat/);
  assert.match(await desktop.locator('.capacity-table').innerText(), /Mobil Dönüşüm/);
  checks.push('desktop-weekly-people-project-views');
  await capture(desktop, 'desktop-ready.png');

  await desktop.getByRole('button', { name: /Mobil yayılım kapasitesi/ }).click();
  await desktop.locator('.capacity-partial').waitFor();
  assert.match(await desktop.locator('.capacity-partial').innerText(), /1 proje/);
  checks.push('desktop-partial-source-explicit');

  await desktop.getByRole('button', { name: /Teslimat kapasitesi/ }).click();
  await desktop.getByRole('tab', { name: 'Senaryo' }).click();
  await desktop.getByRole('button', { name: 'Tahsis ekle' }).click();
  const candidateRow = desktop.locator('.capacity-panel .capacity-allocation-row').last();
  await candidateRow.getByLabel('Proje').selectOption({ label: 'Mobil Dönüşüm' });
  await candidateRow.getByLabel('Oran %').fill('50');
  await desktop.getByRole('button', { name: 'Senaryoyu hesapla' }).click();
  await desktop.locator('.capacity-scenario-summary').waitFor();
  assert.match(await desktop.locator('.capacity-scenario-summary').innerText(), /88[.,]0 sa/);
  assert.match(await desktop.locator('.capacity-scenario-summary').innerText(), /\+40[.,]0 sa/);
  assert.match(await desktop.locator('.capacity-feedback.success').innerText(), /değiştirilmedi/);
  checks.push('desktop-readonly-scenario-comparison');
  await capture(desktop, 'desktop-scenario.png');
  await desktopContext.close();

  const mobileContext = await contextFor(viewer, { width: 390, height: 844 });
  const mobile = await mobileContext.newPage();
  diagnostics(mobile, 'mobile-viewer');
  await mobile.goto(`${server.origin}/mobile-ionic/index.html#/capacity`, {
    waitUntil: 'networkidle'
  });
  await mobile.getByText('Teslimat kapasitesi', { exact: true }).first().waitFor();
  await mobile.locator('.mobile-capacity-readonly').waitFor();
  assert.equal(await mobile.getByRole('tab', { name: 'Senaryo' }).count(), 0);
  assert.equal(await mobile.getByRole('button', { name: 'Kapasite planı oluştur' }).count(), 0);
  assert.match(await mobile.locator('.mobile-capacity-metrics').innerText(), /80[.,]0/);
  const dimensions = await mobile.evaluate(() => ({
    width: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
    minimumActionHeight: Math.min(...Array.from(
      document.querySelectorAll('.mobile-capacity-tabs button, .mobile-capacity-head button')
    ).map(element => element.getBoundingClientRect().height))
  }));
  assert.ok(dimensions.scrollWidth <= dimensions.width + 1);
  assert.ok(dimensions.minimumActionHeight >= 44);
  checks.push('mobile-readonly-no-overflow-touch-targets');
  await capture(mobile, 'mobile-viewer.png');
  await mobileContext.close();

  assert.deepEqual(failures, [], failures.join('\n'));
} finally {
  await browser.close();
  await server.close();
  await writeFile(resolve(output, 'result.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-FEATURE-006',
    mode: 'deterministic-browser',
    passed: failures.length === 0 && checks.length === 5,
    viewports: ['1440x1000', '390x844'],
    checks,
    failures,
    noDeployment: true
  }, null, 2)}\n`, 'utf8');
}

assert.equal(checks.length, 5);
console.log('V3-FEATURE-006 browser passed: separate units, weekly views, partial sources, scenario and mobile authority.');
