import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const read = (path) => readFile(resolve(root, path), 'utf8');

test('desktop search uses bounded fallback contract and exposes degraded state', async () => {
  const [script, html] = await Promise.all([
    read('desktop-bulma/task-board.js'),
    read('desktop-bulma/index.html')
  ]);
  assert.match(script, /apiClient\.post\(\s*['"]\/api\/work-items\/search['"]/);
  assert.match(script, /vm\.searchDegraded = result\.degraded === true/);
  assert.match(html, /status-banner warning[^>]+vm\.searchDegraded/);
  assert.match(html, /güvenli yedek görünümden gösteriliyor/);
});

test('mobile search preserves paging and exposes degraded state on both task surfaces', async () => {
  const [api, tasks, workspace, html] = await Promise.all([
    read('mobile-ionic/api.js'),
    read('mobile-ionic/tasks.js'),
    read('mobile-ionic/workspace.js'),
    read('mobile-ionic/index.html')
  ]);
  assert.equal((api.match(/apiClient\.post\(['"]\/api\/work-items\/search['"]/g) || []).length, 2);
  assert.match(tasks, /vm\.searchDegraded = data\.degraded === true/);
  assert.match(workspace, /vm\.searchDegraded = result\[1\]\.degraded === true/);
  assert.equal((html.match(/mobile-degraded-state/g) || []).length, 2);
});

test('backend fallback is bounded and returns an explicit degraded API response', async () => {
  const source = await read('../Backend/src/Zumbo.Modules.WorkItems/WorkItemSearchFallback.cs');
  assert.match(source, /DegradedFallbackMaxItems/);
  assert.match(source, /Math\.Clamp\([^;]+1, 10_000\)/s);
  assert.match(source, /WorkItemSearchPageResponse\([^;]+true\)/s);
});
