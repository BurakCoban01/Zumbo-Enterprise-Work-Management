import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const evidence = json('artifacts/v3/V3-DESIGN-001.json');
const visual = json('artifacts/v3/V3-DESIGN-001-visual.json');

assert.equal(evidence.schemaVersion, 1);
assert.equal(evidence.task, 'V3-DESIGN-001');
assert.equal(evidence.passed, true);
assert.equal(evidence.noDeployment, true);
assert.equal(evidence.validation.officialSources, 6);
assert.deepEqual(evidence.validation.designSystemTests, { passed: 5, failed: 0 });
assert.deepEqual(evidence.validation.frontend.unit, { passed: 78, failed: 0, skipped: 0 });
assert.equal(evidence.validation.frontend.build.assets, 49);
assert.equal(evidence.validation.browser.captures, 4);
assert.equal(evidence.validation.visualReview.criticalBlockers, 0);
assert.ok(evidence.validation.visualReview.score >= 90);

const sourceFiles = {
  designSystemSha256: 'Frontend/shared/design-system.css',
  galleryHtmlSha256: 'Frontend/shared/component-gallery.html',
  galleryCssSha256: 'Frontend/shared/component-gallery.css',
  designNoteSha256: 'docs/product/design-system.md'
};
for (const [field, path] of Object.entries(sourceFiles)) {
  assert.match(evidence.hashes[field], /^[a-f0-9]{64}$/, `${path} is missing its historical SHA-256 digest.`);
}

assert.equal(visual.schemaVersion, 1);
assert.equal(visual.task, 'V3-DESIGN-001');
assert.equal(visual.browser, 'chromium');
assert.equal(visual.localRuntimeAssetsOnly, true);
assert.equal(visual.buildAssets, 49);
assert.deepEqual(visual.captures.map(capture => capture.name), ['gallery-desktop', 'gallery-mobile', 'login-desktop', 'login-mobile']);
for (const capture of visual.captures) {
  assert.equal(capture.horizontalOverflow, false);
  assert.equal(capture.keyboardFocusVisible, true);
  assert.equal(capture.externalRequests, 0);
  assert.equal(capture.runtimeFailures, 0);
  assert.ok(exists(capture.screenshot), `Screenshot is missing: ${capture.screenshot}`);
  assert.equal(capture.sha256, fileSha(capture.screenshot));
}

const gallery = read('Frontend/shared/component-gallery.html');
assert.doesNotMatch(gallery, /<style\b|style=|<script\b|https?:\/\//i);
const designNote = read('docs/product/design-system.md');
for (const source of ['linear.app', 'help.asana.com', 'atlassian.design', 'ionicframework.com']) assert.ok(designNote.includes(source));

console.log('V3-DESIGN-001 historical evidence passed: owned design sources and 4 immutable Chromium captures retain zero critical visual blockers.');

function json(path) { return JSON.parse(read(path)); }
function exists(path) { return existsSync(resolve(applicationRoot, path)); }
function read(path) { return readFileSync(resolve(applicationRoot, path), 'utf8'); }
function fileSha(path) { return createHash('sha256').update(readFileSync(resolve(applicationRoot, path))).digest('hex'); }
