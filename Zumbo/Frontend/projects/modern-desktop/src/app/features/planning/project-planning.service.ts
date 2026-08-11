import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { ProjectWorkItemService } from '../work-items/project-work-item.service';
import {
  PlannedSprintItem,
  CreateSprintDraft,
  ProjectPlanningData,
  ProjectSprint,
  SprintBurndownPoint,
  SprintBacklogItem,
  SprintPage,
  SprintVelocity
} from './project-planning.models';

@Injectable()
export class ProjectPlanningService {
  private readonly api = inject(ZumboApiClient);
  private readonly workItems = inject(ProjectWorkItemService);

  load(projectId: string): Observable<ProjectPlanningData> {
    const encodedId = encodeURIComponent(projectId);
    return forkJoin({
      collection: this.workItems.load(projectId),
      sprints: this.api.get<SprintPage<ProjectSprint>>(`/api/sprints/projects/${encodedId}?pageSize=50`),
      backlog: this.api.get<SprintPage<SprintBacklogItem>>(`/api/sprints/projects/${encodedId}/backlog?pageSize=100`),
      velocity: this.api.get<readonly SprintVelocity[]>(`/api/sprints/projects/${encodedId}/velocity?sprintCount=3`)
    }).pipe(map(result => ({
      ...result.collection,
      sprints: result.sprints.items,
      sprintNextCursor: result.sprints.nextCursor,
      backlog: result.backlog.items,
      backlogNextCursor: result.backlog.nextCursor,
      velocity: result.velocity
    })));
  }

  loadMoreBacklog(projectId: string, cursor: string): Observable<SprintPage<SprintBacklogItem>> {
    return this.api.get<SprintPage<SprintBacklogItem>>(`/api/sprints/projects/${encodeURIComponent(projectId)}/backlog?pageSize=100&after=${encodeURIComponent(cursor)}`);
  }

  plan(item: SprintBacklogItem, sprintId: string): Observable<PlannedSprintItem> {
    return this.api.put<PlannedSprintItem>(
      `/api/sprints/${encodeURIComponent(sprintId)}/items/${encodeURIComponent(item.id)}`,
      { estimatePoints: item.estimatePoints || 0 },
      { ifMatch: item.version }
    );
  }

  unplan(workItemId: string, sprintId: string, version: number): Observable<PlannedSprintItem> {
    return this.api.delete<PlannedSprintItem>(
      `/api/sprints/${encodeURIComponent(sprintId)}/items/${encodeURIComponent(workItemId)}`,
      { ifMatch: version }
    );
  }

  createSprint(draft: CreateSprintDraft): Observable<ProjectSprint> {
    return this.api.post<ProjectSprint>('/api/sprints', draft);
  }

  startSprint(sprint: ProjectSprint): Observable<ProjectSprint> {
    return this.api.post<ProjectSprint>(`/api/sprints/${encodeURIComponent(sprint.id)}/start`, {}, { ifMatch: sprint.version });
  }

  completeSprint(sprint: ProjectSprint, carryoverSprintId: string | null): Observable<ProjectSprint> {
    return this.api.post<ProjectSprint>(
      `/api/sprints/${encodeURIComponent(sprint.id)}/complete`,
      { carryoverSprintId },
      { ifMatch: sprint.version }
    );
  }

  loadBurndown(sprintId: string): Observable<readonly SprintBurndownPoint[]> {
    return this.api.get<readonly SprintBurndownPoint[]>(`/api/sprints/${encodeURIComponent(sprintId)}/burndown`);
  }
}
