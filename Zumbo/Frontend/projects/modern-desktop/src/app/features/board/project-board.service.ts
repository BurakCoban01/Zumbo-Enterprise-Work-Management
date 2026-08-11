import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { ProjectWorkItemService, compareProjectWorkItemRank } from '../work-items/project-work-item.service';
import { BoardWorkItem, BoardWorkflow, ProjectBoardData } from './project-board.models';

@Injectable()
export class ProjectBoardService {
  private readonly api = inject(ZumboApiClient);
  private readonly workItems = inject(ProjectWorkItemService);

  load(projectId: string): Observable<ProjectBoardData> {
    const encodedId = encodeURIComponent(projectId);
    return forkJoin({
      collection: this.workItems.load(projectId),
      workflow: this.api.get<BoardWorkflow>(`/api/workflows/${encodedId}`)
    }).pipe(map(result => ({
      tasks: result.collection.tasks,
      totalCount: result.collection.totalCount,
      degraded: result.collection.degraded,
      workflow: result.workflow,
      users: result.collection.users,
      roles: result.collection.roles
    })));
  }

  changeStatus(taskId: string, status: string): Observable<BoardWorkItem> {
    return this.api.patch<BoardWorkItem>(`/api/work-items/${encodeURIComponent(taskId)}/status`, { status });
  }

  changeRank(taskId: string, beforeWorkItemId: string | null, afterWorkItemId: string | null): Observable<BoardWorkItem> {
    return this.api.patch<BoardWorkItem>(`/api/work-items/${encodeURIComponent(taskId)}/rank`, { beforeWorkItemId, afterWorkItemId });
  }
}

export function compareRank(left: BoardWorkItem, right: BoardWorkItem): number {
  return compareProjectWorkItemRank(left, right);
}
