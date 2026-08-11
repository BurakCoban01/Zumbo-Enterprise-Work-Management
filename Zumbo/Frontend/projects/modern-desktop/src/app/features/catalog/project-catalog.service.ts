import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { ComponentDraft, MilestoneDraft, ProjectCatalogData, ProjectCatalogProject, ProjectCatalogRole, ProjectCatalogUser, ProjectCatalogAudit, ReleaseDraft, TemplateDraft } from './project-catalog.models';

@Injectable()
export class ProjectCatalogService {
  private readonly api = inject(ZumboApiClient);
  load(projectId: string): Observable<ProjectCatalogData> {
    const id = encodeURIComponent(projectId);
    return forkJoin({
      project: this.api.get<ProjectCatalogProject>(`/api/projects/${id}`),
      roles: this.api.get<readonly ProjectCatalogRole[]>('/api/auth/roles?scope=Project'),
      users: this.api.get<readonly ProjectCatalogUser[]>('/api/auth/users'),
      audit: this.api.get<readonly ProjectCatalogAudit[]>(`/api/audit/entity/Project/${id}`)
    }).pipe(map(data => ({ ...data, audit: catalogAudit(data.audit) })));
  }
  refreshAudit(projectId: string): Observable<readonly ProjectCatalogAudit[]> { return this.api.get<readonly ProjectCatalogAudit[]>(`/api/audit/entity/Project/${encodeURIComponent(projectId)}`).pipe(map(catalogAudit)); }
  saveTemplate(projectId: string, draft: TemplateDraft, names: readonly string[]): Observable<ProjectCatalogProject> { const path = `/api/projects/${encodeURIComponent(projectId)}/templates`; const body = { name: draft.name.trim(), isDefault: draft.isDefault, defaultComponentNames: names }; return draft.id ? this.api.put(`${path}/${encodeURIComponent(draft.id)}`, body) : this.api.post(path, body); }
  archiveTemplate(projectId: string, id: string): Observable<ProjectCatalogProject> { return this.api.delete(`/api/projects/${encodeURIComponent(projectId)}/templates/${encodeURIComponent(id)}`); }
  saveComponent(projectId: string, draft: ComponentDraft): Observable<ProjectCatalogProject> { const path = `/api/projects/${encodeURIComponent(projectId)}/components`; const body = { name: draft.name.trim(), description: draft.description.trim() || null }; return draft.id ? this.api.put(`${path}/${encodeURIComponent(draft.id)}`, body) : this.api.post(path, body); }
  archiveComponent(projectId: string, id: string): Observable<ProjectCatalogProject> { return this.api.delete(`/api/projects/${encodeURIComponent(projectId)}/components/${encodeURIComponent(id)}`); }
  createVersion(projectId: string, name: string): Observable<ProjectCatalogProject> { return this.api.post(`/api/projects/${encodeURIComponent(projectId)}/versions`, { name: name.trim() }); }
  archiveVersion(projectId: string, id: string): Observable<ProjectCatalogProject> { return this.api.delete(`/api/projects/${encodeURIComponent(projectId)}/versions/${encodeURIComponent(id)}`); }
  createRelease(projectId: string, draft: ReleaseDraft): Observable<ProjectCatalogProject> { return this.api.post(`/api/projects/${encodeURIComponent(projectId)}/releases`, { versionId: draft.versionId, name: draft.name.trim(), scheduledAt: draft.scheduledAt || null }); }
  approveRelease(projectId: string, id: string): Observable<ProjectCatalogProject> { return this.api.post(`/api/projects/${encodeURIComponent(projectId)}/releases/${encodeURIComponent(id)}/approve`, {}); }
  publishRelease(projectId: string, id: string): Observable<ProjectCatalogProject> { return this.api.post(`/api/projects/${encodeURIComponent(projectId)}/releases/${encodeURIComponent(id)}/publish`, {}); }
  saveMilestone(projectId: string, draft: MilestoneDraft): Observable<ProjectCatalogProject> { const path = `/api/projects/${encodeURIComponent(projectId)}/milestones`; const body = { name: draft.name.trim(), dueAt: draft.dueAt }; return draft.id ? this.api.put(`${path}/${encodeURIComponent(draft.id)}`, body) : this.api.post(path, body); }
  completeMilestone(projectId: string, id: string): Observable<ProjectCatalogProject> { return this.api.post(`/api/projects/${encodeURIComponent(projectId)}/milestones/${encodeURIComponent(id)}/complete`, {}); }
}

function catalogAudit(entries: readonly ProjectCatalogAudit[]): readonly ProjectCatalogAudit[] { return entries.filter(entry => /^Project(?:Template|Component|Version|Release|Milestone)/.test(entry.action)).sort((a, b) => b.createdAt.localeCompare(a.createdAt)); }
