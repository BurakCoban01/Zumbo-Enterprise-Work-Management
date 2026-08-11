import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { HomeData, HomeNotification, ProjectSearchResult, WorkItemSearchResult } from './home.models';

@Injectable()
export class HomeService {
  private readonly api = inject(ZumboApiClient);

  load(projects: readonly ProjectSummary[], userId: string): Observable<HomeData> {
    const searches = projects.map(project => this.api.post<WorkItemSearchResult>('/api/work-items/search', {
      projectId: project.id,
      assigneeUserId: userId,
      page: 1,
      pageSize: 50
    }).pipe(
      map(result => ({ project, result }) satisfies ProjectSearchResult),
      catchError(() => of({ project, result: null } satisfies ProjectSearchResult))
    ));

    return forkJoin({
      searches: searches.length ? forkJoin(searches) : of([] as readonly ProjectSearchResult[]),
      notifications: this.api.get<readonly HomeNotification[]>('/api/notifications?page=1&pageSize=50')
    }).pipe(map(({ searches: results, notifications }) => ({
      tasks: results.flatMap(({ project, result }) => (result?.items ?? []).map(task => ({ ...task, projectName: project.name }))),
      notifications,
      partial: results.some(result => result.result === null)
    })));
  }

  markNotificationRead(notificationId: string): Observable<unknown> {
    return this.api.patch(`/api/notifications/${encodeURIComponent(notificationId)}/read`, {});
  }
}
