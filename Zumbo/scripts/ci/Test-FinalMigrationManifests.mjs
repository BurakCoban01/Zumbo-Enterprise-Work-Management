import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

const outputRoot = 'artifacts/final/manifests';
const generator = spawnSync(
  process.execPath,
  ['scripts/product/Build-FinalMigrationManifests.mjs', '--check'],
  { cwd: applicationRoot, encoding: 'utf8', timeout: 30_000 });
assert.equal(generator.status, 0, generator.stderr || generator.stdout);

const index = json(`${outputRoot}/index.json`);
const surfaces = json(`${outputRoot}/frontend-surfaces.json`);
const routes = json(`${outputRoot}/api-routes.json`);
const openApi = json(`${outputRoot}/openapi-summary.json`);
const usage = json(`${outputRoot}/frontend-api-usage.json`);
const storage = json(`${outputRoot}/browser-storage.json`);
const framework = json(`${outputRoot}/legacy-framework-markers.json`);
const pwa = json(`${outputRoot}/static-pwa-contract.json`);

assert.equal(index.schemaVersion, 1);
assert.equal(index.task, 'FINAL-BASE-003');
assert.match(index.sourceCommit, /^[0-9a-f]{7,40}$/);
assert.equal(index.outputs.length, 14);
assert.equal(new Set(index.outputs.map(output => output.path)).size, 14);
assert.equal(index.noProductionMutation, true);
assert.equal(index.noDeployment, true);

assert.deepEqual(surfaces.summary, {
  desktopSections: 14,
  desktopProjectViews: 15,
  mobileStates: 28,
  total: 57
});
assert.deepEqual(routes.summary, {
  operations: 324,
  businessMinimalApiOperations: 320,
  frameworkEndpoints: 4,
  byWave: {
    'framework-exempt': 4,
    'wave-1-low-risk': 32,
    'wave-2-medium': 90,
    'wave-3-special': 41,
    'wave-4-sensitive': 66,
    'wave-5-workitems': 91
  }
});
assert.equal(openApi.summary.operations, 320);
assert.deepEqual(usage.summary, {
  scannedCalls: 548,
  consumerContexts: 550,
  uniqueSourceLocations: 549,
  desktopContexts: 310,
  mobileContexts: 240,
  adminContexts: 0
});
assert.equal(storage.summary.keys, 20);
assert.equal(framework.legacyDependencies.angular, '1.8.3');
assert.equal(framework.legacyDependencies['ionic-sdk'], '1.3.2');
assert.equal(framework.modernAngularWorkspacePresent, false);
assert.equal(framework.summary.byKind.ionicState, 28);
assert.equal(pwa.summary.sourceFiles, 8);
assert.equal(pwa.summary.cspPresent, true);

for (const output of index.outputs) {
  const content = text(output.path);
  assert.equal(sha256(content), output.sha256, `${output.path} hash drifted from the index.`);
  if (output.path.endsWith('.csv')) {
    assert.equal(content.trimEnd().split(/\r?\n/).length, output.records + 1,
      `${output.path} must contain one header plus every indexed record.`);
  }
}
for (const [path, hash] of Object.entries(index.sourceHashes)) {
  assert.equal(sha256(text(path)), hash, `${path} drifted from the manifest baseline.`);
}

console.log(
  `FINAL-BASE-003 manifest gate passed: ${surfaces.summary.total} surfaces, `
  + `${routes.summary.operations} routes, ${usage.summary.scannedCalls} scanned calls, `
  + `${index.outputs.length} hash-bound JSON/CSV outputs.`);

function text(path) { return readFileSync(resolve(applicationRoot, path), 'utf8').replaceAll('\r\n', '\n'); }
function json(path) { return JSON.parse(text(path)); }
function sha256(value) { return createHash('sha256').update(value).digest('hex'); }
