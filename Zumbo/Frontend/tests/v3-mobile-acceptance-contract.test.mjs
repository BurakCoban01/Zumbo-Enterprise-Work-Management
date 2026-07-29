import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import {
  additionalStateIds,
  expectedMatrixCaptureCount,
  externalScenarioGates,
  isMatrixState,
  matrixState,
  mobileSurfaces,
  profiles
} from './v3-mobile-acceptance-contract.mjs';

test('mobile acceptance profiles cover required widths, orientation, theme and motion preferences', () => {
  assert.deepEqual(profiles.map(item => `${item.width}x${item.height}`), [
    '430x932', '390x844', '360x800', '844x390'
  ]);
  assert.deepEqual([...new Set(profiles.map(item => item.orientation))].sort(), ['landscape', 'portrait']);
  assert.deepEqual([...new Set(profiles.map(item => item.theme))].sort(), ['dark', 'light']);
  assert.deepEqual([...new Set(profiles.map(item => item.reducedMotion))].sort(), ['no-preference', 'reduce']);
  assert.ok(profiles.filter(item => item.orientation === 'portrait').every(item => [430, 390, 360].includes(item.width)));
  assert.ok(profiles.every(item => item.orientation === (item.width > item.height ? 'landscape' : 'portrait')));
});

test('mobile acceptance contract owns all 27 concrete Ionic routes', async () => {
  assert.deepEqual(mobileSurfaces.map(item => item.stateName), [
    'login', 'forgot-password', 'reset-password', 'public-intake',
    'project-detail', 'project-catalog', 'project-intake', 'project-automation',
    'project-jobs', 'project-planning', 'project-reporting', 'team-detail',
    'task-detail', 'integration-center', 'operations-center', 'portfolio-center',
    'goal-center', 'capacity-center', 'knowledge-center', 'app.dashboard',
    'app.tasks', 'app.create', 'app.notifications', 'app.more', 'app.projects',
    'app.search', 'app.profile'
  ]);
  assert.equal(new Set(mobileSurfaces.map(item => item.id)).size, mobileSurfaces.length);
  assert.ok(mobileSurfaces.every(item => item.path.startsWith('/') && item.selector.length > 0));
  assert.deepEqual([...new Set(mobileSurfaces.map(item => item.authentication))].sort(), ['anonymous', 'authenticated']);

  const app = await readFile(resolve(import.meta.dirname, '../mobile-ionic/app.js'), 'utf8');
  const declared = [...app.matchAll(/\.state\('([^']+)'/g)].map(match => match[1]);
  assert.equal(declared.includes('app'), true);
  assert.deepEqual(
    declared.filter(stateName => stateName !== 'app'),
    mobileSurfaces.map(item => item.stateName)
  );
});

test('mobile acceptance states are explicit and total 108 matrix captures', () => {
  const counts = mobileSurfaces.map(matrixState).reduce((result, state) => {
    result[state] += 1;
    return result;
  }, { normal: 0, 'high-data': 0, empty: 0, negative: 0 });
  assert.deepEqual(counts, { normal: 7, 'high-data': 12, empty: 7, negative: 1 });
  assert.ok(mobileSurfaces.map(matrixState).every(isMatrixState));
  assert.equal(expectedMatrixCaptureCount, 108);
  assert.equal(profiles.length * mobileSurfaces.length, expectedMatrixCaptureCount);
  assert.deepEqual(additionalStateIds, ['loading', 'empty-no-project', 'permission', 'offline']);
});

test('mobile external gates map to exact current browser checks', async () => {
  assert.deepEqual(externalScenarioGates.map(gate => gate.state), [
    'error', 'conflict', 'essential-parity', 'pwa-lifecycle', 'accessibility-preferences'
  ]);
  for (const gate of externalScenarioGates) {
    const source = await readFile(resolve(import.meta.dirname, gate.testFile), 'utf8');
    for (const check of gate.requiredChecks) {
      assert.ok(source.includes(`checks.push('${check}')`), `${gate.testFile} is missing ${check}`);
    }
    assert.match(gate.evidence, /^artifacts\/ui\/.+\/result\.json$/);
  }
});
