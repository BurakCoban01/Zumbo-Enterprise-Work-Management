import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin } from 'rxjs';
import { IntakeContext, IntakeForm, IntakeFormDraft, IntakePage, IntakeRole, IntakeSchema, IntakeSubmission, IntakeSubmissionConfirmation, PublishedIntakeForm } from './intake.models';
import { intakeDraftRequest } from './intake-form.core';

@Injectable()
export class IntakeService {
  private readonly api = inject(ZumboApiClient);

  load(projectId: string): Observable<IntakeContext> {
    const id = encodeURIComponent(projectId);
    return forkJoin({
      forms: this.api.get<readonly IntakeForm[]>(`/api/intake/forms?projectId=${id}`),
      roles: this.api.get<readonly IntakeRole[]>('/api/auth/roles?scope=Project'),
      schema: this.api.get<IntakeSchema>(`/api/work-item-schemas/${id}`)
    });
  }

  save(draft: IntakeFormDraft): Observable<IntakeForm> {
    const request = intakeDraftRequest(draft);
    return draft.id
      ? this.api.put<IntakeForm>(`/api/intake/forms/${encodeURIComponent(draft.id)}`, request)
      : this.api.post<IntakeForm>('/api/intake/forms', request);
  }

  publish(formId: string): Observable<IntakeForm> { return this.api.post<IntakeForm>(`/api/intake/forms/${encodeURIComponent(formId)}/publish`, {}); }
  archive(formId: string): Observable<IntakeForm> { return this.api.post<IntakeForm>(`/api/intake/forms/${encodeURIComponent(formId)}/archive`, {}); }
  published(formId: string): Observable<PublishedIntakeForm> { return this.api.get<PublishedIntakeForm>(`/api/intake/forms/${encodeURIComponent(formId)}/published`); }

  submit(formId: string, body: FormData): Observable<IntakeSubmissionConfirmation> {
    return this.api.post<IntakeSubmissionConfirmation>(`/api/intake/forms/${encodeURIComponent(formId)}/submissions`, body, { idempotencyKey: this.api.newIdempotencyKey() });
  }

  queue(formId: string, state: string): Observable<IntakePage<IntakeSubmission>> {
    const filter = state ? `&state=${encodeURIComponent(state)}` : '';
    return this.api.get<IntakePage<IntakeSubmission>>(`/api/intake/forms/${encodeURIComponent(formId)}/submissions?page=1&pageSize=100${filter}`);
  }

  triage(formId: string, submissionId: string, state: string, note: string): Observable<IntakeSubmission> {
    return this.api.post<IntakeSubmission>(`/api/intake/forms/${encodeURIComponent(formId)}/submissions/${encodeURIComponent(submissionId)}/triage`, { state, note: note.trim() || null });
  }
}
