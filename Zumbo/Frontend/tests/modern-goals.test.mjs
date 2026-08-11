import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const root=new URL('../projects/modern-desktop/src/app/',import.meta.url);
const [models,core,service,page,template,workspace,workspaceTemplate]=await Promise.all(['features/goals/goal.models.ts','features/goals/goal.core.ts','features/goals/goal.service.ts','features/goals/goal.page.ts','features/goals/goal.page.html','workspace.page.ts','workspace.page.html'].map(p=>readFile(new URL(p,root),'utf8')));
test('modern Goals preserves measurable result, history, rollup and source contracts',()=>{
 assert.match(models,/interface GoalRollup/);assert.match(models,/interface KeyResultUpdate/);
 assert.match(core,/function keyResultError/);assert.match(core,/Baseline ve target farklı/);assert.match(core,/function initiativeOptions/);
 assert.match(service,/\/api\/goals\?page=1&pageSize=100/);assert.match(service,/\/rollup/);assert.match(service,/\/key-results/);assert.match(service,/\/progress-updates/);assert.match(service,/\/status-updates/);assert.match(service,/ifMatch:g\.version/);assert.match(service,/idempotencyKey:this\.api\.newIdempotencyKey/);
 assert.match(page,/r\.canUpdate/);assert.match(page,/g\?\.canUpdateStatus/);
 assert.match(template,/Ölçülebilir sonuçlar/);assert.match(template,/Hedef sağlık geçmişi/);assert.match(template,/Plan bağlantıları/);assert.match(template,/Hedef tanımı salt okunurdur/);
 assert.match(workspace,/import \{ GoalPage \}/);assert.match(workspaceTemplate,/<zumbo-goal-page/);
 assert.doesNotMatch(service+page+template+workspaceTemplate,/fresh=/);assert.doesNotMatch(page+template,/role\s*===|SystemAdmin|ProjectAdmin/);
});
