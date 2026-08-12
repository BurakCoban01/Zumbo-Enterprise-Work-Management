import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('../projects/modern-desktop/src/app/', import.meta.url);
const [models, core, service, page, template, workspace, workspaceTemplate] = await Promise.all([
  'features/portfolios/portfolio.models.ts', 'features/portfolios/portfolio.core.ts',
  'features/portfolios/portfolio.service.ts', 'features/portfolios/portfolio.page.ts',
  'features/portfolios/portfolio.page.html', 'workspace.page.ts', 'workspace.page.html'
].map(path => readFile(new URL(path, root), 'utf8')));

test('modern Portfolios preserves hierarchy, roadmap, status and dependency contracts', () => {
  assert.match(models, /interface Portfolio /);
  assert.match(models, /interface PortfolioRoadmap /);
  assert.match(core, /function initiativeTree/);
  assert.match(core, /En az bir proje bağlayın/);
  assert.match(core, /Bir proje kendisine bağlanamaz/);
  assert.match(service, /\/api\/portfolios\?page=1&pageSize=100/);
  assert.match(service, /\/roadmap/);
  assert.match(service, /\/initiatives/);
  assert.match(service, /\/status-updates/);
  assert.match(service, /\/dependencies/);
  assert.match(service, /ifMatch: portfolio\.version/);
  assert.match(service, /idempotencyKey: this\.api\.newIdempotencyKey/);
  assert.match(page, /item\.canUpdateStatus/);
  assert.match(template, /Yol haritası/);
  assert.match(template, /İnisiyatif hiyerarşisi/);
  assert.match(template, /Durum güncellemeleri/);
  assert.match(template, /Proje bağımlılıkları/);
  assert.match(workspace, /import \{ PortfolioPage \}/);
  assert.match(workspaceTemplate, /<zumbo-portfolio-page/);
  assert.doesNotMatch(service + page + template + workspaceTemplate, /fresh=/);
  assert.doesNotMatch(page + template, /role\s*===|SystemAdmin|ProjectAdmin/);
});
