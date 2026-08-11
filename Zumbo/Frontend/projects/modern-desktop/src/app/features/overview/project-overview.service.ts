import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import {
  OverviewRole,
  OverviewTeam,
  OverviewUser,
  ProjectAuditEntry,
  ProjectOverviewData,
  ProjectRiskItem,
  ProjectSprintPage,
  ProjectSummaryMetrics
} from './project-overview.models';

@Injectable()
export class ProjectOverviewService {
  private readonly api = inject(ZumboApiClient);

  load(projectId: string, organizationId?: string | null, boardId?: string | null): Observable<ProjectOverviewData> {
    const encodedProjectId = encodeURIComponent(projectId);
    const projectAudit = this.api.get<readonly ProjectAuditEntry[]>(`/api/audit/entity/Project/${encodedProjectId}`).pipe(catchError(() => of([])));
    const boardAudit = boardId
      ? this.api.get<readonly ProjectAuditEntry[]>(`/api/audit/entity/Board/${encodeURIComponent(boardId)}`).pipe(catchError(() => of([])))
      : of([] as readonly ProjectAuditEntry[]);
    return forkJoin({
      summary: this.api.get<ProjectSummaryMetrics>(`/api/work-items/reports/project-summary/${encodedProjectId}`).pipe(catchError(() => of(null))),
      risks: this.api.get<readonly ProjectRiskItem[]>(`/api/work-items/reports/due-date-risks/${encodedProjectId}?days=14`).pipe(catchError(() => of(null))),
      sprints: this.api.get<ProjectSprintPage>(`/api/sprints/projects/${encodedProjectId}?pageSize=50`).pipe(catchError(() => of(null))),
      projectAudit,
      boardAudit,
      users: this.api.get<readonly OverviewUser[]>('/api/auth/users').pipe(catchError(() => of([]))),
      roles: this.api.get<readonly OverviewRole[]>('/api/auth/roles?scope=Project').pipe(catchError(() => of([]))),
      teams: organizationId
        ? this.api.get<readonly OverviewTeam[]>(`/api/teams?organizationId=${encodeURIComponent(organizationId)}`).pipe(catchError(() => of([])))
        : of([] as readonly OverviewTeam[])
    }).pipe(map(result => {
      if (result.summary === null && result.risks === null && result.sprints === null) throw new Error('Overview unavailable.');
      const activity = [...result.projectAudit, ...result.boardAudit]
        .filter((entry, index, entries) => entries.findIndex(item => item.id === entry.id) === index)
        .sort((left, right) => right.createdAt.localeCompare(left.createdAt));
      return {
        summary: result.summary ?? { total: 0, done: 0, inProgress: 0, overdue: 0 },
        risks: result.risks ?? [],
        sprints: result.sprints?.items ?? [],
        activity,
        users: result.users,
        roles: result.roles,
        teams: result.teams,
        partial: result.summary === null || result.risks === null || result.sprints === null
      };
    }));
  }
}
