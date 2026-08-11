import {inject,Injectable} from '@angular/core';
import {ZumboApiClient} from '@zumbo/modern-shared';
import {forkJoin} from 'rxjs';
import {DependencyDraft,InitiativeDraft,Portfolio,PortfolioContext,PortfolioDraft,PortfolioPage,PortfolioUser} from './mobile-portfolio.models';
@Injectable() export class MobilePortfolioService{
 private readonly api=inject(ZumboApiClient);
 list(){return this.api.get<PortfolioPage>('/api/portfolios?page=1&pageSize=100');}
 users(){return this.api.get<readonly PortfolioUser[]>('/api/auth/users');}
 detail(id:string){const value=encodeURIComponent(id);return forkJoin({portfolio:this.api.get<Portfolio>(`/api/portfolios/${value}`),roadmap:this.api.get<PortfolioContext['roadmap']>(`/api/portfolios/${value}/roadmap`)});}
 updateStatus(portfolio:Portfolio,initiativeId:string,value:{status:string;health:string;confidence:number|null;note:string}){return this.api.post<Portfolio>(`/api/portfolios/${encodeURIComponent(portfolio.id)}/initiatives/${encodeURIComponent(initiativeId)}/status-updates`,value,{ifMatch:portfolio.version,idempotencyKey:this.api.newIdempotencyKey()});}
 savePortfolio(draft:PortfolioDraft){const body={name:draft.name.trim(),description:draft.description.trim()||null,viewerUserIds:[...new Set(draft.viewerUserIds)]};return draft.id?this.api.put<Portfolio>(`/api/portfolios/${encodeURIComponent(draft.id)}`,body,{ifMatch:draft.version}):this.api.post<Portfolio>('/api/portfolios',body,{idempotencyKey:this.api.newIdempotencyKey()});}
 archive(portfolio:Portfolio){return this.api.delete(`/api/portfolios/${encodeURIComponent(portfolio.id)}`,{ifMatch:portfolio.version});}
 saveInitiative(portfolio:Portfolio,draft:InitiativeDraft){const body={name:draft.name.trim(),summary:draft.summary.trim()||null,parentInitiativeId:null,ownerUserId:draft.ownerUserId,status:draft.status,health:draft.health,confidence:draft.confidence,targetAt:draft.targetAt?new Date(`${draft.targetAt}T00:00:00Z`).toISOString():null,projectIds:[...new Set(draft.projectIds)],milestoneLinks:[]},root=`/api/portfolios/${encodeURIComponent(portfolio.id)}/initiatives`;return draft.id?this.api.put<Portfolio>(`${root}/${encodeURIComponent(draft.id)}`,body,{ifMatch:portfolio.version}):this.api.post<Portfolio>(root,body,{ifMatch:portfolio.version,idempotencyKey:this.api.newIdempotencyKey()});}
 saveDependency(portfolio:Portfolio,draft:DependencyDraft){const body={sourceProjectId:draft.sourceProjectId,targetProjectId:draft.targetProjectId,description:draft.description.trim(),status:draft.status,requiredBy:draft.requiredBy?new Date(`${draft.requiredBy}T00:00:00Z`).toISOString():null},root=`/api/portfolios/${encodeURIComponent(portfolio.id)}/dependencies`;return draft.id?this.api.put<Portfolio>(`${root}/${encodeURIComponent(draft.id)}`,body,{ifMatch:portfolio.version}):this.api.post<Portfolio>(root,body,{ifMatch:portfolio.version,idempotencyKey:this.api.newIdempotencyKey()});}
}
