import { Injectable,inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable,catchError,forkJoin,map,of } from 'rxjs';
import { BoardSummary,ProjectSummary } from '../../shell/desktop-shell.models';
import { ProjectWorkItem } from '../work-items/project-work-item.models';
import { ArchiveCollection,ArchiveContext,ArchiveKind,ArchivedTeam,ArchiveRole } from './archive.models';

@Injectable() export class ArchiveService{
  private readonly api=inject(ZumboApiClient);
  load(context:ArchiveContext):Observable<ArchiveCollection>{
    const failed:ArchiveKind[]=[];
    const safe=<T>(kind:ArchiveKind,request:Observable<readonly T[]>)=>request.pipe(catchError(()=>{failed.push(kind);return of([] as readonly T[]);}));
    const organizationId=encodeURIComponent(context.organizationId);
    const projectId=context.projectId?encodeURIComponent(context.projectId):null;
    return forkJoin({
      projects:safe('projects',this.api.get<readonly ProjectSummary[]>(`/api/projects?organizationId=${organizationId}&archived=true`)),
      teams:safe('teams',this.api.get<readonly ArchivedTeam[]>(`/api/teams?organizationId=${organizationId}&archived=true`)),
      boards:projectId?safe('boards',this.api.get<readonly BoardSummary[]>(`/api/boards/by-project/${projectId}?archived=true`)):of([] as readonly BoardSummary[]),
      workItems:projectId?safe('work-items',this.api.get<readonly ProjectWorkItem[]>(`/api/work-items?projectId=${projectId}&archived=true&page=1&pageSize=100`)):of([] as readonly ProjectWorkItem[]),
      roles:this.api.get<readonly ArchiveRole[]>('/api/auth/roles').pipe(catchError(()=>of([] as readonly ArchiveRole[])))
    }).pipe(map(value=>{const permissions=[...new Set(value.roles.filter(role=>role.isActive&&context.roleNames.includes(role.name)).flatMap(role=>role.permissions))];return{projects:value.projects,teams:value.teams,boards:value.boards,workItems:value.workItems,permissions,failed:[...failed]};}));
  }
  restore(kind:ArchiveKind,id:string):Observable<unknown>{const resource=kind==='work-items'?'work-items':kind;return this.api.post(`/api/${resource}/${encodeURIComponent(id)}/restore`,{});}
}
