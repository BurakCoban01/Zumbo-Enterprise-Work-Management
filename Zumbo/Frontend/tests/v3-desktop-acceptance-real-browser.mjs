import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { freemem } from 'node:os';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import {
  additionalStateIds,
  expectedMatrixCaptureCount,
  externalScenarioGates,
  isMatrixState,
  matrixState,
  profiles,
  projectSurfaces,
  projectView,
  section,
  sectionSurfaces
} from './v3-desktop-acceptance-contract.mjs';
import { apiBaseUrl, frontendBaseUrl, requireLocalSecret } from './environment.mjs';
import { createCleanupLedger, createRunContext } from './e2e-run-context.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-qa-001/desktop-matrix');
const frontendOrigin = new URL(frontendBaseUrl).origin;
const apiOrigin = new URL(apiBaseUrl).origin;
const runContext = createRunContext('V3-QA-001-matrix', 'chromium');
const tenantId = runContext.tenants.desktop;
const cleanupLedger = createCleanupLedger();
const adminEmail = requireLocalSecret('ZUMBO_IDENTITY_ADMIN_EMAIL', 'for V3-QA-001 tenant cleanup');
const adminBootstrapToken = requireLocalSecret('ZUMBO_IDENTITY_BOOTSTRAP_TOKEN', 'for V3-QA-001 tenant cleanup');
const password = 'P@ssword123';
const minimumFreeMemoryMiB = Number(process.env.ZUMBO_QA_MATRIX_MIN_FREE_MEMORY_MIB || 1024);
const observedFreeMemoryMiB = Math.floor(freemem() / 1024 / 1024);

let browser;
let cleanupAdminTokenPromise;
let cleanupResult = { attempted: 0, passed: 0, failed: 0, results: [] };
const captures = [];
const diagnostics = [];
const stateCoverage = [];
const externalOrigins = new Set();
let fixture;

await mkdir(outputDir, { recursive: true });

async function apiRequest(path, method = 'GET', body, token) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json().catch(() => ({}));
  return { response, payload, data: payload.data };
}

async function requireApi(path, method, body, token, label) {
  const result = await apiRequest(path, method, body, token);
  assert.ok(result.response.ok, result.payload.error?.message || `${label} failed with HTTP ${result.response.status}`);
  return result.data;
}

async function cleanupAdminToken() {
  if (!cleanupAdminTokenPromise) {
    cleanupAdminTokenPromise = (async () => {
      const authentication = await apiRequest('/api/auth/login', 'POST', { usernameOrEmail: adminEmail, password });
      assert.ok(authentication.response.ok, authentication.payload.error?.message || 'Cleanup administrator authentication failed');
      return authentication.data.accessToken;
    })();
  }
  return cleanupAdminTokenPromise;
}

async function archiveTenant() {
  const token = await cleanupAdminToken();
  const result = await apiRequest(`/api/organizations/${encodeURIComponent(tenantId)}/archive`, 'POST', undefined, token);
  if (result.response.ok || result.response.status === 404) return { tenantId, status: result.response.status };
  throw new Error(result.payload.error?.message || `Tenant cleanup failed with HTTP ${result.response.status}`);
}

async function browserContextLogin(context, usernameOrEmail) {
  const response = await context.request.post(`${apiBaseUrl}/api/browser-auth/login`, {
    headers: { Origin: frontendOrigin },
    data: { usernameOrEmail, password }
  });
  const payload = await response.json();
  assert.ok(response.ok(), payload.error?.message || 'Browser context login failed');
  await context.addInitScript(auth => {
    localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
    sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
  }, payload.data);
}

async function createProject(owner, token, key, name, withBoard, visibility = 'Internal') {
  const project = await requireApi('/api/projects', 'POST', {
    organizationId: tenantId,
    key,
    name,
    visibility,
    ownerUserId: owner.id
  }, token, `${name} project creation`);
  const board = withBoard
    ? await requireApi('/api/boards', 'POST', {
      projectId: project.id,
      name: `${name} Panosu`,
      type: 'Kanban'
    }, token, `${name} board creation`)
    : null;
  return { project, board };
}

async function createFixture() {
  const stamp = runContext.runId.replace(/[^a-z0-9]/g, '').slice(-10);
  const ownerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `qaowner${stamp}`,
    email: adminEmail,
    password,
    organizationId: tenantId,
    bootstrapToken: adminBootstrapToken
  }, undefined, 'Owner bootstrap registration');
  const owner = ownerRegistration.user;
  const ownerToken = ownerRegistration.accessToken;
  cleanupAdminTokenPromise = Promise.resolve(ownerToken);
  await requireApi('/api/organizations', 'POST', {
    name: 'Zumbo Sentetik Kalite Organizasyonu',
    tenantKey: tenantId
  }, ownerToken, 'Organization creation');
  cleanupLedger.add(`archive:${tenantId}`, archiveTenant);

  const team = await requireApi('/api/teams', 'POST', {
    organizationId: tenantId,
    name: 'Sentetik Teslimat Ekibi',
    ownerUserId: owner.id
  }, ownerToken, 'Team creation');
  const viewerEmail = `qaviewer${stamp}@zumbo.local`;
  const invitation = await requireApi(`/api/teams/${team.id}/members`, 'POST', {
    email: viewerEmail,
    role: 'Member'
  }, ownerToken, 'Viewer invitation');
  const viewerRegistration = await requireApi('/api/auth/register', 'POST', {
    username: `qaviewer${stamp}`,
    email: viewerEmail,
    password,
    organizationId: tenantId
  }, undefined, 'Viewer registration');
  await requireApi(`/api/teams/${team.id}/invites/accept`, 'POST', {
    token: invitation.invitationToken
  }, viewerRegistration.accessToken, 'Viewer invitation acceptance');

  const keySuffix = stamp.slice(-5).toUpperCase();
  const delivery = await createProject(owner, ownerToken, `QA${keySuffix}`, 'Kalite Teslimat Merkezi', true);
  const noBoard = await createProject(owner, ownerToken, `QE${keySuffix}`, 'Panosuz Hazırlık Alanı', false);
  const restricted = await createProject(owner, ownerToken, `QR${keySuffix}`, 'Kısıtlı Strateji Alanı', true, 'Private');
  for (const projectFixture of [delivery, noBoard]) {
    await requireApi(`/api/projects/${projectFixture.project.id}/members`, 'POST', {
      userId: viewerRegistration.user.id,
      role: 'Viewer'
    }, ownerToken, 'Viewer project grant');
  }
  await requireApi(`/api/projects/${delivery.project.id}/teams`, 'POST', { teamId: team.id }, ownerToken, 'Project team link');
  await requireApi(`/api/projects/${delivery.project.id}/milestones`, 'POST', {
    name: 'Müşteri kabul kapısı',
    dueAt: new Date(Date.now() + 12 * 86400000).toISOString()
  }, ownerToken, 'Milestone creation');

  const sprint = await requireApi('/api/sprints', 'POST', {
    projectId: delivery.project.id,
    name: 'Kalite Sprinti',
    goal: 'Masaüstü kabul matrisini güvenilir biçimde doğrula',
    startDate: new Date(Date.now() - 2 * 86400000).toISOString().slice(0, 10),
    endDate: new Date(Date.now() + 12 * 86400000).toISOString().slice(0, 10)
  }, ownerToken, 'Sprint creation');
  const priorities = ['Critical', 'High', 'Medium', 'Low'];
  const tasks = [];
  for (let index = 0; index < 12; index += 1) {
    const task = await requireApi('/api/work-items', 'POST', {
      projectId: delivery.project.id,
      boardId: delivery.board.id,
      title: `Sentetik kabul işi ${String(index + 1).padStart(2, '0')}`,
      type: index % 4 === 0 ? 'Bug' : 'Task',
      priority: priorities[index % priorities.length],
      assigneeUserId: index % 3 === 0 ? viewerRegistration.user.id : owner.id,
      dueDate: new Date(Date.now() + (index - 2) * 86400000).toISOString()
    }, ownerToken, `Work item ${index + 1} creation`);
    tasks.push(task);
    if (index < 6) {
      await requireApi(`/api/sprints/${sprint.id}/items/${task.id}`, 'PUT', {
        estimatePoints: (index % 5) + 1
      }, ownerToken, `Sprint item ${index + 1} planning`);
    }
  }
  return {
    owner,
    ownerToken,
    viewer: viewerRegistration.user,
    viewerToken: viewerRegistration.accessToken,
    team,
    delivery,
    noBoard,
    restricted,
    sprint,
    tasks
  };
}

function attachDiagnostics(page, label) {
  page.on('pageerror', error => diagnostics.push(`${label}: page error: ${error.message}`));
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const detail = message.text();
    if (detail.includes('/hubs/work-items') || detail.includes('Failed to start the connection')) return;
    diagnostics.push(`${label}: console error: ${detail}`);
  });
  page.on('response', response => {
    const status = response.status();
    if (response.url().startsWith(apiBaseUrl) && (status === 429 || status >= 500)) {
      diagnostics.push(`${label}: HTTP ${status} ${new URL(response.url()).pathname}`);
    }
  });
  page.on('requestfailed', request => {
    if (request.url().includes('/hubs/work-items')) return;
    diagnostics.push(`${label}: request failed: ${request.failure()?.errorText || 'unknown'} ${request.url()}`);
  });
  page.on('request', request => {
    const url = new URL(request.url());
    if (!['http:', 'https:'].includes(url.protocol)) return;
    if (url.origin !== frontendOrigin && url.origin !== apiOrigin) externalOrigins.add(url.origin);
  });
}

async function createProfilePage(profileDefinition, username) {
  const context = await browser.newContext({
    viewport: { width: profileDefinition.width, height: profileDefinition.height },
    reducedMotion: profileDefinition.reducedMotion
  });
  await context.addInitScript(preferences => {
    localStorage.setItem('zumbo.theme', preferences.theme);
    localStorage.setItem('zumbo.density', preferences.density);
    localStorage.setItem('zumbo.navCollapsed', 'false');
  }, profileDefinition);
  await browserContextLogin(context, username);
  const page = await context.newPage();
  attachDiagnostics(page, profileDefinition.id);
  return { context, page };
}

function routeFor(surface, projectFixture = fixture.delivery) {
  const params = new globalThis.URLSearchParams({
    section: surface.section,
    project: projectFixture.project.id
  });
  if (projectFixture.board) params.set('board', projectFixture.board.id);
  if (surface.kind === 'project-view') params.set('view', surface.id);
  return params.toString();
}

async function navigate(page, surface, projectFixture = fixture.delivery) {
  const route = routeFor(surface, projectFixture);
  await page.evaluate(nextRoute => {
    window.history.pushState(null, '', `#${nextRoute}`);
    window.dispatchEvent(new window.PopStateEvent('popstate'));
  }, route);
  await page.waitForFunction(expected => {
    const scope = window.angular?.element(document.body).scope();
    return scope?.vm?.activeSection === expected;
  }, surface.section, { timeout: 45_000 });
  if (surface.kind === 'project-view') {
    await page.locator(`.project-view-switcher [data-view="${surface.id}"][aria-selected="true"]`)
      .waitFor({ state: 'visible', timeout: 45_000 });
  }
  await page.locator(surface.selector).first().waitFor({ state: 'visible', timeout: 45_000 });
  await page.waitForFunction(selector => {
    const visible = element => {
      const style = window.getComputedStyle(element);
      return element.getClientRects().length > 0 && style.visibility !== 'hidden' && style.display !== 'none';
    };
    const candidates = Array.from(document.querySelectorAll(selector));
    const root = candidates.find(visible);
    if (!root) return false;
    if (root.matches('[data-busy="true"], [data-loading="true"], [data-settings-ready="false"]')) return false;
    if (root.querySelector('[data-busy="true"], [data-loading="true"], [data-settings-ready="false"]')) return false;
    const transient = Array.from(root.querySelectorAll('[class*="loading"], [class*="skeleton"]')).some(visible);
    return !transient && !/yükleniyor|hazırlanıyor/i.test(root.innerText);
  }, surface.selector, { timeout: 45_000 });
  return route;
}

async function assertSurface(page, surface, profileDefinition, evidenceState = 'normal') {
  const state = await page.evaluate(({ expectedSection, expectedView, selector, theme, density, reducedMotion }) => {
    const visible = element => {
      const style = window.getComputedStyle(element);
      return element.getClientRects().length > 0 && style.visibility !== 'hidden' && style.display !== 'none';
    };
    const mains = Array.from(document.querySelectorAll('main')).filter(visible);
    const scope = window.angular?.element(document.body).scope();
    const bodyText = document.body.innerText;
    const surfaceText = Array.from(document.querySelectorAll(selector)).find(visible)?.innerText || '';
    const root = document.documentElement;
    const bodyStyle = window.getComputedStyle(document.body);
    const durations = reducedMotion === 'reduce'
      ? Array.from(document.querySelectorAll('*')).filter(visible).flatMap(element => {
        const style = window.getComputedStyle(element);
        return `${style.animationDuration},${style.transitionDuration}`.split(',')
          .map(value => Number.parseFloat(value) || 0);
      })
      : [];
    return {
      activeSection: scope?.vm?.activeSection,
      activeView: scope?.vm?.workMode,
      summaryTotal: Number(scope?.vm?.summary?.total || 0),
      mainCount: mains.length,
      textLength: bodyText.trim().length,
      emptyStateVisible: /henüz|bulunamadı|yok\.|boş\.|oluşturun/i.test(surfaceText),
      visibleGuid: bodyText.match(/\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b/i)?.[0] || null,
      scrollWidth: Math.max(root.scrollWidth, document.body.scrollWidth),
      clientWidth: root.clientWidth,
      themeClass: document.body.classList.contains('theme-dark') ? 'dark' : 'light',
      densityClass: document.body.classList.contains('density-compact') ? 'compact' : 'comfortable',
      reducedMotionMatches: window.matchMedia('(prefers-reduced-motion: reduce)').matches,
      maximumMotionSeconds: durations.length ? Math.max(...durations) : null,
      bodyBackground: bodyStyle.backgroundColor,
      expectedSection,
      expectedView,
      expectedTheme: theme,
      expectedDensity: density
    };
  }, {
    expectedSection: surface.section,
    expectedView: surface.kind === 'project-view' ? surface.id : null,
    selector: surface.selector,
    theme: profileDefinition.theme,
    density: profileDefinition.density,
    reducedMotion: profileDefinition.reducedMotion
  });
  assert.equal(state.activeSection, surface.section, `${surface.id} active section drifted`);
  if (surface.kind === 'project-view') assert.equal(state.activeView, surface.id, `${surface.id} active view drifted`);
  if (evidenceState === 'high-data') {
    assert.ok(state.summaryTotal >= 12, `${surface.id} high-data state exposed only ${state.summaryTotal} summarized tasks`);
  }
  if (evidenceState === 'empty') {
    assert.equal(state.emptyStateVisible, true, `${surface.id} did not expose a visible empty-state message`);
  }
  assert.equal(state.mainCount, 1, `${surface.id} exposed ${state.mainCount} visible main landmarks`);
  assert.ok(state.textLength > 80, `${surface.id} rendered insufficient visible content`);
  assert.equal(state.visibleGuid, null, `${surface.id} exposed opaque identifier ${state.visibleGuid}`);
  assert.ok(state.scrollWidth <= state.clientWidth + 1, `${surface.id} overflowed ${state.scrollWidth}/${state.clientWidth}`);
  assert.equal(state.themeClass, profileDefinition.theme, `${surface.id} theme class drifted`);
  assert.equal(state.densityClass, profileDefinition.density, `${surface.id} density class drifted`);
  assert.equal(state.reducedMotionMatches, profileDefinition.reducedMotion === 'reduce');
  if (profileDefinition.reducedMotion === 'reduce') {
    assert.ok(state.maximumMotionSeconds <= 0.01, `${surface.id} retained ${state.maximumMotionSeconds}s reduced-motion animation`);
  }
  assert.notEqual(state.bodyBackground, 'rgba(0, 0, 0, 0)', `${surface.id} rendered a transparent page background`);

  await page.locator('#main-workspace.workspace').focus();
  await page.keyboard.press('Tab');
  const focus = await page.evaluate(() => {
    const element = document.activeElement;
    const style = element ? window.getComputedStyle(element) : null;
    return {
      tag: element?.tagName || null,
      outlineStyle: style?.outlineStyle || 'none',
      outlineWidth: Number.parseFloat(style?.outlineWidth || '0') || 0,
      boxShadow: style?.boxShadow || 'none'
    };
  });
  assert.ok(focus.tag && focus.tag !== 'BODY', `${surface.id} keyboard navigation did not move focus`);
  assert.ok(
    (focus.outlineStyle !== 'none' && focus.outlineWidth > 0) || focus.boxShadow !== 'none',
    `${surface.id} keyboard focus indicator was not visible`
  );
  return { ...state, focus };
}

async function captureSurface(page, surface, profileDefinition, state = 'normal', projectFixture = fixture.delivery) {
  const diagnosticsBefore = diagnostics.length;
  const route = await navigate(page, surface, projectFixture);
  const assertions = await assertSurface(page, surface, profileDefinition, state);
  assert.equal(
    diagnostics.length,
    diagnosticsBefore,
    diagnostics.slice(diagnosticsBefore).join('\n')
  );
  const fileName = `${profileDefinition.id}--${surface.kind}-${surface.id}--${state}.png`;
  const image = await page.screenshot({ path: resolve(outputDir, fileName), fullPage: true });
  const capture = {
    surface: surface.id,
    kind: surface.kind,
    state,
    route,
    profile: profileDefinition.id,
    viewport: { width: profileDefinition.width, height: profileDefinition.height },
    nominalWidth: profileDefinition.nominalWidth,
    zoomPercent: profileDefinition.zoomPercent,
    theme: profileDefinition.theme,
    density: profileDefinition.density,
    reducedMotion: profileDefinition.reducedMotion,
    screenshot: fileName,
    bytes: image.length,
    sha256: createHash('sha256').update(image).digest('hex'),
    assertions
  };
  captures.push(capture);
  return capture;
}

async function runProfile(profileDefinition) {
  const { context, page } = await createProfilePage(profileDefinition, fixture.owner.username);
  try {
    await page.goto(
      `${frontendBaseUrl}/desktop-bulma/index.html#${routeFor(projectView('overview', 'board', '.project-overview'))}`,
      { waitUntil: 'domcontentloaded' }
    );
    await page.locator('.project-overview').waitFor({ state: 'visible', timeout: 45_000 });
    for (const surface of [...sectionSurfaces, ...projectSurfaces]) {
      await captureSurface(page, surface, profileDefinition, matrixState(surface));
    }
  } finally {
    await context.close();
  }
}

async function runLoadingState() {
  const profileDefinition = profiles[1];
  const { context, page } = await createProfilePage(profileDefinition, fixture.owner.username);
  const workItemsPattern = `${apiBaseUrl}/api/work-items**`;
  let releaseRequests;
  let interceptedRequests = 0;
  const heldRequests = new Promise(resolveHeldRequests => {
    releaseRequests = resolveHeldRequests;
  });
  try {
    await page.goto(
      `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${fixture.delivery.project.id}&board=${fixture.delivery.board.id}&view=board`,
      { waitUntil: 'domcontentloaded' }
    );
    await page.locator('.board-shell').waitFor({ state: 'visible', timeout: 45_000 });
    await page.route(workItemsPattern, async route => {
      interceptedRequests += 1;
      await heldRequests;
      await route.continue();
    });
    await page.reload({ waitUntil: 'domcontentloaded' });
    const skeleton = page.locator('.board-skeleton[aria-label="Pano yükleniyor"]');
    await skeleton.waitFor({ state: 'visible', timeout: 10_000 });
    assert.ok(interceptedRequests > 0, 'Loading state did not hold a real work-item request.');
    const dimensions = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
      mainCount: Array.from(document.querySelectorAll('main')).filter(element => element.getClientRects().length > 0).length
    }));
    assert.equal(dimensions.mainCount, 1);
    assert.ok(dimensions.scrollWidth <= dimensions.clientWidth + 1);
    const fileName = `${profileDefinition.id}--project-view-board--loading.png`;
    const image = await page.screenshot({ path: resolve(outputDir, fileName), fullPage: true });
    captures.push({
      surface: 'board',
      kind: 'project-view',
      state: 'loading',
      route: await locationHash(page),
      profile: profileDefinition.id,
      viewport: { width: profileDefinition.width, height: profileDefinition.height },
      nominalWidth: profileDefinition.nominalWidth,
      zoomPercent: profileDefinition.zoomPercent,
      theme: profileDefinition.theme,
      density: profileDefinition.density,
      reducedMotion: profileDefinition.reducedMotion,
      screenshot: fileName,
      bytes: image.length,
      sha256: createHash('sha256').update(image).digest('hex'),
      realRequestDelayedWithoutPayloadMock: true
    });
    stateCoverage.push({ state: 'loading', capture: fileName, passed: true });
    releaseRequests();
    await page.locator('.board-shell').waitFor({ state: 'visible', timeout: 45_000 });
  } finally {
    releaseRequests();
    await page.unroute(workItemsPattern);
    await context.close();
  }
}

async function runNoBoardState() {
  const profileDefinition = profiles[1];
  const { context, page } = await createProfilePage(profileDefinition, fixture.viewer.username);
  try {
    await page.goto(
      `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${fixture.noBoard.project.id}&view=board`,
      { waitUntil: 'domcontentloaded' }
    );
    await page.locator('.project-overview').waitFor({ state: 'visible', timeout: 45_000 });
    assert.equal(await page.locator('.project-view-switcher [role="tab"]').count(), 8);
    assert.equal(await page.locator('[data-view="overview"]').getAttribute('aria-selected'), 'true');
    const capture = await captureSurface(page, projectView('overview', 'board', '.project-overview'), profileDefinition, 'empty-no-board', fixture.noBoard);
    stateCoverage.push({ state: 'empty-no-board', capture: capture.screenshot, passed: true });
  } finally {
    await context.close();
  }
}

async function runPermissionState() {
  const profileDefinition = profiles[1];
  const { context, page } = await createProfilePage(profileDefinition, fixture.viewer.username);
  try {
    await page.goto(
      `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${fixture.restricted.project.id}&board=${fixture.restricted.board.id}&view=board`,
      { waitUntil: 'domcontentloaded' }
    );
    const notice = page.getByText('Bu organizasyon içi proje görünür; pano ve iş öğelerine erişmek için proje üyeliği gerekir.', { exact: true });
    await notice.waitFor({ state: 'visible', timeout: 45_000 });
    assert.equal(await page.locator('.project-view-switcher').count(), 0);
    assert.equal(await page.locator('.board-shell').count(), 0);
    assert.equal(await page.locator('.board-management').count(), 0);
    const surface = section('projects', '.management-layout');
    const assertions = await assertSurface(page, surface, profileDefinition);
    const fileName = `${profileDefinition.id}--section-projects--permission.png`;
    const image = await page.screenshot({ path: resolve(outputDir, fileName), fullPage: true });
    captures.push({
      surface: 'projects',
      kind: 'section',
      state: 'permission',
      route: await locationHash(page),
      profile: profileDefinition.id,
      viewport: { width: profileDefinition.width, height: profileDefinition.height },
      nominalWidth: profileDefinition.nominalWidth,
      zoomPercent: profileDefinition.zoomPercent,
      theme: profileDefinition.theme,
      density: profileDefinition.density,
      reducedMotion: profileDefinition.reducedMotion,
      screenshot: fileName,
      bytes: image.length,
      sha256: createHash('sha256').update(image).digest('hex'),
      assertions
    });
    stateCoverage.push({ state: 'permission', capture: fileName, passed: true });
  } finally {
    await context.close();
  }
}

async function runOfflineState() {
  const profileDefinition = profiles[1];
  const { context, page } = await createProfilePage(profileDefinition, fixture.owner.username);
  try {
    await page.goto(
      `${frontendBaseUrl}/desktop-bulma/index.html#section=board&project=${fixture.delivery.project.id}&board=${fixture.delivery.board.id}&view=board`,
      { waitUntil: 'domcontentloaded' }
    );
    await page.locator('.board-shell').waitFor({ state: 'visible', timeout: 45_000 });
    await context.setOffline(true);
    await page.evaluate(() => window.dispatchEvent(new window.Event('offline')));
    const offline = page.locator('.desktop-pwa-state.offline');
    await offline.waitFor({ state: 'visible', timeout: 10_000 });
    assert.match(await offline.innerText(), /Çevrimdışısınız/);
    const fileName = `${profileDefinition.id}--project-view-board--offline.png`;
    const image = await page.screenshot({ path: resolve(outputDir, fileName), fullPage: true });
    captures.push({
      surface: 'board',
      kind: 'project-view',
      state: 'offline',
      route: await locationHash(page),
      profile: profileDefinition.id,
      viewport: { width: profileDefinition.width, height: profileDefinition.height },
      nominalWidth: profileDefinition.nominalWidth,
      zoomPercent: profileDefinition.zoomPercent,
      theme: profileDefinition.theme,
      density: profileDefinition.density,
      reducedMotion: profileDefinition.reducedMotion,
      screenshot: fileName,
      bytes: image.length,
      sha256: createHash('sha256').update(image).digest('hex'),
      degradedMode: 'offline-read-only-shell'
    });
    stateCoverage.push({ state: 'offline', degraded: true, capture: fileName, passed: true });
    await context.setOffline(false);
  } finally {
    await context.close();
  }
}

async function locationHash(page) {
  return page.evaluate(() => location.hash.slice(1));
}

async function writeManifest(status, failure = null) {
  await writeFile(resolve(outputDir, 'desktop-matrix.json'), `${JSON.stringify({
    schemaVersion: 1,
    taskId: 'V3-QA-001',
    runId: runContext.runId,
    generatedAtUtc: new Date().toISOString(),
    status,
    failure,
    backend: 'real-dotnet-api',
    persistenceProvider: 'InMemory',
    apiOrigin,
    frontendOrigin,
    publicExposure: false,
    automatedAcceptanceOnly: true,
    explicitVisualReviewRequired: true,
    visualReviewStatus: 'PendingExplicitReview',
    visualReviewEvidence: 'artifacts/ui/v3-qa-001/desktop-matrix/visual-review.json',
    minimumFreeMemoryMiB,
    observedFreeMemoryMiB,
    expectedMatrixCaptures: expectedMatrixCaptureCount,
    completedMatrixCaptures: captures.filter(capture => isMatrixState(capture.state)).length,
    profiles,
    surfaces: {
      sections: sectionSurfaces.map(surface => surface.id),
      projectViews: projectSurfaces.map(surface => surface.id)
    },
    stateCoverage,
    expectedAdditionalStates: additionalStateIds,
    separateScenarioGates: externalScenarioGates.map(gate => ({
      ...gate,
      status: 'separate-gate-required'
    })),
    externalOrigins: [...externalOrigins],
    diagnostics,
    captures,
    cleanup: cleanupResult
  }, null, 2)}\n`, 'utf8');
}

function additionalStateCoverageComplete() {
  const passedStates = new Set(
    stateCoverage.filter(state => state.passed).map(state => state.state)
  );
  return additionalStateIds.every(state => passedStates.has(state));
}

let runFailure = null;
let runBlocked = false;
try {
  if (observedFreeMemoryMiB < minimumFreeMemoryMiB) {
    runBlocked = true;
    throw new Error(
      `V3-QA-001 desktop matrix requires at least ${minimumFreeMemoryMiB} MiB free physical memory; `
      + `${observedFreeMemoryMiB} MiB is available.`
    );
  }
  browser = await chromium.launch({ headless: true });
  fixture = await createFixture();
  for (const profileDefinition of profiles) await runProfile(profileDefinition);
  await runLoadingState();
  await runNoBoardState();
  await runPermissionState();
  await runOfflineState();
  assert.equal(externalOrigins.size, 0, `External origins were contacted: ${[...externalOrigins].join(', ')}`);
  assert.deepEqual(diagnostics, [], diagnostics.join('\n'));
} catch (error) {
  runFailure = error instanceof Error ? error.message : String(error);
} finally {
  cleanupResult = await cleanupLedger.run();
  await browser?.close();
  const passed = !runFailure
    && cleanupResult.failed === 0
    && captures.filter(capture => isMatrixState(capture.state)).length === expectedMatrixCaptureCount
    && additionalStateCoverageComplete();
  await writeManifest(passed ? 'Passed' : runBlocked ? 'Blocked' : 'Failed', runFailure);
}

assert.equal(runFailure, null, runFailure);
assert.equal(cleanupResult.failed, 0, `Cleanup failures: ${cleanupResult.results.map(result => result.error).filter(Boolean).join(' | ')}`);
console.log(`V3-QA-001 desktop acceptance matrix passed: ${captures.length} real-backend captures.`);
