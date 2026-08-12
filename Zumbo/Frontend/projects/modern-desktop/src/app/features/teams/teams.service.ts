import { Injectable,inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { catchError,forkJoin,of } from 'rxjs';
import { Team,TeamAudit,TeamInviteDraft,TeamRole,TeamUserContext } from './teams.models';
import { hasPermission } from './teams.core';
@Injectable() export class TeamsService {
  private readonly api=inject(ZumboApiClient);
  context(context:TeamUserContext){return forkJoin({teams:this.api.get<readonly Team[]>(`/api/teams?organizationId=${encodeURIComponent(context.organizationId)}`),roles:this.api.get<readonly TeamRole[]>('/api/auth/roles')});}
  audit(teamId:string,roles:readonly TeamRole[],context:TeamUserContext){return hasPermission(roles,context,'AuditRead')?this.api.get<readonly TeamAudit[]>(`/api/audit/entity/Team/${encodeURIComponent(teamId)}`).pipe(catchError(()=>of([] as readonly TeamAudit[]))):of([] as readonly TeamAudit[]);}
  create(context:TeamUserContext,name:string){return this.api.post<Team>('/api/teams',{organizationId:context.organizationId,name:name.trim(),ownerUserId:context.id},{idempotencyKey:this.api.newIdempotencyKey()});}
  update(team:Team,name:string){return this.api.put<Team>(`/api/teams/${encodeURIComponent(team.id)}`,{name:name.trim()},{ifMatch:team.version});}
  invite(team:Team,draft:TeamInviteDraft){return this.api.post<Team>(`/api/teams/${encodeURIComponent(team.id)}/members`,{email:draft.email.trim(),role:draft.role},{ifMatch:team.version,idempotencyKey:this.api.newIdempotencyKey()});}
  changeRole(team:Team,userId:string,role:string){return this.api.patch<Team>(`/api/teams/${encodeURIComponent(team.id)}/members/${encodeURIComponent(userId)}/role`,{role},{ifMatch:team.version});}
  remove(team:Team,key:string){return this.api.delete<Team>(`/api/teams/${encodeURIComponent(team.id)}/members/${encodeURIComponent(key)}`,{ifMatch:team.version});}
  transfer(team:Team,userId:string){return this.api.post<Team>(`/api/teams/${encodeURIComponent(team.id)}/ownership-transfer`,{newOwnerUserId:userId},{ifMatch:team.version,idempotencyKey:this.api.newIdempotencyKey()});}
  archive(team:Team){return this.api.delete<{readonly archived:boolean}>(`/api/teams/${encodeURIComponent(team.id)}`,{ifMatch:team.version});}
}
