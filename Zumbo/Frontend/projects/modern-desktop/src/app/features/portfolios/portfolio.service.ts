import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin } from 'rxjs';
import { DependencyDraft, InitiativeDraft, Portfolio, PortfolioDraft, PortfolioPageResponse, PortfolioRoadmap, PortfolioUser, StatusDraft } from './portfolio.models';
import { isoDate } from './portfolio.core';

@Injectable()
export class PortfolioService {
  private readonly api = inject(ZumboApiClient);
  list(): Observable<PortfolioPageResponse> { return this.api.get('/api/portfolios?page=1&pageSize=100'); }
  users(): Observable<readonly PortfolioUser[]> { return this.api.get('/api/auth/users'); }
  detail(id: string) { return forkJoin({ portfolio: this.api.get<Portfolio>(`/api/portfolios/${encodeURIComponent(id)}`), roadmap: this.api.get<PortfolioRoadmap>(`/api/portfolios/${encodeURIComponent(id)}/roadmap`) }); }
  savePortfolio(draft: PortfolioDraft): Observable<Portfolio> { const body = { name: draft.name.trim(), description: draft.description.trim() || null, viewerUserIds: [...new Set(draft.viewerUserIds)] }; return draft.id ? this.api.put(`/api/portfolios/${encodeURIComponent(draft.id)}`, body, { ifMatch: draft.version }) : this.api.post('/api/portfolios', body, { idempotencyKey: this.api.newIdempotencyKey() }); }
  archive(item: Portfolio): Observable<unknown> { return this.api.delete(`/api/portfolios/${encodeURIComponent(item.id)}`, { ifMatch: item.version }); }
  saveInitiative(portfolio: Portfolio, draft: InitiativeDraft): Observable<Portfolio> { const body = { name: draft.name.trim(), summary: draft.summary.trim() || null, parentInitiativeId: draft.parentInitiativeId || null, ownerUserId: draft.ownerUserId, status: draft.status, health: draft.health, confidence: draft.confidence, targetAt: isoDate(draft.targetAt), projectIds: [...new Set(draft.projectIds)], milestoneLinks: [] }; const root = `/api/portfolios/${encodeURIComponent(portfolio.id)}/initiatives`; return draft.id ? this.api.put(`${root}/${encodeURIComponent(draft.id)}`, body, { ifMatch: portfolio.version }) : this.api.post(root, body, { ifMatch: portfolio.version, idempotencyKey: this.api.newIdempotencyKey() }); }
  addStatus(portfolio: Portfolio, initiativeId: string, draft: StatusDraft): Observable<Portfolio> { return this.api.post(`/api/portfolios/${encodeURIComponent(portfolio.id)}/initiatives/${encodeURIComponent(initiativeId)}/status-updates`, { ...draft, note: draft.note.trim() }, { ifMatch: portfolio.version, idempotencyKey: this.api.newIdempotencyKey() }); }
  saveDependency(portfolio: Portfolio, draft: DependencyDraft): Observable<Portfolio> { const body = { sourceProjectId: draft.sourceProjectId, targetProjectId: draft.targetProjectId, description: draft.description.trim(), status: draft.status, requiredBy: isoDate(draft.requiredBy) }; const root = `/api/portfolios/${encodeURIComponent(portfolio.id)}/dependencies`; return draft.id ? this.api.put(`${root}/${encodeURIComponent(draft.id)}`, body, { ifMatch: portfolio.version }) : this.api.post(root, body, { ifMatch: portfolio.version, idempotencyKey: this.api.newIdempotencyKey() }); }
}
