import { Injectable,inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { forkJoin,Observable } from 'rxjs';
import { links } from './goal.core';
import { Goal,GoalDraft,GoalPageResponse,GoalPortfolio,GoalRollup,GoalStatusDraft,GoalUser,InitiativeOption,KeyResultDraft,KeyResultProgressDraft } from './goal.models';
@Injectable() export class GoalService{
 private readonly api=inject(ZumboApiClient);
 context(){return forkJoin({page:this.api.get<GoalPageResponse>('/api/goals?page=1&pageSize=100'),portfolios:this.api.get<{readonly items:readonly GoalPortfolio[]}>('/api/portfolios?page=1&pageSize=100'),users:this.api.get<readonly GoalUser[]>('/api/auth/users')});}
 detail(id:string){const v=encodeURIComponent(id);return forkJoin({goal:this.api.get<Goal>(`/api/goals/${v}`),rollup:this.api.get<GoalRollup>(`/api/goals/${v}/rollup`)});}
 saveGoal(d:GoalDraft,options:readonly InitiativeOption[]):Observable<Goal>{const body={name:d.name.trim(),description:d.description.trim()||null,periodStart:d.periodStart,periodEnd:d.periodEnd,viewerUserIds:[...new Set(d.viewerUserIds)],initiativeLinks:links(d.initiativeKeys,options),projectIds:[...new Set(d.projectIds)]};return d.id?this.api.put(`/api/goals/${encodeURIComponent(d.id)}`,body,{ifMatch:d.version}):this.api.post('/api/goals',body,{idempotencyKey:this.api.newIdempotencyKey()});}
 archive(g:Goal){return this.api.delete(`/api/goals/${encodeURIComponent(g.id)}`,{ifMatch:g.version});}
 saveKeyResult(g:Goal,d:KeyResultDraft):Observable<Goal>{const body={name:d.name.trim(),description:d.description.trim()||null,ownerUserId:d.ownerUserId,baselineValue:d.baselineValue,targetValue:d.targetValue,initialValue:d.initialValue,unit:d.unit.trim(),direction:d.direction};const root=`/api/goals/${encodeURIComponent(g.id)}/key-results`;return d.id?this.api.put(`${root}/${encodeURIComponent(d.id)}`,body,{ifMatch:g.version}):this.api.post(root,body,{ifMatch:g.version,idempotencyKey:this.api.newIdempotencyKey()});}
 progress(g:Goal,id:string,d:KeyResultProgressDraft):Observable<Goal>{return this.api.post(`/api/goals/${encodeURIComponent(g.id)}/key-results/${encodeURIComponent(id)}/progress-updates`,{...d,note:d.note.trim()},{ifMatch:g.version,idempotencyKey:this.api.newIdempotencyKey()});}
 status(g:Goal,d:GoalStatusDraft):Observable<Goal>{return this.api.post(`/api/goals/${encodeURIComponent(g.id)}/status-updates`,{...d,note:d.note.trim()},{ifMatch:g.version,idempotencyKey:this.api.newIdempotencyKey()});}
}
