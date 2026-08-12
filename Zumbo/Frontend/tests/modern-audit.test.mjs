import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('../projects/modern-desktop/src/app/', import.meta.url);
const [models, core, service, page, template, workspace, workspaceTemplate] = await Promise.all([
  'features/audit/audit.models.ts', 'features/audit/audit.core.ts', 'features/audit/audit.service.ts',
  'features/audit/audit.page.ts', 'features/audit/audit.page.html', 'workspace.page.ts', 'workspace.page.html'
].map(path => readFile(new URL(path, root), 'utf8')));

test('modern Audit preserves capability, query, cursor, export and integrity contracts', () => {
  assert.match(models, /interface AuditEntry/); assert.match(models, /interface AuditIntegrity/);
  assert.match(core, /AuditReadAll/); assert.match(core, /Kaynak türü ve kaynak kimliği birlikte girilmelidir/); assert.match(core, /safeAuditChanges/);
  assert.match(service, /\/api\/audit\/integrity\//); assert.match(service, /\/api\/audit\/export/);
  assert.match(page, /nextCursor/); assert.match(page, /if \(this\.allowed\(\)\) this\.loadInitial\(\)/);
  assert.match(template, /Bütünlüğü doğrula/); assert.match(template, /Güvenli değişiklik özeti/);
  assert.match(workspace, /import \{ AuditPage \}/); assert.match(workspaceTemplate, /<zumbo-audit-page/);
  assert.doesNotMatch(service + page + template + workspaceTemplate, /fresh=/); assert.doesNotMatch(template, /WorkItemUpdated/);
});
