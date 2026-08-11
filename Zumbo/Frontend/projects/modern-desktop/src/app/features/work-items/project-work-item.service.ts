import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import {
  BulkWorkItemResponse,
  CreateProjectWorkItem,
  ProjectWorkItem,
  ProjectWorkItemCollection,
  ProjectWorkItemDetail,
  ProjectWorkItemRole,
  ProjectWorkItemSearchResult,
  ProjectWorkItemUpdate,
  ProjectWorkItemUser,
  WorkItemSchema
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

  loadAll(projectId: string): Observable<ProjectWorkItemCollection> {
    return this.load(projectId).pipe(switchMap(first => {
      const pageSize = 100;
      const pageCount = Math.ceil(first.totalCount / pageSize);
      if (pageCount <= 1) return of(first);
      return forkJoin(Array.from({ length: pageCount - 1 }, (_, index) =>
        this.api.post<ProjectWorkItemSearchResult>('/api/work-items/search', { projectId, page: index + 2, pageSize })
      )).pipe(map(pages => ({
        ...first,
        tasks: [...first.tasks, ...pages.flatMap(page => page.items)].sort(compareProjectWorkItemRank),
        degraded: first.degraded || pages.some(page => page.degraded === true)
      })));
    }));
  }

  update(task: ProjectWorkItem, update: ProjectWorkItemUpdate): Observable<ProjectWorkItem> {
    return this.api.put<ProjectWorkItem>(`/api/work-items/${encodeURIComponent(task.id)}`, update, { ifMatch: task.version });
  }

  get(workItemId: string): Observable<ProjectWorkItemDetail> {
    return this.api.get<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}`);
  }

  schema(projectId: string): Observable<WorkItemSchema> {
    return this.api.get<WorkItemSchema>(`/api/work-item-schemas/${encodeURIComponent(projectId)}`);
  }

  create(request: CreateProjectWorkItem): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>('/api/work-items', request);
  }

  archive(task: ProjectWorkItem): Observable<{ readonly archived: boolean }> {
    return this.api.delete<{ readonly archived: boolean }>(`/api/work-items/${encodeURIComponent(task.id)}`, { ifMatch: task.version });
  }

  addComment(workItemId: string, body: string): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/comments`, { body, mentions: [] });
  }

  addChecklist(workItemId: string, text: string): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/checklist`, { text });
  }

  setChecklist(workItemId: string, entryId: string, completed: boolean): Observable<ProjectWorkItemDetail> {
    return this.api.patch<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/checklist/${encodeURIComponent(entryId)}`, { completed });
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
