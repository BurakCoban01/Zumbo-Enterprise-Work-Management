import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { ProjectWorkflow } from '../work-items/project-work-item.models';
import { ProjectWorkItemService } from '../work-items/project-work-item.service';
import { ProjectListData } from './project-list.models';

@Injectable()
export class ProjectListService {
  private readonly api = inject(ZumboApiClient);
  private readonly workItems = inject(ProjectWorkItemService);

  load(projectId: string): Observable<ProjectListData> {
    return forkJoin({
      collection: this.workItems.load(projectId),
      workflow: this.api.get<ProjectWorkflow>(`/api/workflows/${encodeURIComponent(projectId)}`)
    }).pipe(map(result => ({ ...result.collection, workflow: result.workflow })));
  }
}
