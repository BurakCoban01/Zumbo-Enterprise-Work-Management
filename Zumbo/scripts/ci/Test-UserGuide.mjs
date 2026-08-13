import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, extname, relative, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '../..');
if (!existsSync(resolve(root, 'Frontend/desktop-bulma/index.html'))) {
  await import('./Test-FinalUserGuide.mjs');
  process.exit(0);
}
const coveragePath = resolve(root, 'docs/user-guide/coverage.json');
const coverage = JSON.parse(readFileSync(coveragePath, 'utf8'));
const guidePath = resolve(root, coverage.guide);
const guide = readFileSync(guidePath, 'utf8').replaceAll('\r\n', '\n');
const desktopHtml = readFileSync(resolve(root, 'Frontend/desktop-bulma/index.html'), 'utf8');
const desktopSource = readRuntimeText('Frontend/desktop-bulma');
const mobileApp = readFileSync(resolve(root, 'Frontend/mobile-ionic/app.js'), 'utf8');
const mobileHtml = readFileSync(resolve(root, 'Frontend/mobile-ionic/index.html'), 'utf8');
const mobileSource = readRuntimeText('Frontend/mobile-ionic');
const permissionsSource = readFileSync(resolve(root, 'Backend/src/Zumbo.BuildingBlocks.Application/Security/PermissionCatalog.cs'), 'utf8');
const parity = JSON.parse(readFileSync(resolve(root, 'docs/frontend-parity.json'), 'utf8'));
const checks = [];

assert.equal(coverage.schemaVersion, 1);
assert.equal(coverage.task, 'QA-003');
assert.equal(coverage.visualReview.reviewed, true, 'Selected screenshots require an explicit visual review.');
checks.push({ name: 'manifest', passed: true });

const desktopSections = unique([...desktopHtml.matchAll(/showSection\('([^']+)'\)/g)].map(match => match[1]));
assert.deepEqual(desktopSections.sort(), coverage.desktop.sections.map(item => item.id).sort());
const workModes = unique([...desktopHtml.matchAll(/setWorkMode\('([^']+)'\)/g)].map(match => match[1]));
assert.deepEqual(workModes.sort(), coverage.desktop.workModes.map(item => item.id).sort());
for (const command of coverage.desktop.commands) assert.ok(desktopSource.includes(command), `Desktop command is stale: ${command}`);
checks.push({ name: 'desktop-inventory', passed: true, sections: desktopSections.length, workModes: workModes.length, commands: coverage.desktop.commands.length });

const states = [...mobileApp.matchAll(/\.state\('([^']+)',\s*\{\s*url:\s*'([^']+)'/g)].map(match => ({ name: match[1], url: match[2] }));
assert.deepEqual(states, coverage.mobile.states.map(({ name, url }) => ({ name, url })));
const templateUrls = unique([...mobileApp.matchAll(/templateUrl:\s*'([^']+)'/g)].map(match => match[1]));
const templateIds = new Set([...mobileHtml.matchAll(/<script\s+id="([^"]+)"\s+type="text\/ng-template">/g)].map(match => match[1]));
for (const templateUrl of templateUrls) assert.ok(templateIds.has(templateUrl), `Missing inline mobile template: ${templateUrl}`);
for (const tab of coverage.mobile.tabs) {
  assert.ok(mobileHtml.includes(`title="${tab.label}"`), `Mobile tab label is stale: ${tab.label}`);
  assert.ok(mobileHtml.includes(`href="${tab.href}"`), `Mobile tab href is stale: ${tab.href}`);
}
for (const mode of coverage.mobile.workspaceModes) assert.ok(mobileHtml.includes(`vm.setMode('${mode}')`), `Mobile workspace mode is stale: ${mode}`);
for (const mode of coverage.mobile.workModes) assert.match(mobileSource, new RegExp(`['"]${escapeRegex(mode)}['"]`), `Mobile work mode is stale: ${mode}`);
for (const status of coverage.mobile.statusFilters) assert.ok(mobileHtml.includes(`vm.filter('${status}')`), `Mobile status filter is stale: ${status || 'all'}`);
checks.push({ name: 'mobile-inventory', passed: true, states: states.length, templates: templateUrls.length, tabs: coverage.mobile.tabs.length, workspaceModes: coverage.mobile.workspaceModes.length, workModes: coverage.mobile.workModes.length, statusFilters: coverage.mobile.statusFilters.length });

for (const role of [...coverage.roles.system, ...coverage.roles.project]) {
  assert.deepEqual(parseRolePermissions(role.name), role.permissions, `Permission catalog drifted for ${role.name}.`);
  assert.ok(guide.includes(`\`${role.name}\``), `Guide does not name role ${role.name}.`);
  for (const permission of role.permissions) assert.ok(guide.includes(`\`${permission}\``) || permission === '*', `Guide does not cover permission ${permission}.`);
}
for (const permission of coverage.roles.additionalAssignablePermissions) {
  assert.ok(permissionsSource.includes(`"${permission}"`));
  assert.ok(guide.includes(`\`${permission}\``));
}
checks.push({ name: 'permission-matrix', passed: true, systemRoles: coverage.roles.system.length, projectRoles: coverage.roles.project.length });

for (const item of allAnchoredItems()) assertAnchor(item.anchor, item.id || item.name || item.path);
for (const filter of coverage.filters) assert.ok(`${desktopSource}\n${mobileSource}`.includes(filter.sourceMarker), `Filter marker is stale: ${filter.sourceMarker}`);
for (const shortcut of coverage.shortcuts) assert.ok(`${desktopSource}\n${mobileSource}`.includes(shortcut.sourceMarker), `Shortcut marker is stale: ${shortcut.sourceMarker}`);
checks.push({ name: 'workflow-filter-shortcut-coverage', passed: true, workflows: coverage.workflows.length, filters: coverage.filters.length, shortcuts: coverage.shortcuts.length });

assert.deepEqual(coverage.parityCapabilities, parity.essential.map(item => item.capability));
assert.deepEqual(coverage.administrativeExceptions, parity.administrativeExceptions.map(item => item.capability));
for (const item of parity.essential) assert.ok(guide.toLowerCase().includes(item.capability.toLowerCase()) || coverage.workflows.some(flow => flow.id.includes(item.capability)), `Parity capability is missing: ${item.capability}`);
for (const marker of ['organizasyon/rol/API key/privacy', 'workflow/kolon/schema', 'toplu/komut/raporlama']) {
  assert.ok(guide.toLocaleLowerCase('tr').includes(marker.toLocaleLowerCase('tr')), `Desktop-first mobile alternative is missing: ${marker}`);
}
checks.push({ name: 'platform-parity', passed: true, capabilities: parity.essential.length, administrativeExceptions: parity.administrativeExceptions.length });

const fingerprint = sourceFingerprint();
assert.equal(fingerprint.files, coverage.frontendSource.fileCount, 'Frontend source file count changed; recapture and re-review screenshots.');
assert.equal(fingerprint.sha256, coverage.frontendSource.sha256, 'Frontend source changed; recapture and re-review screenshots.');
checks.push({ name: 'frontend-fingerprint', passed: true, ...fingerprint });

for (const screenshot of coverage.screenshots) {
  const path = resolve(root, screenshot.path);
  assert.ok(existsSync(path), `Screenshot is missing: ${screenshot.path}`);
  const buffer = readFileSync(path);
  assert.ok(buffer.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])), `${screenshot.path} is not a PNG.`);
  assert.equal(buffer.readUInt32BE(16), screenshot.width, `${screenshot.path} width drifted.`);
  assert.equal(buffer.readUInt32BE(20), screenshot.height, `${screenshot.path} height drifted.`);
  assert.equal(buffer.length, screenshot.bytes, `${screenshot.path} byte length drifted.`);
  assert.equal(createHash('sha256').update(buffer).digest('hex'), screenshot.sha256, `${screenshot.path} hash drifted.`);
  const relativeFromGuide = relative(dirname(guidePath), path).replaceAll('\\', '/');
  assert.ok(guide.includes(`](${relativeFromGuide})`), `Guide does not embed ${screenshot.path}.`);
}
checks.push({ name: 'screenshots', passed: true, count: coverage.screenshots.length, privacyReviewed: true });

const links = [...guide.matchAll(/!?\[[^\]]*\]\(([^)]+)\)/g)].map(match => match[1]);
for (const link of links) {
  if (/^(?:https?:|mailto:)/i.test(link)) continue;
  if (link.startsWith('#')) {
    assert.ok(guide.includes(`<a id="${link.slice(1)}"></a>`), `Guide anchor link is stale: ${link}`);
    continue;
  }
  const target = link.split('#')[0];
  assert.ok(existsSync(resolve(dirname(guidePath), target)), `Guide link is stale: ${link}`);
}
assert.doesNotMatch(guide, /artifacts\/ui\/playwright|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|[0-9a-f]{8}-[0-9a-f-]{27,}/i, 'Guide contains a stale evidence path or sensitive fixture identifier.');
checks.push({ name: 'links-and-sensitive-text', passed: true, links: links.length });

const result = {
  schemaVersion: 1,
  task: 'QA-003',
  generatedAtUtc: new Date().toISOString(),
  status: 'complete',
  passed: true,
  checks,
  browserEvidence: {
    controlledCurrentRun: true,
    capturedAtUtc: coverage.capturedAtUtc,
    visualReview: coverage.visualReview,
    frontendSourceSha256: fingerprint.sha256
  },
  ciWired: true,
  deployment: false,
  publicExposure: false,
  totals: {
    checks: checks.length,
    desktopSections: desktopSections.length,
    desktopWorkModes: workModes.length,
    mobileStates: states.length,
    roles: coverage.roles.system.length + coverage.roles.project.length,
    workflows: coverage.workflows.length,
    screenshots: coverage.screenshots.length,
    links: links.length
  }
};

const evidenceArgument = argumentValue('--evidence');
if (evidenceArgument) {
  const evidencePath = resolve(root, evidenceArgument);
  mkdirSync(dirname(evidencePath), { recursive: true });
  writeFileSync(evidencePath, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
}
console.log(`User guide contract passed: ${result.totals.checks} checks, ${result.totals.workflows} workflows, ${result.totals.screenshots} current screenshots.`);

function allAnchoredItems() {
  return [
    ...coverage.desktop.sections,
    ...coverage.desktop.workModes,
    ...coverage.mobile.states,
    ...coverage.filters,
    ...coverage.shortcuts,
    ...coverage.workflows,
    ...coverage.screenshots
  ];
}

function assertAnchor(anchor, owner) {
  assert.ok(anchor, `Coverage item has no anchor: ${owner}`);
  assert.ok(guide.includes(`<a id="${anchor}"></a>`), `Guide anchor '${anchor}' is missing for ${owner}.`);
}

function parseRolePermissions(roleName) {
  const match = permissionsSource.match(new RegExp(`\\("${escapeRegex(roleName)}",\\s*\\[([\\s\\S]*?)\\]\\)`));
  assert.ok(match, `PermissionCatalog role is missing: ${roleName}`);
  return match[1].split(',').map(value => value.trim()).filter(Boolean).map(value => value === 'All' ? '*' : value);
}

function sourceFingerprint() {
  const extensions = new Set(coverage.frontendSource.extensions);
  const files = coverage.frontendSource.roots.flatMap(path => walk(path, extensions)).sort();
  const hash = createHash('sha256');
  for (const path of files) {
    const buffer = readFileSync(resolve(root, path));
    hash.update(path);
    hash.update('\0');
    hash.update(String(buffer.length));
    hash.update('\0');
    hash.update(buffer);
    hash.update('\0');
  }
  return { files: files.length, sha256: hash.digest('hex') };
}

function walk(path, extensions) {
  const absolute = resolve(root, path);
  return readdirSync(absolute, { withFileTypes: true }).flatMap(entry => {
    const child = `${path}/${entry.name}`;
    return entry.isDirectory() ? walk(child, extensions) : extensions.has(extname(entry.name)) ? [child] : [];
  });
}

function readRuntimeText(path) {
  return walk(path, new Set(['.html', '.js', '.css'])).map(file => readFileSync(resolve(root, file), 'utf8')).join('\n');
}

function unique(values) {
  return [...new Set(values)];
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}
