import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '../..');
const read = path => readFileSync(resolve(root, path), 'utf8').replaceAll('\r\n', '\n');
const guide = read('docs/user-guide/power-user-guide.md');
const readme = read('readme.md');
const runbook = read('docs/runbooks/local-demo-walkthrough.md');
const desktopViews = read('Frontend/projects/modern-desktop/src/app/shell/desktop-shell.models.ts');
const mobileRoutes = read('Frontend/projects/modern-mobile/src/app/app.routes.ts');
const mobileMore = read('Frontend/projects/modern-mobile/src/app/features/more/mobile-more.page.html');
const checks = [];

for (const anchor of ['authentication', 'permissions', 'desktop-workspace', 'board-workflow', 'mobile-workspace', 'mobile-task-detail', 'offline-and-update', 'troubleshooting']) {
  assert.ok(guide.includes(`<a id="${anchor}"></a>`), `Guide anchor is missing: ${anchor}`);
}
checks.push({ name: 'guide-anchors', passed: true });

for (const route of ['home', 'my-work', 'inbox', 'projects', 'portfolios', 'goals', 'capacity', 'knowledge', 'teams', 'audit', 'archive', 'settings']) {
  const section = route === 'my-work' ? 'mywork' : route;
  assert.ok(desktopViews.includes(`| '${section}'`) || desktopViews.includes(`  | '${section}'`), `Desktop section is missing: ${route}`);
}
for (const route of ['home', 'work', 'create', 'inbox', 'more', 'projects', 'portfolios', 'goals', 'capacity', 'knowledge', 'teams']) {
  assert.ok(mobileRoutes.includes(`path: '${route}'`), `Mobile route is missing: ${route}`);
}
assert.equal((desktopViews.match(/\{ id:\s*'/g) ?? []).length, 15, 'Project view inventory must remain 15.');
assert.match(mobileMore, /theme\.toggle\(\)/);
checks.push({ name: 'modern-route-inventory', passed: true, projectViews: 15 });

for (const marker of ['runtime katalog', 'on beş proje görünümünü', 'Pano', 'İşlerim', 'Gelen kutusu']) {
  assert.ok(guide.toLocaleLowerCase('tr').includes(marker.toLocaleLowerCase('tr')), `Guide marker is missing: ${marker}`);
}
for (const text of [readme, runbook]) {
  assert.match(text, /\/modern-desktop\//);
  assert.match(text, /\/modern-mobile\//);
  assert.match(text, /demo-start\.mjs/);
}
assert.doesNotMatch(`${readme}\n${runbook}`, /fresh=demo|fresh=demo00/i);
checks.push({ name: 'current-start-contract', passed: true });

for (const link of [...guide.matchAll(/!?\[[^\]]*\]\(([^)]+)\)/g)].map(match => match[1])) {
  if (/^(?:https?:|mailto:|#)/i.test(link)) continue;
  assert.ok(existsSync(resolve(root, 'docs/user-guide', link.split('#')[0])), `Guide link is stale: ${link}`);
}
assert.doesNotMatch(guide, /[0-9a-f]{8}-[0-9a-f-]{27,}|fresh=demo/i);
checks.push({ name: 'links-and-sensitive-text', passed: true });

const result = {
  schemaVersion: 1,
  task: 'FINAL-QA-005',
  generatedAtUtc: new Date().toISOString(),
  status: 'complete',
  passed: true,
  checks,
  deployment: false,
  publicExposure: false
};

const evidenceIndex = process.argv.indexOf('--evidence');
if (evidenceIndex >= 0) {
  const path = resolve(root, process.argv[evidenceIndex + 1]);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
}
console.log(`Final user guide contract passed: ${checks.length} checks, 15 project views, modern desktop/mobile routes.`);
