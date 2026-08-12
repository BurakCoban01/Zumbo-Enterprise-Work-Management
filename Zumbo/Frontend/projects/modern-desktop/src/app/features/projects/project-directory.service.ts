import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable } from 'rxjs';
import { BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ProjectRoleSummary, UpdateProjectRequest } from './project-directory.models';

@Injectable()
export class ProjectDirectoryService {
  private readonly api = inject(ZumboApiClient);

  loadRoles(): Observable<readonly ProjectRoleSummary[]> {
    return this.api.get<readonly ProjectRoleSummary[]>('/api/auth/roles?scope=Project');
  }

  loadBoards(projectId: string): Observable<readonly BoardSummary[]> {
    return this.api.get<readonly BoardSummary[]>(`/api/boards/by-project/${encodeURIComponent(projectId)}`);
  }

  update(projectId: string, request: UpdateProjectRequest): Observable<ProjectSummary> {
    return this.api.put<ProjectSummary>(`/api/projects/${encodeURIComponent(projectId)}`, request);
  }

  archive(projectId: string): Observable<unknown> {
    return this.api.delete(`/api/projects/${encodeURIComponent(projectId)}`);
  }
}
