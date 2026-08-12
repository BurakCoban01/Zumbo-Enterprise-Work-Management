import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin } from 'rxjs';
import { MobileBulkJob, MobileBulkJobPage, MobileImportRow, MobileJobRole, MobileJobsProject } from './mobile-jobs.models';

@Injectable()
export class MobileJobsService {
  private readonly api = inject(ZumboApiClient);

  load(projectId: string): Observable<{ readonly project: MobileJobsProject; readonly roles: readonly MobileJobRole[]; readonly page: MobileBulkJobPage }> {
    const id = encodeURIComponent(projectId);
    return forkJoin({
      project: this.api.get<MobileJobsProject>(`/api/projects/${id}`),
      roles: this.api.get<readonly MobileJobRole[]>('/api/auth/roles?scope=Project'),
      page: this.api.get<MobileBulkJobPage>(`/api/work-items/bulk/jobs?projectId=${id}&page=1&pageSize=50`)
    });
  }

  list(projectId: string): Observable<MobileBulkJobPage> {
    const id = encodeURIComponent(projectId);
    return this.api.get<MobileBulkJobPage>(`/api/work-items/bulk/jobs?projectId=${id}&page=1&pageSize=50`);
  }
  import(projectId: string, items: readonly MobileImportRow[], dryRun: boolean): Observable<MobileBulkJob> {
    return this.api.post<MobileBulkJob>('/api/work-items/bulk/jobs/import', { projectId, items, dryRun }, { idempotencyKey: this.api.newIdempotencyKey() });
  }
  export(projectId: string, includeArchived: boolean, dryRun: boolean): Observable<MobileBulkJob> {
    return this.api.post<MobileBulkJob>('/api/work-items/bulk/jobs/export', { projectId, includeArchived, dryRun }, { idempotencyKey: this.api.newIdempotencyKey() });
  }
  cancel(job: MobileBulkJob): Observable<MobileBulkJob> { return this.api.post<MobileBulkJob>(`/api/work-items/bulk/jobs/${encodeURIComponent(job.id)}/cancel`, {}, { ifMatch: job.version }); }
  retry(job: MobileBulkJob): Observable<MobileBulkJob> { return this.api.post<MobileBulkJob>(`/api/work-items/bulk/jobs/${encodeURIComponent(job.id)}/retry`, {}, { ifMatch: job.version }); }
  artifact(job: MobileBulkJob, errors: boolean): Observable<Blob> { return this.api.download(`/api/work-items/bulk/jobs/${encodeURIComponent(job.id)}/${errors ? 'errors' : 'result'}`); }
}
