import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { MobileCatalogAudit, MobileCatalogProject, MobileCatalogRole, MobileCatalogUser, MobileComponentDraft, MobileMilestoneDraft, MobileProjectCatalogData, MobileReleaseDraft, MobileTemplateDraft } from './mobile-project-catalog.models';
import { mobileCatalogAudit } from './mobile-project-catalog.core';

@Injectable()
export class MobileProjectCatalogService {
  private readonly api = inject(ZumboApiClient);

  load(projectId: string): Observable<MobileProjectCatalogData> {
    const id = encodeURIComponent(projectId);
    return forkJoin({
      project: this.api.get<MobileCatalogProject>(`/api/projects/${id}`),
      roles: this.api.get<readonly MobileCatalogRole[]>('/api/auth/roles?scope=Project'),
      users: this.api.get<readonly MobileCatalogUser[]>('/api/auth/users'),
      audit: this.api.get<readonly MobileCatalogAudit[]>(`/api/audit/entity/Project/${id}`)
    }).pipe(map(data => ({ ...data, audit: mobileCatalogAudit(data.audit) })));
  }

  refreshAudit(projectId: string): Observable<readonly MobileCatalogAudit[]> {
    return this.api.get<readonly MobileCatalogAudit[]>(`/api/audit/entity/Project/${encodeURIComponent(projectId)}`).pipe(map(mobileCatalogAudit));
  }

  saveTemplate(projectId: string, draft: MobileTemplateDraft, names: readonly string[]): Observable<MobileCatalogProject> {
    const path = `/api/projects/${encodeURIComponent(projectId)}/templates`;
    const body = { name: draft.name.trim(), isDefault: draft.isDefault, defaultComponentNames: names };
    return draft.id ? this.api.put<MobileCatalogProject>(`${path}/${encodeURIComponent(draft.id)}`, body) : this.api.post<MobileCatalogProject>(path, body);
  }

  archiveTemplate(projectId: string, id: string): Observable<MobileCatalogProject> { return this.api.delete<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/templates/${encodeURIComponent(id)}`); }
  saveComponent(projectId: string, draft: MobileComponentDraft): Observable<MobileCatalogProject> {
    const path = `/api/projects/${encodeURIComponent(projectId)}/components`;
    const body = { name: draft.name.trim(), description: draft.description.trim() || null };
    return draft.id ? this.api.put<MobileCatalogProject>(`${path}/${encodeURIComponent(draft.id)}`, body) : this.api.post<MobileCatalogProject>(path, body);
  }

  archiveComponent(projectId: string, id: string): Observable<MobileCatalogProject> { return this.api.delete<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/components/${encodeURIComponent(id)}`); }
  createVersion(projectId: string, name: string): Observable<MobileCatalogProject> { return this.api.post<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/versions`, { name: name.trim() }); }
  archiveVersion(projectId: string, id: string): Observable<MobileCatalogProject> { return this.api.delete<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/versions/${encodeURIComponent(id)}`); }
  createRelease(projectId: string, draft: MobileReleaseDraft): Observable<MobileCatalogProject> { return this.api.post<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/releases`, { versionId: draft.versionId, name: draft.name.trim(), scheduledAt: draft.scheduledAt || null }); }
  approveRelease(projectId: string, id: string): Observable<MobileCatalogProject> { return this.api.post<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/releases/${encodeURIComponent(id)}/approve`, {}); }
  publishRelease(projectId: string, id: string): Observable<MobileCatalogProject> { return this.api.post<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/releases/${encodeURIComponent(id)}/publish`, {}); }
  saveMilestone(projectId: string, draft: MobileMilestoneDraft): Observable<MobileCatalogProject> {
    const path = `/api/projects/${encodeURIComponent(projectId)}/milestones`;
    const body = { name: draft.name.trim(), dueAt: draft.dueAt };
    return draft.id ? this.api.put<MobileCatalogProject>(`${path}/${encodeURIComponent(draft.id)}`, body) : this.api.post<MobileCatalogProject>(path, body);
  }

  completeMilestone(projectId: string, id: string): Observable<MobileCatalogProject> { return this.api.post<MobileCatalogProject>(`/api/projects/${encodeURIComponent(projectId)}/milestones/${encodeURIComponent(id)}/complete`, {}); }
}
