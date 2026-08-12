import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import {
  actionNeedsValue, actionTypeLabel, automationActionTypes, automationConditionFields,
  automationConditionOperators, automationError, automationEventTypes, automationLimits,
  conditionFieldLabel, conditionNeedsValue, conditionOperatorLabel, frequencyLabel,
  hasAutomationPermission, newRecurrenceDraft, newRuleDraft, newTemplateDraft,
  recurrenceState, ruleDraft, ruleState, runState, templateDraft, triggerLabel, validRule
} from './automation.core';
import {
  AutomationActionDraft, AutomationAudit, AutomationConditionDraft, AutomationContext,
  AutomationDryRun, AutomationRuleDraft, AutomationRuleSummary, AutomationRun, AutomationTab,
  WorkRecurrence, WorkRecurrenceDraft, WorkRecurrenceOccurrence, WorkRecurrencePreview,
  WorkTemplate, WorkTemplateDraft
} from './automation.models';
import { AutomationService } from './automation.service';

@Component({
  selector: 'zumbo-automation-page',
  imports: [CommonModule, ZumboIconComponent],
  providers: [AutomationService],
  templateUrl: './automation.page.html',
  styleUrls: ['./automation.page.scss', './automation-controls.scss', './automation-layout.scss', './automation-responsive.scss', './automation-theme.scss']
})
export class AutomationPage {
  readonly project = input.required<ProjectSummary>();
  readonly boards = input.required<readonly BoardSummary[]>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();
  readonly openWorkItem = output<string>();

  private readonly automation = inject(AutomationService);
  private readonly destroyRef = inject(DestroyRef);
  private contextId = '';

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly tab = signal<AutomationTab>('rules');
  protected readonly context = signal<AutomationContext | null>(null);
  protected readonly selectedRuleId = signal('');
  protected readonly ruleEditorOpen = signal(false);
  protected readonly ruleEditorLoading = signal(false);
  protected readonly ruleDraft = signal<AutomationRuleDraft>(newRuleDraft());
  protected readonly dryRun = signal<AutomationDryRun | null>(null);
  protected readonly runStatus = signal('');
  protected readonly selectedRunId = signal('');
  protected readonly templateDraft = signal<WorkTemplateDraft | null>(null);
  protected readonly recurrenceDraft = signal<WorkRecurrenceDraft>(newRecurrenceDraft());
  protected readonly recurrencePreview = signal<WorkRecurrencePreview | null>(null);
  protected readonly selectedRecurrenceId = signal('');
  protected readonly occurrences = signal<readonly WorkRecurrenceOccurrence[]>([]);
  protected readonly audit = signal<readonly AutomationAudit[]>([]);
  protected readonly auditTarget = signal('');

  protected readonly limits = automationLimits;
  protected readonly eventTypes = automationEventTypes;
  protected readonly conditionFields = automationConditionFields;
  protected readonly conditionOperators = automationConditionOperators;
  protected readonly actionTypes = automationActionTypes;
  protected readonly roleName = computed(() => this.project().members?.find(member => member.userId === this.userId())?.role ?? null);
  protected readonly canManageRules = computed(() => this.hasPermission('WorkflowManage'));
  protected readonly canCreateWork = computed(() => this.hasPermission('WorkItemCreate'));
  protected readonly canUpdateWork = computed(() => this.hasPermission('WorkItemUpdate'));
  protected readonly rules = computed(() => this.context()?.rules ?? []);
  protected readonly runs = computed(() => this.context()?.runs ?? []);
  protected readonly templates = computed(() => this.context()?.templates ?? []);
  protected readonly activeTemplates = computed(() => this.templates().filter(item => !item.archived));
  protected readonly recurrences = computed(() => this.context()?.recurrences ?? []);
  protected readonly activeRules = computed(() => this.rules().filter(item => item.active && !item.archived));
  protected readonly activeRecurrences = computed(() => this.recurrences().filter(item => item.active && !item.archived));
  protected readonly issueTypes = computed(() => (this.context()?.schema.issueTypes ?? []).filter(item => item.active).sort((left, right) => left.position - right.position));
  protected readonly selectedRun = computed(() => this.runs().find(item => item.id === this.selectedRunId()) ?? null);
  protected readonly selectedRecurrence = computed(() => this.recurrences().find(item => item.id === this.selectedRecurrenceId()) ?? null);
  protected readonly validRule = computed(() => validRule(this.ruleDraft()));
  protected readonly timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Yerel saat';

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
    this.automation.load(this.project().id).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: context => {
        this.context.set(context);
        this.selectedRunId.set(context.runs[0]?.id ?? '');
        this.selectedRecurrenceId.set(context.recurrences[0]?.id ?? '');
        this.recurrenceDraft.set(newRecurrenceDraft(context.templates.find(item => !item.archived)?.id));
        this.templateDraft.set(null);
        this.ruleEditorOpen.set(false);
      },
      error: error => this.error.set(automationError(error, 'Otomasyon kayıtları yüklenemedi.'))
    });
  }

  protected setTab(tab: AutomationTab): void {
    this.tab.set(tab);
    this.clearFeedback();
    if (tab === 'runs') this.loadRuns();
  }

  protected newRule(): void {
    if (!this.canManageRules()) return;
    this.selectedRuleId.set('');
    this.ruleDraft.set(newRuleDraft());
    this.ruleEditorOpen.set(true);
    this.dryRun.set(null);
  }

  protected selectRule(rule: AutomationRuleSummary): void {
    this.selectedRuleId.set(rule.id);
    this.ruleEditorLoading.set(true);
    this.automation.rule(rule.id, rule.hasDraft).pipe(
      finalize(() => this.ruleEditorLoading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: detail => { this.ruleDraft.set(ruleDraft(detail)); this.ruleEditorOpen.set(this.canManageRules()); this.loadAudit('AutomationRule', rule.id, rule.name); },
      error: error => this.error.set(automationError(error, 'Kural ayrıntısı yüklenemedi.'))
    });
  }

  protected updateRule(field: keyof AutomationRuleDraft, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
    const value = target.type === 'number' ? Number(target.value) : target.value;
    this.ruleDraft.update(draft => ({ ...draft, [field]: value }));
  }

  protected setRuleTrigger(value: 'Event' | 'Schedule'): void { this.ruleDraft.update(draft => ({ ...draft, triggerType: value })); }
  protected setConditionMode(value: 'All' | 'Any'): void { this.ruleDraft.update(draft => ({ ...draft, conditionMode: value })); }
  protected addCondition(): void { this.ruleDraft.update(draft => ({ ...draft, conditions: [...draft.conditions, { field: 'Status', operator: 'Equals', value: '' }] })); }
  protected addAction(): void { this.ruleDraft.update(draft => ({ ...draft, actions: [...draft.actions, { type: 'AddLabel', value: '' }] })); }
  protected removeCondition(index: number): void { this.ruleDraft.update(draft => ({ ...draft, conditions: draft.conditions.filter((_, itemIndex) => itemIndex !== index) })); }
  protected removeAction(index: number): void { this.ruleDraft.update(draft => ({ ...draft, actions: draft.actions.filter((_, itemIndex) => itemIndex !== index) })); }
  protected updateCondition(index: number, field: keyof AutomationConditionDraft, event: Event): void { const value = (event.target as HTMLInputElement | HTMLSelectElement).value; this.ruleDraft.update(draft => ({ ...draft, conditions: draft.conditions.map((item, itemIndex) => itemIndex === index ? { ...item, [field]: value } : item) })); }
  protected updateAction(index: number, field: keyof AutomationActionDraft, event: Event): void { const value = (event.target as HTMLInputElement | HTMLSelectElement).value; this.ruleDraft.update(draft => ({ ...draft, actions: draft.actions.map((item, itemIndex) => itemIndex === index ? { ...item, [field]: value } : item) })); }

  protected saveRule(): void {
    const draft = this.ruleDraft();
    if (!this.canManageRules() || !validRule(draft) || this.busy()) return;
    this.mutate(this.automation.saveRule(this.project().id, draft), 'Kural taslağı kaydedildi.', rule => {
      this.ruleDraft.set(ruleDraft(rule));
      this.selectedRuleId.set(rule.id);
      this.ruleEditorOpen.set(true);
      this.reloadContext();
    });
  }

  protected publishRule(): void {
    const id = this.ruleDraft().id;
    if (!id) return;
    this.mutate(this.automation.publishRule(id), 'Kural yayınlandı.', () => this.reloadContext());
  }

  protected setRuleState(rule: AutomationRuleSummary, active: boolean): void { this.mutate(this.automation.setRuleState(rule.id, active, rule.version), active ? 'Kural etkinleştirildi.' : 'Kural duraklatıldı.', () => this.reloadContext()); }
  protected archiveRule(rule: AutomationRuleSummary): void { this.mutate(this.automation.archiveRule(rule.id, rule.version), 'Kural arşivlendi.', () => { this.ruleEditorOpen.set(false); this.reloadContext(); }); }
  protected runDryRun(): void { const id = this.ruleDraft().id; if (!id || this.busy()) return; this.mutate(this.automation.dryRun(id), 'Dry-run tamamlandı.', result => this.dryRun.set(result)); }

  protected chooseRunStatus(event: Event): void { this.runStatus.set((event.target as HTMLSelectElement).value); this.loadRuns(); }
  protected loadRuns(): void { this.automation.runs(this.project().id, this.runStatus()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: page => { this.context.update(value => value ? { ...value, runs: page.items } : value); this.selectedRunId.set(page.items[0]?.id ?? ''); }, error: error => this.error.set(automationError(error, 'Çalıştırmalar yüklenemedi.')) }); }
  protected replayRun(run: AutomationRun): void { this.mutate(this.automation.replayRun(run.id, run.version), 'Çalıştırma yeniden sıraya alındı.', updated => this.context.update(value => value ? { ...value, runs: value.runs.map(item => item.id === updated.id ? updated : item) } : value)); }

  protected newTemplate(): void { const type = this.issueTypes().find(item => item.key === 'Task')?.key ?? this.issueTypes()[0]?.key ?? ''; this.templateDraft.set(newTemplateDraft(this.boards()[0]?.id ?? '', type)); }
  protected editTemplate(value: WorkTemplate): void { this.templateDraft.set(templateDraft(value)); this.loadAudit('WorkItemTemplate', value.id, value.name); }
  protected updateTemplate(field: keyof WorkTemplateDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement; const value = target.type === 'number' ? (target.value ? Number(target.value) : null) : target.value; this.templateDraft.update(draft => draft ? { ...draft, [field]: value } : draft); }
  protected saveTemplate(): void { const draft = this.templateDraft(); if (!draft || !this.canCreateWork() || !draft.name.trim() || !draft.title.trim() || !draft.boardId) return; this.mutate(this.automation.saveTemplate(this.project().id, draft), draft.id ? 'İş şablonu güncellendi.' : 'İş şablonu oluşturuldu.', () => { this.templateDraft.set(null); this.reloadContext(); }); }
  protected archiveTemplate(value: WorkTemplate): void { this.mutate(this.automation.archiveTemplate(value.id, value.version), 'İş şablonu arşivlendi.', () => this.reloadContext()); }

  protected updateRecurrence(field: keyof WorkRecurrenceDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLSelectElement; const value = target.type === 'number' ? Number(target.value) : target.value; this.recurrenceDraft.update(draft => ({ ...draft, [field]: value })); }
  protected previewRecurrence(): void { const draft = this.recurrenceDraft(); if (!draft.templateId) return; this.mutate(this.automation.previewRecurrence(this.project().id, draft), 'Takvim önizlendi.', result => this.recurrencePreview.set(result)); }
  protected createRecurrence(): void { const draft = this.recurrenceDraft(); if (!draft.templateId || !this.canCreateWork()) return; this.mutate(this.automation.createRecurrence(this.project().id, draft), 'Yineleme etkinleştirildi.', () => { this.recurrenceDraft.set(newRecurrenceDraft(this.activeTemplates()[0]?.id)); this.recurrencePreview.set(null); this.reloadContext(); }); }
  protected setRecurrenceState(value: WorkRecurrence, active: boolean): void { this.mutate(this.automation.setRecurrenceState(value.id, active, value.version), active ? 'Yineleme etkinleştirildi.' : 'Yineleme duraklatıldı.', () => this.reloadContext()); }
  protected archiveRecurrence(value: WorkRecurrence): void { this.mutate(this.automation.archiveRecurrence(value.id, value.version), 'Yineleme arşivlendi.', () => this.reloadContext()); }
  protected selectRecurrence(value: WorkRecurrence): void { this.selectedRecurrenceId.set(value.id); this.loadAudit('WorkItemRecurrence', value.id, this.templateName(value.templateId)); this.automation.occurrences(value.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: page => this.occurrences.set(page.items), error: error => this.error.set(automationError(error, 'Çalıştırma geçmişi yüklenemedi.')) }); }

  protected loadAudit(type: 'AutomationRule' | 'WorkItemTemplate' | 'WorkItemRecurrence', id: string, label: string): void { this.auditTarget.set(label); this.automation.audit(type, id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: entries => this.audit.set([...entries].sort((left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt))), error: error => this.error.set(automationError(error, 'Audit kaydı yüklenemedi.')) }); }
  protected templateName(id: string): string { return this.templates().find(item => item.id === id)?.name ?? 'Arşivlenmiş şablon'; }
  protected userName(id?: string | null): string { const user = this.context()?.users.find(item => item.id === id); return user?.username || user?.email || 'Sistem işlemi'; }
  protected actionLabel(value: string): string { return actionTypeLabel(value); }
  protected conditionFieldLabel(value: string): string { return conditionFieldLabel(value); }
  protected conditionOperatorLabel(value: string): string { return conditionOperatorLabel(value); }
  protected actionNeedsValue(value: string): boolean { return actionNeedsValue(value); }
  protected conditionNeedsValue(value: string): boolean { return conditionNeedsValue(value); }
  protected ruleState(value: AutomationRuleSummary): string { return ruleState(value); }
  protected triggerLabel(value: AutomationRuleSummary): string { return triggerLabel(value); }
  protected runState(value: string): string { return runState(value); }
  protected recurrenceState(value: WorkRecurrence): string { return recurrenceState(value); }
  protected frequencyLabel(value: WorkRecurrence): string { return frequencyLabel(value); }

  private hasPermission(permission: string): boolean { return hasAutomationPermission(this.roleName(), this.context()?.roles ?? [], permission); }
  private reloadContext(): void { const tab = this.tab(); this.automation.load(this.project().id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(context => { this.context.set(context); this.tab.set(tab); }); }
  private mutate<T>(request: import('rxjs').Observable<T>, message: string, accept: (value: T) => void): void { if (this.busy()) return; this.busy.set(true); this.clearFeedback(); request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { accept(value); this.notice.set(message); }, error: error => { this.error.set(automationError(error, 'İşlem tamamlanamadı.')); if (error?.code === 'CONCURRENCY_CONFLICT') this.reloadContext(); } }); }
  private clearFeedback(): void { this.error.set(null); this.notice.set(null); }
}
