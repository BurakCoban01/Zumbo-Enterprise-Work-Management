import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin } from 'rxjs';
import { MobileProjectIntakeContext, MobileProjectIntakeForm, MobileProjectIntakeDraft, MobileProjectIntakePage, MobileProjectIntakeProject, MobileProjectIntakePublishedForm, MobileProjectIntakeSubmission, MobileProjectIntakeConfirmation, MobileProjectIntakeRole, MobileProjectIntakeBoard, MobileProjectIntakeSchema } from './mobile-project-intake.models';
import { mobileProjectIntakeRequest } from './mobile-project-intake.core';

@Injectable()
export class MobileProjectIntakeService {
  private readonly api = inject(ZumboApiClient);
  load(projectId: string): Observable<MobileProjectIntakeContext> { const id = encodeURIComponent(projectId); return forkJoin({ project: this.api.get<MobileProjectIntakeProject>(`/api/projects/${id}`), boards: this.api.get<readonly MobileProjectIntakeBoard[]>(`/api/boards/by-project/${id}`), roles: this.api.get<readonly MobileProjectIntakeRole[]>('/api/auth/roles?scope=Project'), schema: this.api.get<MobileProjectIntakeSchema>(`/api/work-item-schemas/${id}`), forms: this.api.get<readonly MobileProjectIntakeForm[]>(`/api/intake/forms?projectId=${id}`) }); }
  save(draft: MobileProjectIntakeDraft): Observable<MobileProjectIntakeForm> { const request = mobileProjectIntakeRequest(draft); return draft.id ? this.api.put<MobileProjectIntakeForm>(`/api/intake/forms/${encodeURIComponent(draft.id)}`, request) : this.api.post<MobileProjectIntakeForm>('/api/intake/forms', request); }
  publish(formId: string): Observable<MobileProjectIntakeForm> { return this.api.post<MobileProjectIntakeForm>(`/api/intake/forms/${encodeURIComponent(formId)}/publish`, {}); }
  archive(formId: string): Observable<MobileProjectIntakeForm> { return this.api.post<MobileProjectIntakeForm>(`/api/intake/forms/${encodeURIComponent(formId)}/archive`, {}); }
  published(formId: string): Observable<MobileProjectIntakePublishedForm> { return this.api.get<MobileProjectIntakePublishedForm>(`/api/intake/forms/${encodeURIComponent(formId)}/published`); }
  submit(formId: string, body: FormData): Observable<MobileProjectIntakeConfirmation> { return this.api.post<MobileProjectIntakeConfirmation>(`/api/intake/forms/${encodeURIComponent(formId)}/submissions`, body, { idempotencyKey: this.api.newIdempotencyKey() }); }
  queue(formId: string, state: string): Observable<MobileProjectIntakePage<MobileProjectIntakeSubmission>> { return this.api.get<MobileProjectIntakePage<MobileProjectIntakeSubmission>>(`/api/intake/forms/${encodeURIComponent(formId)}/submissions?page=1&pageSize=100${state ? `&state=${encodeURIComponent(state)}` : ''}`); }
  triage(formId: string, submissionId: string, state: string, note: string): Observable<MobileProjectIntakeSubmission> { return this.api.post<MobileProjectIntakeSubmission>(`/api/intake/forms/${encodeURIComponent(formId)}/submissions/${encodeURIComponent(submissionId)}/triage`, { state, note: note.trim() || null }); }
}
