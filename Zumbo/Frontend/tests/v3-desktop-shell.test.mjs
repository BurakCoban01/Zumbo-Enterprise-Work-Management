import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import vmModule from 'node:vm';

const root = resolve(import.meta.dirname, '..');
const shellSource = await readFile(resolve(root, 'desktop-bulma/shell.js'), 'utf8');
const appSource = await readFile(resolve(root, 'desktop-bulma/app.js'), 'utf8');
const managementSource = await readFile(resolve(root, 'desktop-bulma/management.js'), 'utf8');
const workItemsSource = await readFile(resolve(root, 'desktop-bulma/work-items.js'), 'utf8');
const html = await readFile(resolve(root, 'desktop-bulma/index.html'), 'utf8');
const styles = await readFile(resolve(root, 'desktop-bulma/styles.css'), 'utf8');

function createShellFeature() {
  let provider;
  const module = {
    factory(name, factory) {
      assert.equal(name, 'desktopShellFeature');
      provider = factory;
      return module;
    }
  };
  vmModule.runInNewContext(shellSource, { angular: { module: () => module } });
  return provider();
}

function createViewModel() {
  const calls = [];
  const viewModel = {
    board: { id: 'board-1' },
    projectMembership: { role: 'Developer' },
    commands: [
      { label: 'Raporları aç', group: 'Navigasyon', action: 'section', value: 'reports' },
      { label: 'Yeni görev oluştur', group: 'Eylem', action: 'create' }
    ],
    tasks: [
      { id: 'task-1', title: 'İzin akışını doğrula', status: 'In Progress', priority: 'High' },
      { id: 'task-2', title: 'Dağıtım notları', status: 'Done', priority: 'Low' }
    ],
    showSection: value => calls.push(['section', value]),
    openEntityCreator: value => calls.push(['create', value]),
    toggleTheme: () => calls.push(['theme']),
    setDensity: value => calls.push(['density', value]),
    selectTask: task => calls.push(['task', task.id]),
    density: 'comfortable'
  };
  createShellFeature().install(viewModel);
  return { viewModel, calls };
}

test('desktop shell create capability follows project role and selected board', () => {
  const { viewModel } = createViewModel();
  assert.equal(viewModel.canCreateTask(), true);
  viewModel.projectMembership.role = 'Viewer';
  assert.equal(viewModel.canCreateTask(), false);
  assert.deepEqual(viewModel.filteredCommands().map(command => command.action), ['section']);
  viewModel.projectMembership.role = 'Developer';
  viewModel.board = null;
  assert.equal(viewModel.canCreateTask(), false);
});

test('command palette searches Turkish command and task labels deterministically', () => {
  const { viewModel } = createViewModel();
  viewModel.commandQuery = 'raporları';
  assert.deepEqual(viewModel.filteredCommands().map(command => command.value), ['reports']);
  assert.equal(viewModel.filteredCommandTasks().length, 0);
  viewModel.commandQuery = 'izin akışı';
  assert.deepEqual(viewModel.filteredCommandTasks().map(task => task.id), ['task-1']);
  viewModel.commandQueryChanged();
  assert.equal(viewModel.activeCommandIndex, 0);
});

test('command palette arrow keys wrap and Enter executes the active task', () => {
  const { viewModel, calls } = createViewModel();
  viewModel.openCommandPalette();
  const prevented = [];
  viewModel.handleCommandKeydown({ key: 'ArrowUp', preventDefault: () => prevented.push('up') });
  assert.equal(viewModel.activeCommandIndex, viewModel.commandResultCount() - 1);
  viewModel.handleCommandKeydown({ key: 'Enter', preventDefault: () => prevented.push('enter') });
  assert.deepEqual(calls.at(-1), ['task', 'task-2']);
  assert.deepEqual(prevented, ['up', 'enter']);
  assert.equal(viewModel.commandOpen, false);
});

test('user navigation pushes history while recovery keeps replace semantics', () => {
  assert.match(appSource, /updateLocation\(section, null, true\)/);
  assert.match(appSource, /updateLocation\('projects', null, false\)/);
  assert.match(appSource, /addEventListener\('popstate', onPopState\)/);
  assert.match(managementSource, /updateLocation\('teams', null, true\)/);
  assert.match(managementSource, /updateLocation\(vm\.activeSection, null, true\)/);
  assert.match(workItemsSource, /updateLocation\(vm\.activeSection, null, true\)/);
  assert.match(workItemsSource, /if \(!skipLocation\) vm\.activeSection = 'board';/);
  assert.match(workItemsSource, /if \(!skipLocation\) updateLocation\('board', detail\.id, true\)/);
  assert.match(workItemsSource, /querySelectorAll\('\[data-work-item-id\]'\)/);
  assert.match(workItemsSource, /if \(target\) target\.focus\(\)/);
});

test('command template exposes combobox, listbox, selected state and permission-aware create entry', () => {
  assert.match(html, /ng-if="vm\.canCreateTask\(\)" ng-click="vm\.openEntityCreator\('task'\)"/);
  assert.match(html, /ng-keydown="vm\.handleCommandKeydown\(\$event\)"[^>]+role="combobox"/);
  assert.match(html, /aria-activedescendant="command-result-\{\{vm\.activeCommandIndex\}\}"/);
  assert.match(html, /class="command-results" role="listbox"/);
  assert.match(html, /role="option" aria-selected=/);
  assert.match(html, /Eşleşen komut veya görev yok\./);
});

test('desktop shell tracks recent projects by stable identity and keeps shell metadata legible', () => {
  assert.match(html, /ng-repeat="recent in vm\.recentProjects \| limitTo:3 track by recent\.id"/);
  assert.match(styles, /\.context-selectors label,\s*\.nav-secondary > span\s*\{[^}]*font-size:\s*12px;/s);
  assert.match(styles, /kbd\s*\{[^}]*font-size:\s*11px;/s);
  assert.match(styles, /\.notification-button > span\s*\{[^}]*color:\s*var\(--color-text-inverse\);[^}]*font-size:\s*12px;/s);
  assert.match(styles, /\.notification-popover > button span,\s*\.notification-popover > p,\s*\.popover-heading span\s*\{[^}]*font-size:\s*12px;/s);
  assert.match(styles, /\.breadcrumbs\s*\{[^}]*font-size:\s*12px;/s);
  assert.match(styles, /\.nav-count\s*\{[^}]*color:\s*var\(--color-text-inverse\);[^}]*font-size:\s*12px;/s);
});
