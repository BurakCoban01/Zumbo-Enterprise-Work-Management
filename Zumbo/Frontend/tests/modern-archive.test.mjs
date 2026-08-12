import assert from 'node:assert/strict';import{readFile}from'node:fs/promises';import{resolve}from'node:path';import test from'node:test';
const root=resolve(import.meta.dirname,'..');const read=path=>readFile(resolve(root,path),'utf8');
test('modern Archive preserves lifecycle sources, partial failure and permission-driven restore contracts',async()=>{
  const[service,core,page,workspace]=await Promise.all([
    read('projects/modern-desktop/src/app/features/archive/archive.service.ts'),read('projects/modern-desktop/src/app/features/archive/archive.core.ts'),read('projects/modern-desktop/src/app/features/archive/archive.page.html'),read('projects/modern-desktop/src/app/workspace.page.html')]);
  for(const route of ['/api/projects?organizationId=','/api/teams?organizationId=','/api/boards/by-project/','/api/work-items?projectId=','/api/auth/roles'])assert.ok(service.includes(route),route);
  assert.match(service,/catchError\(\(\)=>\{failed\.push\(kind\)/);assert.match(service,/\/restore`/);
  for(const permission of ['ProjectManage','TeamManage','BoardManage','WorkItemDelete'])assert.ok(core.includes(permission),permission);
  assert.match(page,/canRestore\(group\.kind\)/);assert.match(page,/Arşivde ara/);assert.match(workspace,/zumbo-archive-page/);
  assert.doesNotMatch(service,/[?&](?:fresh|cache|v)=/i);
});
