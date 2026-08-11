import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import {
  compatibleIntakeFields,
  editIntakeDraft,
  hasIntakePermission,
  intakeAccessLabel,
  intakeErrorMessage,
  intakeFieldTypes,
  intakeLimits,
  intakeStateLabel,
  newField,
  newIntakeDraft,
  validateIntakeDraft
} from './intake-form.core';
import {
  IntakeFieldDraft,
  IntakeForm,
  IntakeFormDraft,
  IntakeSubmission,
  IntakeSubmissionConfirmation,
  IntakeSubmissionModel,
  IntakeTab,
  PublishedIntakeForm
} from './intake.models';
import {
  intakeTriageStates,
  newSubmissionModel,
  securityLabel,
  submissionFormData,
  submissionValue,
  validateSubmission
} from './intake-submission.core';
import { IntakeService } from './intake.service';

@Component({
  selector: 'zumbo-intake-page',
  imports: [CommonModule, ZumboIconComponent],
  providers: [IntakeService],
  templateUrl: './intake.page.html',
  styleUrls: ['./intake.page.scss', './intake-layout.scss', './intake-responsive.scss', './intake-theme.scss']
})
export class IntakePage {
  readonly project = input.required<ProjectSummary>();
  readonly boards = input.required<readonly BoardSummary[]>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();
  readonly openWorkItem = output<string>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly intake = inject(IntakeService);
  private contextId = '';

  protected readonly loading = signal(true);
  protected readonly queueLoading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly tab = signal<IntakeTab>('forms');
  protected readonly forms = signal<readonly IntakeForm[]>([]);
  protected readonly roles = signal<readonly import('./intake.models').IntakeRole[]>([]);
  protected readonly schema = signal<import('./intake.models').IntakeSchema | null>(null);
  protected readonly selectedFormId = signal('');
  protected readonly draft = signal<IntakeFormDraft | null>(null);
  protected readonly published = signal<PublishedIntakeForm | null>(null);
  protected readonly submission = signal<IntakeSubmissionModel | null>(null);
  protected readonly confirmation = signal<IntakeSubmissionConfirmation | null>(null);
  protected readonly queue = signal<readonly IntakeSubmission[]>([]);
  protected readonly queueTotal = signal(0);
  protected readonly queueState = signal('');
  protected readonly triageNotes = signal<Record<string, string>>({});

  protected readonly limits = intakeLimits;
  protected readonly fieldTypes = intakeFieldTypes;
  protected readonly triageStates = intakeTriageStates;
  protected readonly selectedForm = computed(() => this.forms().find(form => form.id === this.selectedFormId()) ?? null);
  protected readonly internalForms = computed(() => this.forms().filter(form => form.state === 'Published' && form.draft.accessPolicy === 'Internal'));
  protected readonly roleName = computed(() => this.project().members?.find(member => member.userId === this.userId())?.role ?? null);
  protected readonly canManage = computed(() => hasIntakePermission(this.roleName(), this.roles(), 'WorkflowManage'));
  protected readonly canSubmit = computed(() => hasIntakePermission(this.roleName(), this.roles(), 'WorkItemCreate'));
  protected readonly canTriage = computed(() => hasIntakePermission(this.roleName(), this.roles(), 'WorkItemUpdate'));
  protected readonly activeIssueTypes = computed(() => (this.schema()?.issueTypes ?? []).filter(type => type.active).sort((left, right) => left.position - right.position));
  protected readonly activeCustomFields = computed(() => (this.schema()?.customFields ?? []).filter(field => field.active !== false).sort((left, right) => left.position - right.position));
  protected readonly draftError = computed(() => validateIntakeDraft(this.draft()));
  protected readonly submissionError = computed(() => validateSubmission(this.published(), this.submission()));

  constructor() {
    effect(() => {
      const projectId = this.project().id;
      if (!this.contextReady() || projectId === this.contextId) return;
      this.contextId = projectId;
      this.load();
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.intake.load(this.project().id).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: context => {
        this.forms.set(context.forms);
        this.roles.set(context.roles);
        this.schema.set(context.schema);
        const selected = context.forms.find(form => form.id === this.selectedFormId())
          ?? context.forms.find(form => form.state !== 'Archived')
          ?? context.forms[0];
        if (selected) this.selectForm(selected);
        else this.draft.set(null);
      },
      error: error => this.error.set(intakeErrorMessage(error, 'Intake merkezi yüklenemedi.'))
    });
  }

  protected setTab(tab: IntakeTab): void {
    this.tab.set(tab);
    this.error.set(null);
    this.notice.set(null);
    if (tab === 'submit') this.selectSubmissionForm(this.internalForms()[0]);
    if (tab === 'triage') this.loadQueue();
  }

  protected newForm(): void {
    if (!this.canManage() || !this.boards().length) return;
    this.selectedFormId.set('');
    this.draft.set(this.createDraft());
    this.error.set(null);
    this.notice.set(null);
  }

  protected selectForm(form: IntakeForm): void {
    this.selectedFormId.set(form.id);
    this.draft.set(editIntakeDraft(form));
    this.error.set(null);
  }

  protected updateDraft(field: 'name' | 'description', event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    this.draft.update(draft => draft ? { ...draft, [field]: value } : draft);
  }

  protected updateDefinition(field: 'accessPolicy' | 'boardId' | 'workItemType' | 'defaultPriority' | 'confirmationMessage', event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.draft.update(draft => draft ? { ...draft, definition: { ...draft.definition, [field]: value } as IntakeFormDraft['definition'] } : draft);
  }

  protected addField(): void {
    this.draft.update(draft => draft ? { ...draft, definition: { ...draft.definition, fields: [...draft.definition.fields, newField(draft.definition.fields.length)] } } : draft);
  }

  protected removeField(index: number): void {
    this.draft.update(draft => {
      if (!draft || draft.definition.fields.length <= 1) return draft;
      const removed = draft.definition.fields[index];
      const fields = draft.definition.fields.filter((_, fieldIndex) => fieldIndex !== index);
      const mapping = draft.definition.mapping;
      return {
        ...draft,
        definition: {
          ...draft.definition, fields,
          mapping: {
            ...mapping,
            titleFieldKey: mapping.titleFieldKey === removed.key ? '' : mapping.titleFieldKey,
            descriptionFieldKey: mapping.descriptionFieldKey === removed.key ? '' : mapping.descriptionFieldKey,
            priorityFieldKey: mapping.priorityFieldKey === removed.key ? '' : mapping.priorityFieldKey,
            dueDateFieldKey: mapping.dueDateFieldKey === removed.key ? '' : mapping.dueDateFieldKey,
            customFields: mapping.customFields.filter(value => value.intakeFieldKey !== removed.key)
          }
        }
      };
    });
  }

  protected updateField(index: number, field: keyof IntakeFieldDraft, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
    const value = field === 'required' ? (target as HTMLInputElement).checked : target.value;
    this.draft.update(draft => draft ? {
      ...draft,
      definition: { ...draft.definition, fields: draft.definition.fields.map((item, itemIndex) => itemIndex === index ? { ...item, [field]: value } : item) }
    } : draft);
  }

  protected compatibleFields(target: 'title' | 'description' | 'priority' | 'dueDate' | 'custom'): readonly IntakeFieldDraft[] {
    return compatibleIntakeFields(this.draft()?.definition.fields ?? [], target);
  }

  protected updateMapping(field: 'titleFieldKey' | 'descriptionFieldKey' | 'priorityFieldKey' | 'dueDateFieldKey', event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.draft.update(draft => draft ? { ...draft, definition: { ...draft.definition, mapping: { ...draft.definition.mapping, [field]: value } } } : draft);
  }

  protected addCustomMapping(): void {
    this.draft.update(draft => draft ? { ...draft, definition: { ...draft.definition, mapping: { ...draft.definition.mapping, customFields: [...draft.definition.mapping.customFields, { intakeFieldKey: '', workItemFieldKey: '' }] } } } : draft);
  }

  protected updateCustomMapping(index: number, field: 'intakeFieldKey' | 'workItemFieldKey', event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.draft.update(draft => draft ? { ...draft, definition: { ...draft.definition, mapping: { ...draft.definition.mapping, customFields: draft.definition.mapping.customFields.map((item, itemIndex) => itemIndex === index ? { ...item, [field]: value } : item) } } } : draft);
  }

  protected removeCustomMapping(index: number): void {
    this.draft.update(draft => draft ? { ...draft, definition: { ...draft.definition, mapping: { ...draft.definition.mapping, customFields: draft.definition.mapping.customFields.filter((_, itemIndex) => itemIndex !== index) } } } : draft);
  }

  protected saveForm(): void {
    const draft = this.draft();
    if (!draft || !this.canManage() || this.busy() || this.draftError()) return;
    this.busy.set(true);
    this.clearFeedback();
    this.intake.save(draft).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: form => {
        this.upsertForm(form);
        this.selectForm(form);
        this.notice.set(draft.id ? 'Form taslağı güncellendi.' : 'Form taslağı oluşturuldu.');
      },
      error: error => {
        this.error.set(intakeErrorMessage(error, 'Form kaydedilemedi.'));
        if (error?.code === 'CONCURRENCY_CONFLICT') this.load();
      }
    });
  }

  protected publishForm(form: IntakeForm): void { this.mutateForm(this.intake.publish(form.id), 'Formun yeni sürümü yayınlandı.'); }
  protected archiveForm(form: IntakeForm): void { this.mutateForm(this.intake.archive(form.id), 'Form arşivlendi.'); }

  protected selectSubmissionForm(form?: IntakeForm): void {
    if (!form || !this.canSubmit()) { this.published.set(null); this.submission.set(null); return; }
    this.selectedFormId.set(form.id);
    this.busy.set(true);
    this.clearFeedback();
    this.intake.published(form.id).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: published => { this.published.set(published); this.submission.set(newSubmissionModel(published)); this.confirmation.set(null); },
      error: error => this.error.set(intakeErrorMessage(error, 'Yayındaki form yüklenemedi.'))
    });
  }

  protected updateSubmission(fieldKey: string, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
    const value = target instanceof HTMLInputElement && target.type === 'checkbox' ? target.checked : target.value;
    this.submission.update(model => model ? { ...model, values: { ...model.values, [fieldKey]: value } } : model);
  }

  protected captureFiles(fieldKey: string, event: Event): void {
    const files = Array.from((event.target as HTMLInputElement).files ?? []);
    this.submission.update(model => model ? { ...model, files: { ...model.files, [fieldKey]: files } } : model);
  }

  protected submitRequest(): void {
    const form = this.published(), model = this.submission(), selected = this.selectedForm();
    if (!form || !model || !selected || !this.canSubmit() || this.busy() || this.submissionError()) return;
    this.busy.set(true);
    this.clearFeedback();
    this.intake.submit(selected.id, submissionFormData(form, model)).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: confirmation => { this.confirmation.set(confirmation); this.submission.set(newSubmissionModel(form)); },
      error: error => this.error.set(intakeErrorMessage(error, 'Talep gönderilemedi.'))
    });
  }

  protected chooseQueueForm(event: Event): void { this.selectedFormId.set((event.target as HTMLSelectElement).value); this.loadQueue(); }
  protected chooseQueueState(event: Event): void { this.queueState.set((event.target as HTMLSelectElement).value); this.loadQueue(); }

  protected loadQueue(): void {
    const form = this.selectedForm() ?? this.forms()[0];
    if (!form) { this.queue.set([]); this.queueTotal.set(0); return; }
    this.selectedFormId.set(form.id);
    this.queueLoading.set(true);
    this.error.set(null);
    this.intake.queue(form.id, this.queueState()).pipe(finalize(() => this.queueLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => { this.queue.set(page.items); this.queueTotal.set(page.totalCount); },
      error: error => this.error.set(intakeErrorMessage(error, 'Triage kuyruğu yüklenemedi.'))
    });
  }

  protected updateTriageNote(submissionId: string, event: Event): void { this.triageNotes.update(notes => ({ ...notes, [submissionId]: (event.target as HTMLTextAreaElement).value })); }

  protected triage(submission: IntakeSubmission, state: string): void {
    const form = this.selectedForm();
    if (!form || !this.canTriage() || this.busy() || submission.state === 'Processing') return;
    this.busy.set(true);
    this.clearFeedback();
    this.intake.triage(form.id, submission.id, state, this.triageNotes()[submission.id] ?? '').pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => { this.queue.update(items => items.map(item => item.id === updated.id ? updated : item)); this.notice.set('Talep durumu güncellendi.'); },
      error: error => this.error.set(intakeErrorMessage(error, 'Talep sınıflandırılamadı.'))
    });
  }

  protected stateLabel(value: string): string { return intakeStateLabel(value); }
  protected accessLabel(value: string): string { return intakeAccessLabel(value); }
  protected securityLabel(value: string): string { return securityLabel(value); }
  protected submissionTitle(submission: IntakeSubmission): string { return submissionValue(submission, this.selectedForm()?.draft.mapping.titleFieldKey ?? '') || submission.confirmationCode; }
  protected openSubmission(submission: IntakeSubmission | IntakeSubmissionConfirmation): void { if (submission.workItemId) this.openWorkItem.emit(submission.workItemId); }

  private createDraft(workItemType?: string): IntakeFormDraft {
    const type = workItemType ?? this.activeIssueTypes().find(item => item.key === 'Task')?.key ?? this.activeIssueTypes()[0]?.key ?? '';
    return newIntakeDraft(this.project(), this.boards(), type);
  }

  private mutateForm(request: ReturnType<IntakeService['publish']>, message: string): void {
    if (!this.canManage() || this.busy()) return;
    this.busy.set(true);
    this.clearFeedback();
    request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: form => { this.upsertForm(form); this.selectForm(form); this.notice.set(message); },
      error: error => this.error.set(intakeErrorMessage(error, 'Form işlemi tamamlanamadı.'))
    });
  }

  private upsertForm(form: IntakeForm): void { this.forms.update(forms => forms.some(item => item.id === form.id) ? forms.map(item => item.id === form.id ? form : item) : [form, ...forms]); }
  private clearFeedback(): void { this.error.set(null); this.notice.set(null); }
}
