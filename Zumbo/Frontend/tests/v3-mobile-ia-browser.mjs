import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-mobile-001');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const checks = [];
const failures = [];
const searchRequests = [];
const user = {
  id: 'mobile-user',
  username: 'deniz',
  displayName: 'Deniz Aras',
  email: 'deniz@zumbo.local',
  organizationId: 'org-mobile',
  roles: ['User']
};
const project = {
  id: 'project-mobile',
  key: 'MOB',
  name: 'Mobil Teslimat',
  visibility: 'Organization',
  members: [{ userId: user.id, role: 'Member' }]
};
const task = {
  id: 'task-mobile',
  projectId: project.id,
  title: 'Mobil arama ve gezinme kontrolü',
  type: 'Task',
  status: 'In Progress',
  priority: 'High',
  assigneeUserId: user.id,
  rank: 100
};

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-mobile-001' });
}

async function createContext(viewport, options = {}) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    timezoneId: 'Europe/Istanbul'
  });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    const path = url.pathname;
    let data = [];

    if (path === '/api/browser-auth/session') {
      data = { user, csrfToken: 'csrf-mobile-001' };
    } else if (path === '/api/projects') {
      data = url.searchParams.get('archived') === 'true' || options.noProjects ? [] : [project];
    } else if (path === '/api/teams') {
      data = [];
    } else if (path === `/api/work-items/reports/project-summary/${project.id}`) {
      data = { total: 8, inProgress: 3, done: 4, overdue: 1 };
    } else if (path === '/api/work-items/search') {
      const body = request.postDataJSON() || {};
      if (body.text) searchRequests.push(body);
      if (body.text === 'hata') {
        return route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({
            success: false,
            data: null,
            error: { code: 'SEARCH_UNAVAILABLE', message: 'Arama şu anda kullanılamıyor.' },
            correlationId: 'v3-mobile-001'
          })
        });
      }
      data = {
        items: body.text === 'boş' ? [] : [task],
        page: body.page || 1,
        pageSize: body.pageSize || 50,
        degraded: body.text === 'sinirli'
      };
    } else if (path === `/api/boards/by-project/${project.id}`) {
      data = [{ id: 'board-mobile', projectId: project.id, name: 'Teslimat panosu', columns: [] }];
    } else if (path === `/api/work-item-schemas/${project.id}`) {
      data = {
        issueTypes: [{ key: 'Task', name: 'Görev', active: true }],
        customFields: [],
        layouts: [{ issueTypeKey: 'Task', fieldKeys: [] }]
      };
    } else if (path === `/api/notifications/${user.id}`) {
      data = [{
        id: 'notification-mobile',
        type: 'Mention',
        message: 'Teslimat görevinde sizden görüş istendi.',
        read: false,
        createdAt: new Date().toISOString()
      }];
    } else if (path.includes('/hubs/work-items')) {
      data = {};
    }

    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error' && !/WebSocket|signalr|Failed to load resource/.test(message.text())) {
      failures.push(`${label}: ${message.text()}`);
    }
  });
}

async function assertNoOverflow(page) {
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
}

try {
  for (const width of [360, 390, 430]) {
    const context = await createContext({ width, height: width === 360 ? 780 : 844 });
    const page = await context.newPage();
    diagnostics(page, `home-${width}`);
    await page.goto(`${server.origin}/mobile-ionic/index.html#/app/dashboard`, {
      waitUntil: 'domcontentloaded'
    });
    const tabs = page.locator('.zumbo-primary-tabs .tab-item');
    await tabs.first().waitFor();
    assert.equal(await tabs.count(), 5);
    assert.deepEqual(
      await tabs.locator('.tab-title').allTextContents(),
      ['Ana sayfa', 'İşlerim', 'Oluştur', 'Gelen kutusu', 'Daha fazla']
    );
    const tabBoxes = await tabs.evaluateAll(elements => elements.map(element => {
      const box = element.getBoundingClientRect();
      return { width: box.width, height: box.height };
    }));
    assert.ok(tabBoxes.every(box => box.width >= 44 && box.height >= 44));
    await page.locator('.mobile-home-actions').waitFor();
    await page.waitForFunction(() => document.querySelector('.metric-band strong')?.textContent.trim() === '8');
    await assertNoOverflow(page);
    await page.screenshot({ path: resolve(output, `home-${width}.png`), fullPage: true });
    checks.push(`home-tabs-touch-and-overflow-${width}`);
    await context.close();
  }

  const flowContext = await createContext({ width: 390, height: 844 });
  const flowPage = await flowContext.newPage();
  diagnostics(flowPage, 'create-search-more');
  await flowPage.goto(`${server.origin}/mobile-ionic/index.html#/app/more`, {
    waitUntil: 'domcontentloaded'
  });
  await flowPage.getByRole('heading', { name: 'Daha fazla' }).waitFor();
  await flowPage.waitForFunction(() => document.querySelectorAll('.zumbo-primary-tabs .tab-item').length === 5);
  assert.equal(await flowPage.locator('.mobile-more-nav > button').count(), 3);
  await assertNoOverflow(flowPage);
  await flowPage.screenshot({ path: resolve(output, 'more-390.png'), fullPage: true });
  checks.push('more-context-and-secondary-navigation');

  await flowPage.getByRole('button', { name: /Arama/ }).click();
  const searchInput = flowPage.getByPlaceholder('Başlık veya içerik ara');
  await searchInput.waitFor();
  await searchInput.fill('sinirli');
  await flowPage.getByRole('button', { name: 'Ara', exact: true }).click();
  await flowPage.getByText('Mobil arama ve gezinme kontrolü').waitFor();
  await flowPage.locator('.mobile-degraded-state').waitFor();
  assert.equal(searchRequests.at(-1).projectId, project.id);
  await assertNoOverflow(flowPage);
  await flowPage.screenshot({ path: resolve(output, 'search-degraded-390.png'), fullPage: true });
  checks.push('search-result-and-degraded-state');

  await searchInput.fill('boş');
  await flowPage.getByRole('button', { name: 'Ara', exact: true }).click();
  await flowPage.getByRole('heading', { name: 'Sonuç bulunamadı' }).waitFor();
  await searchInput.fill('hata');
  await flowPage.getByRole('button', { name: 'Ara', exact: true }).click();
  await flowPage.getByRole('alert').waitFor();
  assert.match(await flowPage.getByRole('alert').innerText(), /kullanılamıyor/i);
  checks.push('search-empty-and-error-states');

  await flowPage.locator('.zumbo-primary-tabs .tab-item').filter({ hasText: 'Oluştur' }).click();
  await flowPage.getByRole('heading', { name: 'Görev oluştur' }).waitFor();
  assert.match(await flowPage.locator('.mobile-create-entry select').locator('option').nth(1).innerText(), /MOB/);
  await flowPage.getByRole('button', { name: 'Görev ayrıntılarına geç' }).click();
  await flowPage.locator('.popup-container').waitFor();
  assert.match(await flowPage.locator('.popup-title').innerText(), /Yeni iş/i);
  await flowPage.locator('.popup-buttons .button').first().click();
  await assertNoOverflow(flowPage);
  checks.push('create-project-context-and-existing-task-form');
  await flowContext.close();

  const offlineContext = await createContext({ width: 430, height: 844 });
  const offlinePage = await offlineContext.newPage();
  diagnostics(offlinePage, 'create-offline');
  await offlinePage.goto(`${server.origin}/mobile-ionic/index.html#/app/create`, {
    waitUntil: 'domcontentloaded'
  });
  await offlinePage.getByRole('heading', { name: 'Görev oluştur' }).waitFor();
  await offlinePage.evaluate(async () => {
    if ('serviceWorker' in navigator) await navigator.serviceWorker.ready;
  });
  await offlineContext.setOffline(true);
  await offlinePage.evaluate(() => window.dispatchEvent(new window.Event('offline')));
  await offlinePage.locator('.mobile-shell-offline').waitFor();
  assert.equal(await offlinePage.getByRole('button', { name: 'Görev ayrıntılarına geç' }).isDisabled(), true);
  assert.equal(await offlinePage.evaluate(() => {
    const banner = document.querySelector('.mobile-pwa-state').getBoundingClientRect();
    const heading = document.querySelector('.mobile-shell-heading').getBoundingClientRect();
    return banner.bottom <= heading.top;
  }), true);
  await offlinePage.screenshot({ path: resolve(output, 'create-offline-430.png'), fullPage: true });
  checks.push('create-offline-readonly');
  await offlineContext.close();

  const emptyContext = await createContext({ width: 360, height: 780 }, { noProjects: true });
  const emptyPage = await emptyContext.newPage();
  diagnostics(emptyPage, 'create-empty');
  await emptyPage.goto(`${server.origin}/mobile-ionic/index.html#/app/create`, {
    waitUntil: 'domcontentloaded'
  });
  await emptyPage.getByRole('heading', { name: 'Kullanılabilir proje yok' }).waitFor();
  await assertNoOverflow(emptyPage);
  checks.push('create-empty-project-state');
  await emptyContext.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-MOBILE-001',
  passed: failures.length === 0,
  viewports: ['360x780', '390x844', '430x844'],
  checks,
  failures
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 9);
console.log(JSON.stringify(result, null, 2));
