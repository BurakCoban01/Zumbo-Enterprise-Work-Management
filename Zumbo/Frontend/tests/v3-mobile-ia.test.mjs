import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const [app, api, shell, tasks, html, css] = await Promise.all([
  'app.js', 'api.js', 'mobile-shell.js', 'tasks.js', 'index.html', 'styles.css'
].map(file => readFile(resolve(root, 'mobile-ionic', file), 'utf8')));

test('V3-MOBILE-001 five-target information architecture is explicit', () => {
  for (const label of ['Ana sayfa', 'İşlerim', 'Oluştur', 'Gelen kutusu', 'Daha fazla']) {
    assert.match(html, new RegExp(`title="${label}"`));
  }
  assert.equal((html.match(/<ion-tab /g) || []).length, 5);
  for (const view of ['home', 'work', 'create', 'inbox', 'more']) {
    assert.match(html, new RegExp(`<ion-nav-view name="${view}"`));
  }
  for (const route of ['app.create', 'app.more', 'app.search']) {
    assert.ok(app.includes(`state('${route}'`), `${route} route is missing`);
  }
});

test('create reuses the characterized task form in selected project context', () => {
  assert.match(shell, /sessionStore\.state\.project = project/);
  assert.match(shell, /sessionStore\.state\.openCreateTask = true/);
  assert.match(shell, /\$state\.go\('app\.tasks'\)/);
  assert.match(tasks, /\$ionicView\.afterEnter/);
  assert.match(tasks, /vm\.load\(\)\.then\(vm\.quickAdd\)/);
  assert.match(html, /ng-submit="vm\.continueToForm\(\)"/);
  assert.match(html, /templates\/create-task\.html/);
});

test('resource-authorized project search exposes loading, degraded, error and empty states', () => {
  assert.match(api, /searchWork: function/);
  assert.match(api, /projectId: projectId/);
  assert.match(api, /scope: 'mobile-global-search', replace: true/);
  assert.match(shell, /zumboApi\.searchWork\(vm\.selectedProjectId/);
  assert.match(shell, /query\.length < 2/);
  assert.match(shell, /result\.degraded === true/);
  for (const state of ['vm.loading', 'vm.degraded', 'vm.error', 'vm.searched']) {
    assert.ok(html.includes(state), `${state} template state is missing`);
  }
  assert.match(html, /ng-click="vm\.openTask\(task\)"/);
  assert.match(html, /aria-label="Arama projesi"/);
});

test('mobile shell preserves touch targets and safe areas at narrow widths', () => {
  assert.match(css, /env\(safe-area-inset-top\)/);
  assert.match(css, /env\(safe-area-inset-bottom\)/);
  assert.match(css, /\.zumbo-primary-tabs \.tab-item[\s\S]*min-height: 56px/);
  assert.match(css, /\.mobile-more-nav > button[\s\S]*min-height: 68px/);
  assert.match(css, /@media \(max-width: 360px\)/);
  assert.doesNotMatch(css, /font-size:\s*clamp\([^;]*vw/);
  assert.doesNotMatch(css, /letter-spacing:\s*-/);
});
