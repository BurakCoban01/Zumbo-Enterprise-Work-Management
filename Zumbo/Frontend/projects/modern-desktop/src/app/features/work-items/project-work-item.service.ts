import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import {
  BulkWorkItemResponse,
  ProjectWorkItem,
  ProjectWorkItemCollection,
  ProjectWorkItemRole,
  ProjectWorkItemSearchResult,
  ProjectWorkItemUpdate,
  ProjectWorkItemUser
} from './project-work-item.models';

@Injectable({ providedIn: 'root' })
export class ProjectWorkItemService {
  private readonly api = inject(ZumboApiClient);

  load(projectId: string): Observable<ProjectWorkItemCollection> {
    return forkJoin({
      search: this.api.post<ProjectWorkItemSearchResult>('/api/work-items/search', { projectId, page: 1, pageSize: 100 }),
      users: this.api.get<readonly ProjectWorkItemUser[]>('/api/auth/users').pipe(catchError(() => of([]))),
      roles: this.api.get<readonly ProjectWorkItemRole[]>('/api/auth/roles?scope=Project').pipe(catchError(() => of([])))
    }).pipe(map(result => ({
      tasks: [...result.search.items].sort(compareProjectWorkItemRank),
      totalCount: result.search.totalCount,
      degraded: result.search.degraded === true,
      users: result.users,
      roles: result.roles
    })));
  }

  update(task: ProjectWorkItem, update: ProjectWorkItemUpdate): Observable<ProjectWorkItem> {
    return this.api.put<ProjectWorkItem>(`/api/work-items/${encodeURIComponent(task.id)}`, update, { ifMatch: task.version });
  }

  bulkMove(workItemIds: readonly string[], status: string): Observable<BulkWorkItemResponse> {
    return this.api.post<BulkWorkItemResponse>('/api/work-items/bulk/move', { workItemIds, status });
  }

  bulkAssign(workItemIds: readonly string[], assigneeUserId: string): Observable<BulkWorkItemResponse> {
    return this.api.post<BulkWorkItemResponse>('/api/work-items/bulk/assign', { workItemIds, assigneeUserId });
  }

  bulkArchive(workItemIds: readonly string[]): Observable<BulkWorkItemResponse> {
    return this.api.post<BulkWorkItemResponse>('/api/work-items/bulk/archive', { workItemIds });
  }
}

export function compareProjectWorkItemRank(left: ProjectWorkItem, right: ProjectWorkItem): number {
  return left.rank - right.rank || left.id.localeCompare(right.id);
}
