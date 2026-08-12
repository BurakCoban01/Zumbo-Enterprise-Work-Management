import {inject,Injectable} from '@angular/core';
import {ZumboApiClient} from '@zumbo/modern-shared';
import {forkJoin} from 'rxjs';
import {Goal,GoalContext,GoalDraft,GoalPage,GoalPortfolio,GoalUser,KeyResultDraft} from './mobile-goal.models';
@Injectable() export class MobileGoalService{
 private readonly api=inject(ZumboApiClient);
 list(){return this.api.get<GoalPage>('/api/goals?page=1&pageSize=100');}
 catalog(){return forkJoin({portfolios:this.api.get<{readonly items:readonly GoalPortfolio[]}>('/api/portfolios?page=1&pageSize=100'),users:this.api.get<readonly GoalUser[]>('/api/auth/users')});}
 detail(id:string){const value=encodeURIComponent(id);return forkJoin({goal:this.api.get<Goal>(`/api/goals/${value}`),rollup:this.api.get<GoalContext['rollup']>(`/api/goals/${value}/rollup`)});}
 progress(goal:Goal,keyResultId:string,value:{currentValue:number;confidence:number|null;note:string}){return this.api.post<Goal>(`/api/goals/${encodeURIComponent(goal.id)}/key-results/${encodeURIComponent(keyResultId)}/progress-updates`,value,{ifMatch:goal.version,idempotencyKey:this.api.newIdempotencyKey()});}
 updateStatus(goal:Goal,value:{status:string;health:string;confidence:number|null;note:string}){return this.api.post<Goal>(`/api/goals/${encodeURIComponent(goal.id)}/status-updates`,value,{ifMatch:goal.version,idempotencyKey:this.api.newIdempotencyKey()});}
 saveGoal(draft:GoalDraft){const initiativeLinks=[...new Set(draft.initiativeKeys)].map(key=>{const [portfolioId,initiativeId]=key.split(':');return{portfolioId,initiativeId};}).filter(link=>link.portfolioId&&link.initiativeId),body={name:draft.name.trim(),description:draft.description.trim()||null,periodStart:draft.periodStart,periodEnd:draft.periodEnd,viewerUserIds:[...new Set(draft.viewerUserIds)],initiativeLinks,projectIds:[...new Set(draft.projectIds)]};return draft.id?this.api.put<Goal>(`/api/goals/${encodeURIComponent(draft.id)}`,body,{ifMatch:draft.version}):this.api.post<Goal>('/api/goals',body,{idempotencyKey:this.api.newIdempotencyKey()});}
 archive(goal:Goal){return this.api.delete(`/api/goals/${encodeURIComponent(goal.id)}`,{ifMatch:goal.version});}
 saveKeyResult(goal:Goal,draft:KeyResultDraft){const body={name:draft.name.trim(),description:draft.description.trim()||null,ownerUserId:draft.ownerUserId,baselineValue:draft.baselineValue,targetValue:draft.targetValue,initialValue:draft.initialValue,unit:draft.unit.trim(),direction:draft.direction},root=`/api/goals/${encodeURIComponent(goal.id)}/key-results`;return draft.id?this.api.put<Goal>(`${root}/${encodeURIComponent(draft.id)}`,body,{ifMatch:goal.version}):this.api.post<Goal>(root,body,{ifMatch:goal.version,idempotencyKey:this.api.newIdempotencyKey()});}
}
