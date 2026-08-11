import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map, switchMap } from 'rxjs';
import { ProjectWorkItem, ProjectWorkflow } from '../../work-items/project-work-item.models';
import { ProjectWorkItemService } from '../../work-items/project-work-item.service';
import { ProjectSprint, SprintPage } from '../project-planning.models';
import { PlanningViewData } from './project-planning-view.models';

@Injectable()
export class ProjectPlanningViewService {
  private readonly api = inject(ZumboApiClient);
  private readonly workItems = inject(ProjectWorkItemService);

  load(projectId: string): Observable<PlanningViewData> {
    const id = encodeURIComponent(projectId);
    return forkJoin({
      collection: this.workItems.loadAll(projectId),
      sprints: this.loadSprints(id),
      workflow: this.api.get<ProjectWorkflow>(`/api/workflows/${id}`)
    }).pipe(map(result => ({ ...result.collection, sprints: result.sprints, workflow: result.workflow })));
  }

  updateDueDate(task: ProjectWorkItem, dueDate: string): Observable<ProjectWorkItem> {
    return this.workItems.update(task, {
      title: task.title,
      description: task.description ?? '',
      priority: task.priority,
      dueDate: `${dueDate}T00:00:00.000Z`
    });
  }

  private loadSprints(projectId: string, cursor?: string, collected: readonly ProjectSprint[] = []): Observable<readonly ProjectSprint[]> {
    const suffix = cursor ? `&after=${encodeURIComponent(cursor)}` : '';
    return this.api.get<SprintPage<ProjectSprint>>(`/api/sprints/projects/${projectId}?pageSize=50${suffix}`).pipe(
      switchMap(page => page.nextCursor ? this.loadSprints(projectId, page.nextCursor, [...collected, ...page.items]) : [[...collected, ...page.items]])
    );
  }
}
