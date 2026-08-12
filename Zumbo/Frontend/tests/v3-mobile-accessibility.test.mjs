import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');
const read = path => readFile(resolve(root, path), 'utf8');
const html = await read('mobile-ionic/index.html');
const styles = await read('mobile-ionic/styles.css');
const design = await read('shared/design-system.css');
const touchSheets = await Promise.all([
  'mobile-ionic/planning-views.css',
  'mobile-ionic/work-automation.css',
  'mobile-ionic/project-catalog.css',
  'mobile-ionic/operations-center.css',
  'mobile-ionic/bulk-job-center.css'
].map(read));

test('mobile safe areas orientation and stable device dimensions are explicit', () => {
  assert.match(styles, /env\(safe-area-inset-top\)/);
  assert.match(styles, /env\(safe-area-inset-bottom\)/);
  assert.match(html, /class="login-surface login-entry-surface"/);
  assert.match(styles, /@media \(orientation: landscape\) and \(max-height: 520px\)/);
  assert.match(styles, /\.login-entry-surface \.scroll[\s\S]*grid-template-columns:/);
  assert.match(styles, /@media \(max-width: 360px\)/);
});

test('mobile primary and advanced commands preserve 44px touch targets', () => {
  for (const contract of [
    /\.zumbo-primary-tabs\.tabs-icon-top > \.tabs \.tab-item[\s\S]*min-height: 56px/,
    /\.mobile-pwa-state \.button[\s\S]*min-height: 44px/,
    /\.segmented button[\s\S]*min-height: 44px/,
    /\.mobile-task-mentions button \{ min-height: 44px/,
    /\.mobile-member-actions[\s\S]*44px 44px/,
    /\.mobile-member-actions select[\s\S]*height: 44px/
  ]) assert.match(styles, contract);

  for (const sheet of touchSheets) {
    assert.doesNotMatch(sheet, /(?:button|\.button)[^{]*\{[^}]*min-height:\s*(?:2\d|3\d|4[0-3])px/s);
  }
});

test('mobile keyboard and screen-reader semantics remain explicit', () => {
  assert.match(html, /<ion-nav-view id="mobile-main" role="main" tabindex="-1">/);
  assert.match(html, /role="status" aria-live="polite"/);
  const workModes = html.match(/class="segmented work-mode-segments"[\s\S]+?<\/div>/)?.[0] || '';
  assert.match(workModes, /role="tablist" aria-label="Mobil iş görünümü"/);
  assert.equal((workModes.match(/role="tab" aria-selected=/g) || []).length, 5);
  assert.match(html, /ng-keydown="vm\.handleModeKey\(\$event\)"/);
  assert.match(html, /enterkeyhint="search"/);
  assert.match(html, /autocomplete="one-time-code"/);
});

test('mobile accessibility preferences and reflow avoid viewport-scaled text', () => {
  assert.match(design, /@media \(prefers-reduced-motion: reduce\)/);
  assert.match(design, /@media \(forced-colors: active\)/);
  assert.match(design, /@media \(prefers-contrast: more\)/);
  assert.match(design, /outline:\s*3px solid var\(--color-focus\)/);
  assert.doesNotMatch(`${design}\n${styles}`, /font-size:\s*[^;]*(?:vw|vh)/);
  assert.doesNotMatch(`${design}\n${styles}`, /letter-spacing:\s*-/);
});

test('mobile surfaces keep meaningful text at or above the caption floor', async () => {
  const mobileRoot = resolve(root, 'mobile-ionic');
  const cssFiles = (await readdir(mobileRoot)).filter(file => file.endsWith('.css'));
  const css = (await Promise.all(cssFiles.map(file => readFile(resolve(mobileRoot, file), 'utf8')))).join('\n');
  assert.doesNotMatch(css, /font-size:\s*(?:8|9|10|11)px|font-size:\s*0?\.(?:5|6|7[0-4])\d*rem/);
  assert.match(styles, /small\s*\{[^}]*font-size:\s*var\(--font-size-caption\);/s);
  assert.match(styles, /\.zumbo-primary-tabs\.tabs-icon-top > \.tabs \.tab-item\s*\{[^}]*font-size:\s*var\(--font-size-caption\);/s);
});
