import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const root=new URL('../projects/modern-desktop/src/app/',import.meta.url);
const [models,core,service,page,template,workspace,workspaceTemplate]=await Promise.all(['features/knowledge/knowledge.models.ts','features/knowledge/knowledge.core.ts','features/knowledge/knowledge.service.ts','features/knowledge/knowledge.page.ts','features/knowledge/knowledge.page.html','workspace.page.ts','workspace.page.html'].map(path=>readFile(new URL(path,root),'utf8')));
test('modern Knowledge preserves document, version, links, markdown and comment contracts',()=>{
  assert.match(models,/interface KnowledgeDocument/);assert.match(models,/interface KnowledgeVersion/);assert.match(models,/interface KnowledgeComment/);
  assert.match(core,/function knowledgeScopes/);assert.match(core,/BoardManage/);assert.match(core,/function parseMarkdown/);assert.match(core,/function safeLink/);assert.match(core,/Doküman başlığı 160/);
  assert.match(service,/\/api\/knowledge-documents\?page=1&pageSize=100/);assert.match(service,/scope-link-options/);assert.match(service,/\/versions\//);assert.match(service,/\/comments/);assert.match(service,/\/resolve/);assert.match(service,/ifMatch:document\.version/);
  assert.match(page,/document\.canEdit/);assert.match(page,/document\?\.canComment/);assert.match(page,/comment\.authorUserId===this\.userId\(\)/);
  assert.match(template,/Bilgi ve karar dokümanları/);assert.match(template,/Sürüm geçmişi/);assert.match(template,/Bu doküman salt okunur/);assert.match(template,/ngTemplateOutlet/);
  assert.match(workspace,/import \{ KnowledgePage \}/);assert.match(workspaceTemplate,/<zumbo-knowledge-page/);
  assert.doesNotMatch(service+page+template+workspaceTemplate,/fresh=/);assert.doesNotMatch(page+template,/role\s*===|SystemAdmin|ProjectAdmin/);
});
