import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applicationRoot } from '../repository-layout.mjs';

function readJson(relativePath) {
  const path = resolve(applicationRoot, relativePath);
  assert.ok(existsSync(path), `${relativePath} is missing.`);
  return JSON.parse(readFileSync(path, 'utf8'));
}

const runtime = readJson('artifacts/final/M01-runtime.json');
assert.equal(runtime.passed, true);
assert.equal(runtime.decision, 'ready');
assert.equal(runtime.noDeployment, true);
assert.equal(runtime.noPublicExposure, true);
assert.equal(runtime.noVolumeDeletion, true);
const services = runtime.checks.find(check => check.name === 'service-inventory');
assert.deepEqual(services.detail.healthy.sort(), ['api', 'gateway', 'minio', 'mongo', 'opensearch', 'redis', 'worker']);
assert.deepEqual(services.detail.completed, ['mongo-init-replica']);
const readiness = runtime.checks.find(check => check.name === 'http-readiness');
assert.deepEqual(readiness.detail, { live: 200, ready: 200, desktop: 200, mobile: 200 });

const seed = readJson('artifacts/final/M01-seed-verify.json');
assert.equal(seed.passed, true);
assert.equal(seed.decision, 'ready');
assert.equal(seed.seedVersion, 'demo-readiness-v1');
assert.deepEqual(seed.changes, []);
assert.deepEqual(seed.baseline, {
  users: 18,
  teams: 4,
  projects: 6,
  selectedProjectWorkItems: 13,
  selectedProjectHasViewer: true
});
assert.equal(seed.secretsRecorded, false);

const visual = readJson('artifacts/final/M01-visual-triage.json');
assert.equal(visual.task, 'FINAL-UX-001');
assert.equal(visual.passed, true);
assert.equal(visual.findings.length, 8);
assert.equal(visual.findings.filter(finding => finding.severity === 'P0').length, 1);
assert.equal(visual.findings.filter(finding => finding.severity === 'P1').length, 7);
assert.ok(visual.findings.every(finding => finding.ownerTasks.length > 0));
assert.deepEqual(visual.unownedP0P1, []);
assert.deepEqual(visual.criticalFlow.reversibleMutation, {
  workItem: 'Form Bilesenleri',
  forwardStatus: 200,
  restoreStatus: 200,
  restored: true
});
assert.equal(visual.criticalFlow.wipProbe.status, 409);
assert.equal(visual.criticalFlow.wipProbe.persistentChange, false);
assert.equal(visual.browser.unexpectedConsoleErrors, 0);
assert.equal(visual.browser.pageErrors, 0);
assert.equal(visual.browser.unexpectedHttpFailures, 0);
assert.equal(visual.secretsRecorded, false);

for (const screenshot of visual.screenshots) {
  const path = resolve(applicationRoot, screenshot.path);
  assert.ok(existsSync(path), `${screenshot.path} is missing.`);
  const actual = createHash('sha256').update(readFileSync(path)).digest('hex');
  assert.equal(actual, screenshot.sha256, `${screenshot.path} hash drifted.`);
}

const serializedEvidence = JSON.stringify({ runtime, seed, visual });
for (const forbiddenKey of ['password', 'accessToken', 'refreshToken', 'cookie']) {
  assert.equal(new RegExp(`"${forbiddenKey}"`, 'i').test(serializedEvidence), false, `Evidence contains forbidden key ${forbiddenKey}.`);
}

console.log('M01 acceptance passed: runtime, seed, reversible mutation, owned visual triage and screenshot hashes verified.');
