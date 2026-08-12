import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable } from 'rxjs';
import { BulkJob, BulkJobPage, ImportRow, JobRole } from './jobs.models';

@Injectable()
export class JobsService {
  private readonly api = inject(ZumboApiClient);
  list(projectId: string): Observable<BulkJobPage> { return this.api.get(`/api/work-items/bulk/jobs?projectId=${encodeURIComponent(projectId)}&page=1&pageSize=50`); }
  roles(): Observable<readonly JobRole[]> { return this.api.get('/api/auth/roles?scope=Project'); }
  import(projectId: string, items: readonly ImportRow[], dryRun: boolean): Observable<BulkJob> { return this.api.post('/api/work-items/bulk/jobs/import', { projectId, items, dryRun }, { idempotencyKey: this.api.newIdempotencyKey() }); }
  export(projectId: string, includeArchived: boolean, dryRun: boolean): Observable<BulkJob> { return this.api.post('/api/work-items/bulk/jobs/export', { projectId, includeArchived, dryRun }, { idempotencyKey: this.api.newIdempotencyKey() }); }
  cancel(job: BulkJob): Observable<BulkJob> { return this.api.post(`/api/work-items/bulk/jobs/${encodeURIComponent(job.id)}/cancel`, {}, { ifMatch: job.version }); }
  retry(job: BulkJob): Observable<BulkJob> { return this.api.post(`/api/work-items/bulk/jobs/${encodeURIComponent(job.id)}/retry`, {}, { ifMatch: job.version }); }
  artifact(job: BulkJob, errors: boolean): Observable<Blob> { return this.api.download(`/api/work-items/bulk/jobs/${encodeURIComponent(job.id)}/${errors ? 'errors' : 'result'}`); }
}
