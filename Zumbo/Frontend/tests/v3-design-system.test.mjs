import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const appRoot = resolve(root, '..');
const read = path => readFile(resolve(root, path), 'utf8');

test('V3 semantic token contract covers theme density typography state and motion roles', async () => {
  const css = await read('shared/design-system.css');
  for (const token of [
    '--color-text-subtle', '--color-text-inverse', '--color-surface-subtle', '--color-panel-raised',
    '--color-border-strong', '--color-brand-signal', '--color-info', '--color-success-soft',
    '--color-warning-soft', '--color-danger-soft', '--font-display', '--font-numeric', '--font-size-title',
    '--font-weight-strong', '--radius-compact', '--elevation-overlay', '--motion-instant',
    '--motion-deliberate', '--motion-easing', '--control-height', '--row-height', '--density-padding'
  ]) assert.match(css, new RegExp(`${token}:`));
  assert.match(css, /\.density-compact\s*\{/);
  assert.doesNotMatch(css, /font-size:\s*[^;]*(?:vw|vh)/);
});

test('light and dark state pairs retain WCAG AA text contrast', async () => {
  const css = await read('shared/design-system.css');
  const light = variables(css.match(/:root\s*\{([\s\S]*?)\}/)[1]);
  const dark = variables(css.match(/\.theme-dark\s*\{([\s\S]*?)\}/)[1]);
  for (const palette of [light, dark]) {
    for (const role of ['success', 'warning', 'danger', 'info']) {
      assert.ok(contrast(palette[`--color-${role}`], palette[`--color-${role}-soft`]) >= 4.5, `${role} contrast failed`);
    }
    assert.ok(contrast(palette['--color-text-inverse'], palette['--color-accent']) >= 4.5);
  }
});

test('component gallery uses shipped primitives and renders required product states', async () => {
  const html = await read('shared/component-gallery.html');
  assert.match(html, /shared\/component-gallery\.html|<title>Zumbo Bileşen Galerisi<\/title>/);
  assert.match(html, /\.\/design-system\.css/);
  assert.match(html, /\.\/component-gallery\.css/);
  for (const state of ['is-primary', 'is-danger', 'is-loading', 'disabled', 'aria-invalid="true"', 'zumbo-status', 'zumbo-message', 'zumbo-skeleton', 'theme-dark']) {
    assert.match(html, new RegExp(state));
  }
  for (const copy of ['Yükleniyor', 'Görev yok', 'Salt okunur', 'Yüklenemedi']) assert.match(html, new RegExp(copy));
  assert.doesNotMatch(html, /<style\b|style=|<script\b|https?:\/\//i);
  const visibleText = html.replace(/<[^>]+>/g, ' ');
  assert.doesNotMatch(visibleText, /AngularJS|Bulma|Ionic|API|database|production-grade/i);
});

test('desktop and mobile authentication surfaces share the Zumbo orientation signature', async () => {
  const desktop = await read('desktop-bulma/index.html');
  const mobile = await read('mobile-ionic/index.html');
  assert.match(desktop, /class="desktop-login-frame"/);
  assert.match(desktop, /class="workflow-signal"/);
  assert.match(desktop, /İşinize kaldığınız yerden devam edin\./);
  assert.match(mobile, /class="zumbo-kicker">Ekip çalışma alanı/);
  assert.match(mobile, /İşinize kaldığınız yerden devam edin\./);
});

test('canonical design note records product-specific direction and current official sources', async () => {
  const note = await readFile(resolve(appRoot, 'docs/product/design-system.md'), 'utf8');
  for (const domain of ['linear.app', 'help.asana.com', 'atlassian.design', 'ionicframework.com']) assert.match(note, new RegExp(domain.replace('.', '\\.')));
  for (const heading of ['## Product Model', '## Official-Source Principles', '## Art Direction', '## Token Contract', '## Component and State Contract']) assert.match(note, new RegExp(heading));
  assert.match(note, /dense but calm/);
  assert.match(note, /work graph/i);
});

function variables(block) {
  return Object.fromEntries([...block.matchAll(/(--[a-z0-9-]+):\s*(#[0-9a-f]{6})\s*;/gi)].map(match => [match[1], match[2]]));
}

function contrast(left, right) {
  assert.ok(left && right, 'Missing contrast token.');
  const values = [luminance(left), luminance(right)].sort((a, b) => b - a);
  return (values[0] + 0.05) / (values[1] + 0.05);
}

function luminance(value) {
  const rgb = [1, 3, 5].map(index => Number.parseInt(value.slice(index, index + 2), 16) / 255);
  return rgb.map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4)
    .reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0);
}
