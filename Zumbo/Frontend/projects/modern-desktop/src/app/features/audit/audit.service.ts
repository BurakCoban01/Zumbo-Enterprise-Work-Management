import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable } from 'rxjs';
import { AuditIntegrity, AuditPageResponse, AuditRole, AuditUser } from './audit.models';

@Injectable()
export class AuditService {
  private readonly api = inject(ZumboApiClient);
  roles(): Observable<readonly AuditRole[]> { return this.api.get('/api/auth/roles'); }
  users(): Observable<readonly AuditUser[]> { return this.api.get('/api/auth/users'); }
  search(query: string): Observable<AuditPageResponse> { return this.api.get(`/api/audit${query}`); }
  integrity(organizationId: string): Observable<AuditIntegrity> { return this.api.get(`/api/audit/integrity/${encodeURIComponent(organizationId)}`); }
  export(query: string): Observable<Blob> { return this.api.download(`/api/audit/export${query}`); }
}
