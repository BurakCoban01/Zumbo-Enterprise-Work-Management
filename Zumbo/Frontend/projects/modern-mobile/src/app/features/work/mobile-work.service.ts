import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin } from 'rxjs';
import { MobileSearchResult, MobileWorkflow } from '../../shell/mobile-workspace.models';

@Injectable({ providedIn: 'root' })
export class MobileWorkService {
  private readonly api = inject(ZumboApiClient);

  search(projectId: string, text: string, page = 1, pageSize = 50, status?: string): Observable<MobileSearchResult> {
    return this.api.post<MobileSearchResult>('/api/work-items/search', {
      projectId,
      text: text.trim() || null,
      status: status || null,
      page,
      pageSize
    });
  }

  loadProject(projectId: string, status?: string) {
    return forkJoin({
      result: this.search(projectId, '', 1, 50, status),
      workflow: this.api.get<MobileWorkflow>(`/api/workflows/${encodeURIComponent(projectId)}`)
    });
  }
}
