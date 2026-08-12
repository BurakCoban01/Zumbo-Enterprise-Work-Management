import { inject, Injectable } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { catchError, forkJoin, map, Observable, of } from 'rxjs';
import { MobileRole, MobileSearchResult, MobileWorkItemRecord, MobileWorkflow } from '../../shell/mobile-workspace.models';
import { MobileBacklogItem, MobileProjectHubData, MobileProjectRisk, MobileProjectSprint, MobileProjectSummary, MobileSprintPage } from './mobile-project-hub.models';

@Injectable()
export class MobileProjectHubService {
  private readonly api = inject(ZumboApiClient);

  load(projectId: string): Observable<MobileProjectHubData> {
    const id = encodeURIComponent(projectId); const failures: string[] = [];
    const safe = <T>(name: string, request: Observable<T>, fallback: T) => request.pipe(catchError(() => { failures.push(name); return of(fallback); }));
    return forkJoin({
      summary: safe('özet', this.api.get<MobileProjectSummary>(`/api/work-items/reports/project-summary/${id}`), { total: 0, done: 0, inProgress: 0, overdue: 0 }),
      risks: safe('riskler', this.api.get<readonly MobileProjectRisk[]>(`/api/work-items/reports/due-date-risks/${id}?days=14`), []),
      work: safe('proje işleri', this.api.post<MobileSearchResult>('/api/work-items/search', { projectId, page: 1, pageSize: 100 }), { items: [], totalCount: 0, degraded: true }),
      workflow: safe('iş akışı', this.api.get<MobileWorkflow>(`/api/workflows/${id}`), { statuses: [] }),
      sprints: safe('sprintler', this.api.get<MobileSprintPage<MobileProjectSprint>>(`/api/sprints/projects/${id}?pageSize=50`), { items: [] }),
      backlog: safe('backlog', this.api.get<MobileSprintPage<MobileBacklogItem>>(`/api/sprints/projects/${id}/backlog?pageSize=100`), { items: [] }),
      roles: safe('yetkiler', this.api.get<readonly MobileRole[]>('/api/auth/roles?scope=Project'), [])
    }).pipe(map(value => ({ summary: value.summary, risks: value.risks, tasks: value.work.items, workflow: value.workflow, sprints: value.sprints.items, backlog: value.backlog.items, roles: value.roles, failures })));
  }

  changeStatus(itemId: string, status: string): Observable<MobileWorkItemRecord> {
    return this.api.patch<MobileWorkItemRecord>(`/api/work-items/${encodeURIComponent(itemId)}/status`, { status });
  }
}
