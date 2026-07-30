import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';
import { apiBaseUrl } from './environment.mjs';
import { buildFrontend } from './build-frontend.mjs';
import { startStaticServer } from './static-server.mjs';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, '../artifacts/ui/v3-mobile-004');
await mkdir(output, { recursive: true });
await buildFrontend();
const server = await startStaticServer(resolve(root, 'dist'));
const browser = await chromium.launch({ headless: true });
const checks = [];
const failures = [];
const captures = [];

const user = {
  id: 'mobile-a11y-user',
  username: 'duru',
  displayName: 'Duru Aksoy Uzun Yerelleştirme Kontrolü',
  email: 'duru@zumbo.local',
  organizationId: 'mobile-a11y-org',
  roles: ['User']
};
const project = {
  id: 'mobile-a11y-project',
  key: 'ERIS',
  name: 'Erişilebilir Mobil Teslimat',
  visibility: 'Organization',
  members: [{ userId: user.id, role: 'Member' }]
};
const task = {
  id: 'mobile-a11y-task',
  projectId: project.id,
  title: 'Uzun başlıklı erişilebilir mobil görev',
  type: 'Task',
  status: 'In Progress',
  priority: 'High',
  assigneeUserId: user.id,
  rank: 100
};

function envelope(data) {
  return JSON.stringify({ success: true, data, error: null, correlationId: 'v3-mobile-004' });
}

async function createContext(viewport, { authenticated = true, reducedMotion = 'reduce' } = {}) {
  const context = await browser.newContext({
    viewport,
    isMobile: true,
    hasTouch: true,
    reducedMotion,
    timezoneId: 'Europe/Istanbul'
  });
  await context.route(`${apiBaseUrl}/**`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, body: '' });
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === '/api/browser-auth/session' && !authenticated) {
      return route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ success: false, error: { code: 'UNAUTHORIZED', message: 'Oturum yok.' } })
      });
    }
    if (path === '/api/browser-auth/login') {
      return route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ success: false, error: { code: 'INVALID_CREDENTIALS', message: 'Giriş başarısız.' } })
      });
    }
    if (path === '/api/auth/forgot-password') {
      return route.fulfill({ status: 200, contentType: 'application/json', body: envelope({ accepted: true }) });
    }

    let data = [];
    if (path === '/api/browser-auth/session') data = { user, csrfToken: 'csrf-mobile-a11y' };
    else if (path === '/api/projects') data = url.searchParams.get('archived') === 'true' ? [] : [project];
    else if (path === '/api/teams') data = [];
    else if (path === `/api/work-items/reports/project-summary/${project.id}`) {
      data = { total: 12, inProgress: 4, done: 7, overdue: 1 };
    } else if (path === '/api/work-items/search') {
      data = { items: [task], page: 1, pageSize: 50, degraded: false };
    } else if (path === `/api/boards/by-project/${project.id}`) {
      data = [{ id: 'mobile-a11y-board', projectId: project.id, name: 'Erişilebilirlik Panosu', columns: [] }];
    } else if (path === `/api/work-item-schemas/${project.id}`) {
      data = { issueTypes: [{ key: 'Task', name: 'Görev', active: true }], customFields: [], layouts: [] };
    } else if (path === `/api/notifications/${user.id}`) {
      data = [{
        id: 'mobile-a11y-notification',
        type: 'Mention',
        message: 'Mobil erişilebilirlik görevinde sizden görüş istendi.',
        read: false,
        createdAt: new Date().toISOString()
      }];
    }
    return route.fulfill({ status: 200, contentType: 'application/json', body: envelope(data) });
  });
  return context;
}

function diagnostics(page, label) {
  page.on('pageerror', error => failures.push(`${label}: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error' && !/Failed to load resource|WebSocket|signalr/i.test(message.text())) {
      failures.push(`${label}: ${message.text()}`);
    }
  });
}

async function surfaceReport(page) {
  return page.evaluate(() => {
    const visible = element => {
      const style = window.getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== 'none'
        && style.visibility !== 'hidden'
        && Number(style.opacity) !== 0
        && rect.width > 0
        && rect.height > 0;
    };
    const accessibleName = element => {
      const labelledBy = element.getAttribute('aria-labelledby');
      const labelled = labelledBy
        ? labelledBy.split(/\s+/).map(id => document.getElementById(id)?.textContent || '').join(' ').trim()
        : '';
      const explicit = element.id
        ? document.querySelector(`label[for="${window.CSS.escape(element.id)}"]`)?.textContent.trim()
        : '';
      return element.getAttribute('aria-label')
        || labelled
        || explicit
        || element.closest('label')?.textContent.trim()
        || element.getAttribute('title')
        || element.textContent.trim();
    };
    const controls = [...document.querySelectorAll(
      'a[href], button, input:not([type="hidden"]), select, textarea, [role="button"], [role="tab"]'
    )].filter(visible);
    const touchTargets = controls
      .filter(element => !element.classList.contains('skip-link'))
      .map(element => {
        const wrapper = /^(INPUT|SELECT|TEXTAREA)$/.test(element.tagName)
          ? element.closest('label') || element
          : element;
        const rect = wrapper.getBoundingClientRect();
        return {
          name: accessibleName(element).slice(0, 80),
          tag: element.tagName,
          width: Math.round(rect.width * 10) / 10,
          height: Math.round(rect.height * 10) / 10
        };
      });
    return {
      language: document.documentElement.lang,
      mainCount: [...document.querySelectorAll('main, [role="main"]')].filter(visible).length,
      missingNames: controls.filter(element => !accessibleName(element)).map(element => element.outerHTML.slice(0, 160)),
      touchViolations: touchTargets.filter(target => target.width < 44 || target.height < 44),
      horizontalOverflow: document.documentElement.scrollWidth - window.innerWidth,
      rawBindings: document.body.innerText.includes('{{')
    };
  });
}

async function assertSurface(page, label) {
  const report = await surfaceReport(page);
  assert.equal(report.language, 'tr', `${label}: document language`);
  assert.equal(report.mainCount, 1, `${label}: visible main landmark`);
  assert.deepEqual(report.missingNames, [], `${label}: missing accessible names`);
  assert.deepEqual(report.touchViolations, [], `${label}: touch target violations`);
  assert.ok(report.horizontalOverflow <= 1, `${label}: ${report.horizontalOverflow}px horizontal overflow`);
  assert.equal(report.rawBindings, false, `${label}: raw Angular binding`);
  return report;
}

async function capture(page, name, state, viewport) {
  await page.screenshot({ path: resolve(output, name), fullPage: true });
  captures.push({ screenshot: `artifacts/ui/v3-mobile-004/${name}`, state, viewport });
}

try {
  for (const width of [360, 390, 430]) {
    const height = width === 360 ? 780 : 844;
    const anonymous = await createContext({ width, height }, { authenticated: false });
    const loginPage = await anonymous.newPage();
    diagnostics(loginPage, `login-${width}`);
    await loginPage.goto(`${server.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'networkidle' });
    await loginPage.locator('.login-entry-surface').waitFor();
    await assertSurface(loginPage, `login-${width}`);
    if (width === 360) await capture(loginPage, 'login-360.png', 'portrait-login', '360x780');
    checks.push(`portrait-login-${width}`);
    await anonymous.close();

    const authenticated = await createContext({ width, height });
    const homePage = await authenticated.newPage();
    diagnostics(homePage, `home-${width}`);
    await homePage.goto(`${server.origin}/mobile-ionic/index.html#/app/dashboard`, { waitUntil: 'networkidle' });
    await homePage.locator('.mobile-home-actions').waitFor();
    await homePage.waitForFunction(() => document.querySelectorAll('.zumbo-primary-tabs .tab-item').length === 5);
    await assertSurface(homePage, `home-${width}`);
    assert.equal(await homePage.locator('.zumbo-primary-tabs [aria-selected="true"]').count(), 1);
    if (width !== 390) {
      await capture(homePage, `home-${width}.png`, 'portrait-home', `${width}x${height}`);
    }
    checks.push(`portrait-home-${width}`);
    await authenticated.close();
  }

  const routeContext = await createContext({ width: 390, height: 844 });
  const routePage = await routeContext.newPage();
  diagnostics(routePage, 'route-matrix');
  for (const route of ['create', 'search', 'inbox', 'more']) {
    await routePage.goto(`${server.origin}/mobile-ionic/index.html#/app/${route}`, { waitUntil: 'networkidle' });
    await routePage.waitForFunction(() => document.querySelectorAll('.zumbo-primary-tabs .tab-item').length === 5);
    await assertSurface(routePage, route);
  }
  const aria = await routePage.locator('body').ariaSnapshot();
  assert.match(aria, /main/);
  assert.match(aria, /Daha fazla/);
  checks.push('authenticated-route-touch-and-screen-reader-matrix');
  await routeContext.close();

  const keyboard = await createContext({ width: 390, height: 844 }, { authenticated: false });
  const keyboardPage = await keyboard.newPage();
  diagnostics(keyboardPage, 'keyboard-login');
  await keyboardPage.goto(`${server.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'networkidle' });
  const identity = keyboardPage.locator('.login-entry-surface input').nth(0);
  await identity.focus();
  await keyboardPage.keyboard.press('Tab');
  assert.equal(await keyboardPage.evaluate(() => document.activeElement?.getAttribute('type')), 'password');
  await keyboardPage.keyboard.press('Tab');
  assert.equal(await keyboardPage.evaluate(() => document.activeElement?.textContent.trim()), 'Giriş yap');
  const focus = await keyboardPage.evaluate(() => {
    const style = window.getComputedStyle(document.activeElement);
    return { style: style.outlineStyle, width: Number.parseFloat(style.outlineWidth) };
  });
  assert.notEqual(focus.style, 'none');
  assert.ok(focus.width >= 3);
  await keyboardPage.keyboard.press('Enter');
  await keyboardPage.getByRole('alert').waitFor();
  checks.push('keyboard-safe-login-submit-and-focus');
  await keyboard.close();

  const landscape = await createContext({ width: 844, height: 390 }, { authenticated: false });
  const landscapePage = await landscape.newPage();
  diagnostics(landscapePage, 'landscape-login');
  await landscapePage.goto(`${server.origin}/mobile-ionic/index.html#/login`, { waitUntil: 'networkidle' });
  await landscapePage.locator('.login-entry-surface').waitFor();
  await assertSurface(landscapePage, 'landscape-login');
  const layout = await landscapePage.evaluate(() => {
    const brand = document.querySelector('.brand-lockup').getBoundingClientRect();
    const form = document.querySelector('.login-form').getBoundingClientRect();
    return {
      display: window.getComputedStyle(document.querySelector('.login-entry-surface .scroll')).display,
      separated: brand.right <= form.left + 1,
      formStartsInViewport: form.top < window.innerHeight
    };
  });
  assert.deepEqual(layout, { display: 'grid', separated: true, formStartsInViewport: true });
  await capture(landscapePage, 'login-landscape.png', 'landscape-login', '844x390');
  checks.push('landscape-login-reflow');
  await landscape.close();

  const landscapeHome = await createContext({ width: 844, height: 390 });
  const landscapeHomePage = await landscapeHome.newPage();
  diagnostics(landscapeHomePage, 'landscape-home');
  await landscapeHomePage.goto(`${server.origin}/mobile-ionic/index.html#/app/dashboard`, { waitUntil: 'networkidle' });
  await landscapeHomePage.locator('.mobile-home-actions').waitFor();
  await assertSurface(landscapeHomePage, 'landscape-home');
  checks.push('landscape-home-reflow');
  await landscapeHome.close();

  const preference = await createContext({ width: 390, height: 844 });
  const preferencePage = await preference.newPage();
  diagnostics(preferencePage, 'preferences');
  await preferencePage.goto(`${server.origin}/mobile-ionic/index.html#/app/dashboard`, { waitUntil: 'networkidle' });
  await preferencePage.locator('.mobile-home-actions').waitFor();
  assert.equal(await preferencePage.evaluate(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches), true);
  await preferencePage.emulateMedia({ forcedColors: 'active' });
  const firstAction = preferencePage.locator('.mobile-home-actions button').first();
  await firstAction.focus();
  const forcedFocus = await firstAction.evaluate(element => {
    const style = window.getComputedStyle(element);
    return { style: style.outlineStyle, width: Number.parseFloat(style.outlineWidth) };
  });
  assert.notEqual(forcedFocus.style, 'none');
  assert.ok(forcedFocus.width >= 3);
  checks.push('reduced-motion-and-forced-colors');
  await preference.close();
} catch (error) {
  failures.push(error.stack || error.message);
} finally {
  await browser.close();
  await server.close();
}

const result = {
  schemaVersion: 1,
  taskId: 'V3-MOBILE-004',
  passed: failures.length === 0,
  browser: 'chromium',
  viewports: ['360x780', '390x844', '430x844', '844x390'],
  checks,
  captures,
  failures
};
await writeFile(resolve(output, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
assert.deepEqual(failures, []);
assert.equal(checks.length, 11);
console.log(JSON.stringify(result, null, 2));
