import {inject,Injectable} from '@angular/core';
import {ZumboApiClient} from '@zumbo/modern-shared';
import {forkJoin} from 'rxjs';
import {CapacityAllocation,CapacityDraft,CapacityPage,CapacityPlan,CapacityScenario,CapacitySnapshot,CapacityUser} from './mobile-capacity.models';
@Injectable() export class MobileCapacityService {
 private readonly api=inject(ZumboApiClient);
 list(){return forkJoin({page:this.api.get<CapacityPage>('/api/capacity-plans?page=1&pageSize=100'),users:this.api.get<readonly CapacityUser[]>('/api/auth/users')});}
 detail(id:string){const value=encodeURIComponent(id);return forkJoin({plan:this.api.get<CapacityPlan>(`/api/capacity-plans/${value}`),snapshot:this.api.get<CapacitySnapshot>(`/api/capacity-plans/${value}/snapshot`)});}
 save(draft:CapacityDraft,userId:string,plan?:CapacityPlan|null){const projectIds=plan?.projectIds||[draft.projectId],members=plan?.members||[{userId,teamId:null,weeklyCapacityHours:draft.weeklyCapacityHours}],allocations=plan?.allocations||[{id:null,userId,projectId:draft.projectId,startDate:draft.periodStart,endDate:draft.periodEnd,percent:100}],body={name:draft.name.trim(),description:draft.description.trim()||null,periodStart:draft.periodStart,periodEnd:draft.periodEnd,portfolioId:plan?.portfolioId||null,projectIds,members,allocations,viewerUserIds:plan?.viewerUserIds||[]};return draft.id?this.api.put<CapacityPlan>(`/api/capacity-plans/${encodeURIComponent(draft.id)}`,body,{ifMatch:draft.version}):this.api.post<CapacityPlan>('/api/capacity-plans',body,{idempotencyKey:this.api.newIdempotencyKey()});}
 archive(plan:CapacityPlan){return this.api.delete(`/api/capacity-plans/${encodeURIComponent(plan.id)}`,{ifMatch:plan.version});}
 scenario(plan:CapacityPlan,allocations:readonly CapacityAllocation[]){return this.api.post<CapacityScenario>(`/api/capacity-plans/${encodeURIComponent(plan.id)}/scenarios`,{allocations},{ifMatch:plan.version});}
}
