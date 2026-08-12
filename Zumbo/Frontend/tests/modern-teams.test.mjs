import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const root=new URL('../projects/modern-desktop/src/app/',import.meta.url);
const [models,core,service,page,template,workspace,workspaceTemplate]=await Promise.all(['features/teams/teams.models.ts','features/teams/teams.core.ts','features/teams/teams.service.ts','features/teams/teams.page.ts','features/teams/teams.page.html','workspace.page.ts','workspace.page.html'].map(path=>readFile(new URL(path,root),'utf8')));
test('modern Teams preserves lifecycle, membership, invitation, ownership and audit contracts',()=>{
  assert.match(models,/interface Team /);assert.match(models,/interface TeamMember/);assert.match(models,/interface TeamAudit/);
  assert.match(core,/function hasPermission/);assert.match(core,/role==='Owner'\|\|role==='Admin'/);assert.match(core,/function emailError/);assert.match(page,/TeamManage/);
  assert.match(service,/\/api\/teams\?organizationId=/);assert.match(service,/\/api\/auth\/roles/);assert.match(service,/\/api\/audit\/entity\/Team/);assert.match(service,/\/members/);assert.match(service,/\/role/);assert.match(service,/ownership-transfer/);assert.match(service,/ifMatch:team\.version/);assert.match(service,/idempotencyKey:this\.api\.newIdempotencyKey/);
  assert.match(page,/canReadAudit/);assert.match(page,/this\.owner\(\)/);assert.match(page,/this\.canManage\(\)/);
  assert.match(template,/Üyeler ve davetler/);assert.match(template,/Ekip etkinliği/);assert.match(template,/Bu ekip salt okunurdur/);
  assert.match(workspace,/import \{ TeamsPage \}/);assert.match(workspaceTemplate,/<zumbo-teams-page/);
  assert.doesNotMatch(service+page+template+workspaceTemplate,/fresh=/);assert.doesNotMatch(page+template,/SystemAdmin|ProjectAdmin/);
});
