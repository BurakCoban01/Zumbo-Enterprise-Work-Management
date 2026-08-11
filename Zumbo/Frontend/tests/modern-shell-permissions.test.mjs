import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import test from 'node:test';
import ts from 'typescript';

const root = resolve(import.meta.dirname, '..');

test('workspace audit navigation follows the runtime role catalog', async () => {
  const models = transpileCommonJs(await read('projects/modern-desktop/src/app/shell/desktop-shell.models.ts'));
  const roles = [
    { name: 'Administrator', permissions: ['*'], isActive: true },
    { name: 'Auditor', permissions: ['AuditReadAll'], isActive: true },
    { name: 'Viewer', permissions: ['WorkItemRead'], isActive: true },
    { name: 'FormerAuditor', permissions: ['AuditReadAll'], isActive: false }
  ];

  assert.equal(models.hasWorkspacePermission(['Administrator'], roles, 'AuditReadAll'), true);
  assert.equal(models.hasWorkspacePermission(['Auditor'], roles, 'AuditReadAll'), true);
  assert.equal(models.hasWorkspacePermission(['Viewer'], roles, 'AuditReadAll'), false);
  assert.equal(models.hasWorkspacePermission(['FormerAuditor'], roles, 'AuditReadAll'), false);

  const workspace = await read('projects/modern-desktop/src/app/workspace.page.ts');
  const template = await read('projects/modern-desktop/src/app/workspace.page.html');
  const navigation = await read('projects/modern-desktop/src/app/shell/desktop-navigation.component.html');
  const palette = await read('projects/modern-desktop/src/app/shell/command-palette.component.ts');
  assert.match(workspace, /\/api\/auth\/roles/);
  assert.match(template, /\[showAudit\]="canViewAudit\(\)"/g);
  assert.match(navigation, /item\.section !== 'audit' \|\| showAudit\(\)/);
  assert.match(palette, /command\.id !== 'audit' \|\| this\.showAudit\(\)/);
});

function read(path) {
  return readFile(resolve(root, path), 'utf8');
}

function transpileCommonJs(source) {
  const output = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText;
  const module = { exports: {} };
  Function('exports', 'module', output)(module.exports, module);
  return module.exports;
}
