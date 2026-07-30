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
  profiles,
  projectSurfaces,
  sectionSurfaces
} from './v3-desktop-acceptance-contract.mjs';

test('desktop acceptance contract covers every required viewport and visual preference', () => {
  assert.deepEqual(profiles.map(item => item.nominalWidth), [1920, 1440, 1366, 1024, 1440]);
  assert.deepEqual([...new Set(profiles.map(item => item.theme))].sort(), ['dark', 'light']);
  assert.deepEqual([...new Set(profiles.map(item => item.density))].sort(), ['comfortable', 'compact']);
  assert.deepEqual([...new Set(profiles.map(item => item.reducedMotion))].sort(), ['no-preference', 'reduce']);

  const zoom = profiles.find(item => item.zoomPercent === 200);
  assert.ok(zoom);
  assert.equal(zoom.width * zoom.zoomPercent / 100, zoom.nominalWidth);
  assert.equal(zoom.reducedMotion, 'reduce');
});

test('desktop acceptance contract owns the complete section and project-view inventory', () => {
  assert.deepEqual(sectionSurfaces.map(item => item.id), [
    'home', 'mywork', 'inbox', 'projects', 'portfolios', 'goals',
    'capacity', 'knowledge', 'teams', 'audit', 'archive', 'settings'
  ]);
  assert.deepEqual(projectSurfaces.map(item => item.id), [
    'overview', 'board', 'list', 'backlog', 'sprint',
    'calendar', 'timeline', 'roadmap', 'catalog', 'intake',
    'automation', 'jobs', 'workload', 'reports', 'dashboards'
  ]);

  const allIds = [...sectionSurfaces, ...projectSurfaces].map(item => `${item.kind}:${item.id}`);
  assert.equal(new Set(allIds).size, allIds.length);
  assert.ok([...sectionSurfaces, ...projectSurfaces].every(item => item.selector.length > 0));
  assert.ok(projectSurfaces.every(item => ['board', 'reports'].includes(item.section)));
});

test('desktop acceptance state distribution is explicit and totals 135 captures', () => {
  const statesPerProfile = [...sectionSurfaces, ...projectSurfaces].map(matrixState);
  const counts = statesPerProfile.reduce((result, state) => {
    result[state] += 1;
    return result;
  }, { normal: 0, 'high-data': 0, empty: 0 });
  assert.deepEqual(
    { normal: counts.normal, highData: counts['high-data'], empty: counts.empty },
    { normal: 8, highData: 10, empty: 9 }
  );
  assert.equal(expectedMatrixCaptureCount, 135);
  assert.equal(statesPerProfile.length * profiles.length, expectedMatrixCaptureCount);
  assert.ok(statesPerProfile.every(isMatrixState));
  assert.deepEqual(additionalStateIds, ['loading', 'empty-no-board', 'permission', 'offline']);
});

test('external error and conflict gates map to exact real-browser checks', async () => {
  assert.deepEqual(externalScenarioGates.map(gate => gate.state), ['error', 'conflict']);
  for (const gate of externalScenarioGates) {
    const source = await readFile(resolve(import.meta.dirname, gate.testFile), 'utf8');
    assert.match(source, new RegExp(`checks\\.push\\('${gate.requiredCheck}'\\)`));
    assert.match(gate.evidence, /^artifacts\/ui\/.+\/result\.json$/);
  }
});
