import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');

test('board uses document flow and fits columns without a horizontal board canvas', async () => {
  const [template, component, styles, responsive] = await Promise.all([
    read('projects/modern-desktop/src/app/features/board/project-board.page.html'),
    read('projects/modern-desktop/src/app/features/board/project-board.page.ts'),
    read('projects/modern-desktop/src/app/features/board/project-board.page.scss'),
    read('projects/modern-desktop/src/app/features/board/project-board-responsive.scss')
  ]);

  assert.doesNotMatch(styles, /\.board-lane\s*\{[^}]*max-height/s);
  assert.doesNotMatch(responsive, /\.board-lane\s*\{[^}]*max-height/s);
  assert.match(styles, /\.board-scroll\s*\{[^}]*overflow:\s*visible/s);
  assert.match(styles, /\.board-columns\s*\{[^}]*grid-template-columns:\s*repeat\(var\(--board-column-count/s);
  assert.doesNotMatch(responsive, /grid-template-columns:\s*repeat\(2/);
  assert.match(responsive, /@media \(max-width:\s*960px\)[\s\S]*grid-template-columns:\s*repeat\(3/s);
  assert.match(responsive, /@media \(max-width:\s*700px\)[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)/s);
  assert.match(template, /\[style\.--board-column-count\]="columns\(\)\.length"/);
  assert.match(template, /\(click\)="openTask\(\$event, task\)"/);
  assert.match(component, /target\.closest\('a, button, input, select, textarea'\)/);
  assert.match(styles, /\.lane-cards\s*\{[^}]*overflow:\s*visible/s);
  assert.doesNotMatch(styles, /\.lane-cards\s*\{[^}]*overflow-y:\s*auto/s);
  assert.doesNotMatch(responsive, /\.lane-cards\s*\{[^}]*overflow-y:\s*auto/s);
});

test('work item detail has one scroll owner and places bounded operational context before activity', async () => {
  const [template, styles, responsive] = await Promise.all([
    read('projects/modern-desktop/src/app/features/work-items/work-item-detail.component.html'),
    read('projects/modern-desktop/src/app/features/work-items/work-item-detail.component.scss'),
    read('projects/modern-desktop/src/app/features/work-items/work-item-detail-responsive.scss')
  ]);

  assert.match(styles, /\.work-detail-body\s*\{[^}]*overflow-y:\s*auto/s);
  assert.match(styles, /\.work-detail-body\s*>\s*main,\s*\.work-detail-properties\s*\{[^}]*overflow:\s*visible/s);
  assert.ok(template.indexOf('id="attachments-title"') < template.indexOf('id="activity-title"'));
  assert.ok(template.indexOf('id="development-title"') < template.indexOf('id="activity-title"'));
  assert.match(responsive, /@media \(max-width:\s*1180px\)/);
  assert.match(responsive, /\.work-detail-properties\s*\{\s*order:\s*3/);
  assert.match(responsive, /\.work-detail-properties\s*\{[^}]*min-height:\s*auto/);
  assert.match(responsive, /\.work-detail-properties\s*\{[^}]*grid-template-columns:\s*repeat\(2,/);
  const extensions = await read('projects/modern-desktop/src/app/features/work-items/work-item-detail-extensions.scss');
  assert.match(extensions, /\.activity-list\s*\{[^}]*max-height:\s*min\(52vh,\s*520px\)[^}]*overflow-y:\s*auto/s);
});

test('project navigation exposes named secondary work groups without a generic overflow label', async () => {
  const [template, component, styles] = await Promise.all([
    read('projects/modern-desktop/src/app/shell/project-view-tabs.component.html'),
    read('projects/modern-desktop/src/app/shell/project-view-tabs.component.ts'),
    read('projects/modern-desktop/src/app/shell/project-view-tabs.component.scss')
  ]);

  assert.doesNotMatch(template, />\s*Diğer\s*</);
  assert.match(template, /@for \(group of secondaryGroups/);
  assert.match(component, /\['plan', 'operate', 'insights'\] as const/);
  assert.match(template, /class="primary-views"/);
  assert.match(template, /class="secondary-groups"/);
  assert.match(template, /class="group-trigger"/);
  assert.match(template, /\(click\)="toggleGroup\(group\)"/);
  assert.match(template, /\[attr\.aria-expanded\]="openGroup\(\) === group"/);
  assert.match(component, /openGroup = signal/);
  assert.match(component, /current === group \? null : group/);
  assert.match(styles, /\.secondary-groups\s*\{[^}]*margin-left:\s*12px/s);
  assert.match(styles, /\.group-trigger small\s*\{/);
  assert.match(styles, /@media \(min-width:\s*761px\) and \(max-width:\s*1180px\)[\s\S]*left:\s*256px/s);
  assert.match(styles, /@media \(max-width:\s*1180px\)\s*\{\s*\.project-tabs\s*\{\s*display:\s*grid/);
  for (const label of ['Planlama', 'Operasyon', 'İçgörüler']) assert.match(component, new RegExp(label));
});

test('rail-aware desktop feature breakpoints prevent medium-width document overflow', async () => {
  const styles = await read('projects/modern-desktop/src/styles.scss');
  assert.match(styles, /@media \(max-width:\s*1180px\)/);
  for (const selector of ['zumbo-goal-page \\.goal-shell', 'zumbo-capacity-page \\.capacity-shell', 'zumbo-teams-page \\.teams-shell']) {
    assert.match(styles, new RegExp(`${selector}[\\s\\S]*grid-template-columns:\\s*1fr`));
  }
});

function read(path) {
  return readFile(resolve(root, path), 'utf8');
}
