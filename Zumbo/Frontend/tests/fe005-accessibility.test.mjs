import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const read = path => readFile(resolve(root, path), 'utf8');

function hexToRgb(value) {
  const hex = value.replace('#', '');
  const normalized = hex.length === 3 ? hex.split('').map(char => char + char).join('') : hex;
  return [0, 2, 4].map(index => Number.parseInt(normalized.slice(index, index + 2), 16));
}

function luminance(value) {
  return hexToRgb(value).map(channel => {
    const ratio = channel / 255;
    return ratio <= 0.04045 ? ratio / 12.92 : ((ratio + 0.055) / 1.055) ** 2.4;
  }).reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0);
}

function contrast(left, right) {
  const values = [luminance(left), luminance(right)].sort((a, b) => b - a);
  return (values[0] + 0.05) / (values[1] + 0.05);
}

function variables(block) {
  return Object.fromEntries([...block.matchAll(/(--[a-z0-9-]+):\s*(#[0-9a-f]{3,6})\s*;/gi)]
    .map(match => [match[1], match[2]]));
}

test('desktop ve mobile ayni semantic design-system katmanini once yukler', async () => {
  for (const path of ['desktop-bulma/index.html', 'mobile-ionic/index.html']) {
    const html = await read(path);
    assert.match(html, /\.\.\/shared\/design-system\.css/);
    assert.ok(html.indexOf('../shared/design-system.css') < html.indexOf('./styles.css'));
  }
});

test('ortak token kontrati renk typography spacing radius elevation ve motion rollerini tanimlar', async () => {
  const css = await read('shared/design-system.css');
  for (const token of [
    '--color-text', '--color-text-muted', '--color-surface', '--color-panel', '--color-border',
    '--color-accent', '--color-focus', '--color-success', '--color-warning', '--color-danger',
    '--font-body', '--font-size-body', '--space-1', '--space-6', '--radius-control',
    '--elevation-panel', '--motion-fast'
  ]) assert.match(css, new RegExp(`${token}:`));
  assert.match(css, /\.theme-dark\s*\{/);
});

test('light ve dark semantic text/state renkleri WCAG AA contrast butcesini gecer', async () => {
  const css = await read('shared/design-system.css');
  const light = variables(css.match(/:root\s*\{([\s\S]*?)\}/)[1]);
  const dark = variables(css.match(/\.theme-dark\s*\{([\s\S]*?)\}/)[1]);
  for (const palette of [light, dark]) {
    assert.ok(contrast(palette['--color-text'], palette['--color-surface']) >= 4.5);
    assert.ok(contrast(palette['--color-text-muted'], palette['--color-panel']) >= 4.5);
    assert.ok(contrast(palette['--color-danger'], palette['--color-panel']) >= 4.5);
    assert.ok(contrast(palette['--color-success'], palette['--color-panel']) >= 4.5);
  }
});

test('tum native ve AngularJS interactive yuzeyler ortak gorunur focus halkasi alir', async () => {
  const css = await read('shared/design-system.css');
  for (const selector of ['a:focus-visible', 'button:focus-visible', 'input:focus-visible', 'select:focus-visible', 'textarea:focus-visible', '[tabindex]:focus-visible']) {
    assert.match(css, new RegExp(selector.replace(/[\[\]]/g, '\\$&')));
  }
  assert.match(css, /outline:\s*3px solid var\(--color-focus\)/);
});

test('reduced motion forced colors ve yuksek contrast tercihleri explicit desteklenir', async () => {
  const css = await read('shared/design-system.css');
  assert.match(css, /@media\s*\(prefers-reduced-motion:\s*reduce\)/);
  assert.match(css, /@media\s*\(forced-colors:\s*active\)/);
  assert.match(css, /@media\s*\(prefers-contrast:\s*more\)/);
  assert.match(css, /forced-color-adjust:\s*auto/);
});

test('desktop ve mobile skip-link ile tek ana landmark sunar', async () => {
  const desktop = await read('desktop-bulma/index.html');
  const mobile = await read('mobile-ionic/index.html');
  assert.match(desktop, /class="skip-link" href="#main-workspace"/);
  assert.match(desktop, /<main id="main-workspace"/);
  assert.match(mobile, /class="skip-link" href="#mobile-main"/);
  assert.match(mobile, /<ion-nav-view id="mobile-main" role="main" tabindex="-1">/);
});

test('login alanlari explicit label association ve hata duyurusu tasir', async () => {
  const desktop = await read('desktop-bulma/index.html');
  for (const id of ['login-identity', 'login-password', 'login-mfa']) {
    assert.match(desktop, new RegExp(`<label[^>]+for="${id}"`));
    assert.match(desktop, new RegExp(`<input[^>]+id="${id}"`));
  }
  assert.match(desktop, /class="login-error"[^>]+role="alert"/);
  const mobile = await read('mobile-ionic/index.html');
  assert.match(mobile, /class="error-line"[^>]+role="alert"/);
});

test('mobil work mode tablari selected state ve roving tabindex bildirir', async () => {
  const mobile = await read('mobile-ionic/index.html');
  const tasks = await read('mobile-ionic/tasks.js');
  const tabs = [...mobile.matchAll(/<button role="tab"[^>]+>/g)].map(match => match[0]);
  assert.equal(tabs.length, 5);
  for (const tab of tabs) {
    assert.match(tab, /aria-selected=/);
    assert.match(tab, /tabindex=/);
  }
  assert.match(mobile, /ng-keydown="vm\.handleModeKey\(\$event\)"/);
  assert.match(tasks, /event\.key === 'ArrowRight'/);
  assert.match(tasks, /event\.key === 'ArrowLeft'/);
  assert.match(tasks, /event\.key === 'Home'/);
  assert.match(tasks, /event\.key === 'End'/);
});

test('ortak display-name resolver opaque user organization ve sprint kimliklerini gorunumden kaldirir', async () => {
  const resolver = await read('shared/display-names.js');
  assert.match(resolver, /factory\('displayNameResolver'/);
  assert.match(resolver, /user:\s*userName/);
  assert.match(resolver, /organization:\s*organizationName/);
  assert.match(resolver, /sprint:\s*sprintName/);
  for (const path of ['desktop-bulma/index.html', 'mobile-ionic/index.html']) {
    const html = await read(path);
    assert.doesNotMatch(html, /\{\{\s*(?:vm\.)?session\.currentUser\.organizationId\s*\}\}/);
    assert.doesNotMatch(html, /\{\{\s*(?:comment\.authorUserId|task\.assigneeUserId|member\.userId|user\.userId|item\.sprintId)\s*\}\}/);
  }
});

test('zoom ve uzun display-name reflow kontrati yatay sayfa tasmasini engeller', async () => {
  const css = await read('shared/design-system.css');
  assert.match(css, /overflow-wrap:\s*anywhere/);
  assert.match(css, /@media\s*\(max-width:\s*720px\)/);
  assert.match(css, /min-inline-size:\s*0/);
  assert.doesNotMatch(css, /font-size:\s*[^;]*(?:vw|vh)/);
});
