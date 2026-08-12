import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';

const root = resolve(import.meta.dirname, '..');

test('board uses document flow for vertical work and reserves scrolling for the horizontal board axis', async () => {
  const styles = await read('projects/modern-desktop/src/app/features/board/project-board.page.scss');
  const responsive = await read('projects/modern-desktop/src/app/features/board/project-board-responsive.scss');

  assert.doesNotMatch(styles, /\.board-lane\s*\{[^}]*max-height/s);
  assert.doesNotMatch(responsive, /\.board-lane\s*\{[^}]*max-height/s);
  assert.match(styles, /\.board-scroll\s*\{[^}]*overflow-x:\s*auto/s);
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
  assert.match(responsive, /@media \(max-width:\s*840px\)/);
  assert.match(responsive, /\.work-detail-properties\s*\{\s*order:\s*3/);
  assert.match(responsive, /\.work-detail-properties\s*\{[^}]*min-height:\s*auto/);
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
  assert.match(styles, /@media \(max-width:\s*960px\)\s*\{\s*\.project-tabs\s*\{\s*display:\s*grid/);
  for (const label of ['Planlama', 'Operasyon', 'İçgörüler']) assert.match(component, new RegExp(label));
});

function read(path) {
  return readFile(resolve(root, path), 'utf8');
}
