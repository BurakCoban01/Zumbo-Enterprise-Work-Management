import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { AutomationAudit, AutomationContext, AutomationDryRun, AutomationPage, AutomationRole, AutomationRule, AutomationRuleDraft, AutomationRuleSummary, AutomationRun, AutomationSchema, AutomationUser, WorkRecurrence, WorkRecurrenceDraft, WorkRecurrenceOccurrence, WorkRecurrencePreview, WorkTemplate, WorkTemplateDraft } from './automation.models';
import { recurrenceRequest, ruleRequest, templateRequest } from './automation.core';

@Injectable()
export class AutomationService {
  private readonly api = inject(ZumboApiClient);
  load(projectId: string): Observable<AutomationContext> { const id = encodeURIComponent(projectId); return forkJoin({ rules: this.api.get<AutomationPage<AutomationRuleSummary>>(`/api/automations?projectId=${id}&page=1&pageSize=100`), runs: this.api.get<AutomationPage<AutomationRun>>(`/api/automations/runs?projectId=${id}&page=1&pageSize=100`), templates: this.api.get<AutomationPage<WorkTemplate>>(`/api/work-items/templates?projectId=${id}`), recurrences: this.api.get<AutomationPage<WorkRecurrence>>(`/api/work-items/recurrences?projectId=${id}`), roles: this.api.get<readonly AutomationRole[]>('/api/auth/roles?scope=Project'), users: this.api.get<readonly AutomationUser[]>('/api/auth/users'), schema: this.api.get<AutomationSchema>(`/api/work-item-schemas/${id}`) }).pipe(map(data => ({ rules: data.rules.items, runs: data.runs.items, templates: data.templates.items, recurrences: data.recurrences.items, roles: data.roles, users: data.users, schema: data.schema }))); }
  rule(id: string, draft: boolean): Observable<AutomationRule> { return this.api.get(`/api/automations/${encodeURIComponent(id)}${draft ? '?draft=true' : ''}`); }
  saveRule(projectId: string, draft: AutomationRuleDraft): Observable<AutomationRule> { const body = ruleRequest(projectId, draft); return draft.id ? this.api.put(`/api/automations/${encodeURIComponent(draft.id)}/draft`, body) : this.api.post('/api/automations', body); }
  publishRule(id: string): Observable<AutomationRule> { return this.api.post(`/api/automations/${encodeURIComponent(id)}/publish`, {}); }
  setRuleState(id: string, active: boolean, version: number): Observable<AutomationRule> { return this.api.patch(`/api/automations/${encodeURIComponent(id)}/state`, { active }, { ifMatch: version }); }
  archiveRule(id: string, version: number): Observable<void> { return this.api.delete(`/api/automations/${encodeURIComponent(id)}`, { ifMatch: version }); }
  dryRun(id: string): Observable<AutomationDryRun> { return this.api.post(`/api/automations/${encodeURIComponent(id)}/dry-run`, { sourceId: null, status: null, previousStatus: null, priority: null, type: null, assigneeUserId: null, actorUserId: null, labels: [] }); }
  runs(projectId: string, status: string): Observable<AutomationPage<AutomationRun>> { const filter = status ? `&status=${encodeURIComponent(status)}` : ''; return this.api.get(`/api/automations/runs?projectId=${encodeURIComponent(projectId)}&page=1&pageSize=100${filter}`); }
  replayRun(id: string, version: number): Observable<AutomationRun> { return this.api.post(`/api/automations/runs/${encodeURIComponent(id)}/replay`, {}, { ifMatch: version }); }
  saveTemplate(projectId: string, draft: WorkTemplateDraft): Observable<WorkTemplate> { const body = templateRequest(projectId, draft); return draft.id ? this.api.put(`/api/work-items/templates/${encodeURIComponent(draft.id)}`, body, { ifMatch: draft.version }) : this.api.post('/api/work-items/templates', body); }
  archiveTemplate(id: string, version: number): Observable<void> { return this.api.delete(`/api/work-items/templates/${encodeURIComponent(id)}`, { ifMatch: version }); }
  previewRecurrence(projectId: string, draft: WorkRecurrenceDraft): Observable<WorkRecurrencePreview> { return this.api.post('/api/work-items/recurrences/preview', { ...(recurrenceRequest(projectId, draft) as object), previewCount: 5 }); }
  createRecurrence(projectId: string, draft: WorkRecurrenceDraft): Observable<WorkRecurrence> { return this.api.post('/api/work-items/recurrences', recurrenceRequest(projectId, draft)); }
  setRecurrenceState(id: string, active: boolean, version: number): Observable<WorkRecurrence> { return this.api.patch(`/api/work-items/recurrences/${encodeURIComponent(id)}/state`, { active }, { ifMatch: version }); }
  archiveRecurrence(id: string, version: number): Observable<void> { return this.api.delete(`/api/work-items/recurrences/${encodeURIComponent(id)}`, { ifMatch: version }); }
  occurrences(id: string): Observable<AutomationPage<WorkRecurrenceOccurrence>> { return this.api.get(`/api/work-items/recurrences/${encodeURIComponent(id)}/occurrences?page=1&pageSize=100`); }
  audit(type: 'AutomationRule' | 'WorkItemTemplate' | 'WorkItemRecurrence', id: string): Observable<readonly AutomationAudit[]> { return this.api.get(`/api/audit/entity/${type}/${encodeURIComponent(id)}`); }
}
