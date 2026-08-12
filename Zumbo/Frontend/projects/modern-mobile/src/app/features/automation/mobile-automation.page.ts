import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { IonBackButton, IonButtons, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { ZumboSessionService, normalizeApiError } from '@zumbo/modern-shared';
import { finalize, Observable } from 'rxjs';
import { MobileConnectivityService } from '../../shell/mobile-connectivity.service';
import { hasMobileAutomationPermission, mobileAutomationActionNeedsValue, mobileAutomationActionTypeLabel, mobileAutomationActionTypes, mobileAutomationAuditActionLabel, mobileAutomationConditionFieldLabel, mobileAutomationConditionFields, mobileAutomationConditionNeedsValue, mobileAutomationConditionOperatorLabel, mobileAutomationConditionOperators, mobileAutomationError, mobileAutomationEventTypes, mobileAutomationLabels, mobileAutomationLimits, mobileAutomationRuleDraft, mobileAutomationRuleState, mobileAutomationRunState, mobileAutomationTriggerLabel, mobileWorkOccurrenceState, mobileWorkRecurrenceFrequency, mobileWorkRecurrenceState, mobileWorkTemplateDraft, newMobileAutomationRuleDraft, newMobileWorkRecurrenceDraft, newMobileWorkTemplateDraft, validMobileAutomationRule, validMobileWorkRecurrence } from './mobile-automation.core';
import { MobileAutomationActionDraft, MobileAutomationAudit, MobileAutomationConditionDraft, MobileAutomationContext, MobileAutomationDryRun, MobileAutomationDryRunInput, MobileAutomationRuleDraft, MobileAutomationRuleSummary, MobileAutomationRun, MobileAutomationTab, MobileWorkRecurrence, MobileWorkRecurrenceDraft, MobileWorkRecurrenceOccurrence, MobileWorkRecurrencePreview, MobileWorkTemplate, MobileWorkTemplateDraft } from './mobile-automation.models';
import { MobileAutomationService } from './mobile-automation.service';

@Component({
  selector: 'zumbo-mobile-automation',
  imports: [CommonModule, IonBackButton, IonButtons, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar],
  providers: [MobileAutomationService],
  templateUrl: './mobile-automation.page.html',
  styleUrls: ['./mobile-automation.page.scss', './mobile-automation-detail.scss', './mobile-automation-responsive.scss']
})
export class MobileAutomationPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(MobileAutomationService);
  private readonly session = inject(ZumboSessionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly projectId = signal('');
  protected readonly context = signal<MobileAutomationContext | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly detailLoading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly tab = signal<MobileAutomationTab>('rules');
  protected readonly selectedRuleId = signal('');
  protected readonly selectedRunId = signal('');
  protected readonly selectedRecurrenceId = signal('');
  protected readonly ruleDraft = signal<MobileAutomationRuleDraft>(newMobileAutomationRuleDraft());
  protected readonly templateDraft = signal<MobileWorkTemplateDraft | null>(null);
  protected readonly recurrenceDraft = signal<MobileWorkRecurrenceDraft>(newMobileWorkRecurrenceDraft());
  protected readonly dryRunInput = signal<MobileAutomationDryRunInput>({ sourceId: '', status: '', previousStatus: '', priority: '', type: '', assigneeUserId: '', labels: '' });
  protected readonly dryRun = signal<MobileAutomationDryRun | null>(null);
  protected readonly recurrencePreview = signal<MobileWorkRecurrencePreview | null>(null);
  protected readonly occurrences = signal<readonly MobileWorkRecurrenceOccurrence[]>([]);
  protected readonly audit = signal<readonly MobileAutomationAudit[]>([]);
  protected readonly auditTarget = signal('');
  protected readonly runStatus = signal('');

  protected readonly limits = mobileAutomationLimits;
  protected readonly eventTypes = mobileAutomationEventTypes;
  protected readonly conditionFields = mobileAutomationConditionFields;
  protected readonly conditionOperators = mobileAutomationConditionOperators;
  protected readonly actionTypes = mobileAutomationActionTypes;
  protected readonly project = computed(() => this.context()?.project ?? null);
  protected readonly rules = computed(() => this.context()?.rules ?? []);
  protected readonly runs = computed(() => this.context()?.runs ?? []);
  protected readonly templates = computed(() => this.context()?.templates ?? []);
  protected readonly activeTemplates = computed(() => this.templates().filter(item => !item.archived));
  protected readonly recurrences = computed(() => this.context()?.recurrences ?? []);
  protected readonly activeRules = computed(() => this.rules().filter(item => !item.archived));
  protected readonly activeRecurrences = computed(() => this.recurrences().filter(item => item.active && !item.archived));
  protected readonly boards = computed(() => this.context()?.boards.filter(item => !item.archived) ?? []);
  protected readonly issueTypes = computed(() => (this.context()?.schema.issueTypes ?? []).filter(item => item.active).sort((left, right) => left.position - right.position));
  protected readonly selectedRun = computed(() => this.runs().find(item => item.id === this.selectedRunId()) ?? null);
  protected readonly selectedRecurrence = computed(() => this.recurrences().find(item => item.id === this.selectedRecurrenceId()) ?? null);
  protected readonly roleName = computed(() => this.project()?.members?.find(member => member.userId === this.session.currentUser()?.id)?.role ?? null);
  protected readonly canManageRules = computed(() => hasMobileAutomationPermission(this.roleName(), this.context()?.roles ?? [], 'WorkflowManage'));
  protected readonly canCreateWork = computed(() => hasMobileAutomationPermission(this.roleName(), this.context()?.roles ?? [], 'WorkItemCreate'));
  protected readonly canUpdateWork = computed(() => hasMobileAutomationPermission(this.roleName(), this.context()?.roles ?? [], 'WorkItemUpdate'));
  protected readonly mutationLocked = computed(() => this.busy() || this.connectivity.offline());
  protected readonly canSaveRule = computed(() => this.canManageRules() && validMobileAutomationRule(this.ruleDraft()) && !this.mutationLocked());
  protected readonly canSaveTemplate = computed(() => { const draft = this.templateDraft(); const labels = mobileAutomationLabels(draft?.labelsText ?? ''); return !!draft && !!draft.boardId && !!draft.name.trim() && !!draft.title.trim() && !labels.tooMany && !labels.tooLong && !this.mutationLocked() && (draft.id ? this.canUpdateWork() : this.canCreateWork()); });
  protected readonly canCreateRecurrence = computed(() => this.canCreateWork() && validMobileWorkRecurrence(this.recurrenceDraft()) && !this.mutationLocked());
  protected readonly timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Yerel saat';

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const projectId = params.get('projectId') ?? '';
      if (projectId === this.projectId()) return;
      this.projectId.set(projectId);
      this.context.set(null);
      this.resetSelections();
      this.load();
    });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => this.tab.set(this.asTab(params.get('tab'))));
  }

  protected refresh(event: Event): void {
    const refresher = event.target as HTMLIonRefresherElement;
    if (this.connectivity.offline()) { this.notice.set('Çevrimdışıyken otomasyon kayıtları yenilenemez.'); void refresher.complete(); return; }
    this.load(() => void refresher.complete());
  }
  protected setTab(tab: MobileAutomationTab): void { this.tab.set(tab); this.clearFeedback(); this.audit.set([]); this.auditTarget.set(''); void this.router.navigate([], { relativeTo: this.route, queryParams: { tab }, queryParamsHandling: 'merge', replaceUrl: true }); const context = this.context(); if (!context) return; if (tab === 'runs') this.loadRuns(); else if (tab === 'rules') { const rule = context.rules.find(item => item.id === this.selectedRuleId()) ?? context.rules.find(item => !item.archived) ?? context.rules[0]; if (rule) this.selectRule(rule); } else if (tab === 'recurrences') { const recurrence = context.recurrences.find(item => item.id === this.selectedRecurrenceId()) ?? context.recurrences[0]; if (recurrence) this.selectRecurrence(recurrence); } }
  protected dismissError(): void { this.error.set(null); }
  protected dismissNotice(): void { this.notice.set(null); }
  protected newRule(): void { if (!this.canManageRules() || this.mutationLocked()) return; this.selectedRuleId.set(''); this.ruleDraft.set(newMobileAutomationRuleDraft()); this.dryRun.set(null); this.audit.set([]); this.auditTarget.set(''); }
  protected selectRule(rule: MobileAutomationRuleSummary): void {
    this.selectedRuleId.set(rule.id);
    this.dryRun.set(null);
    if (this.connectivity.offline()) { this.notice.set('Çevrimdışıyken kural ayrıntısı yüklenemez.'); return; }
    this.detailLoading.set(true);
    this.service.rule(rule.id, rule.hasDraft).pipe(finalize(() => this.detailLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { this.ruleDraft.set(mobileAutomationRuleDraft(value)); this.loadAudit('AutomationRule', rule.id, rule.name); }, error: value => this.error.set(mobileAutomationError(normalizeApiError(value), 'Kural ayrıntısı yüklenemedi.')) });
  }
  protected updateRule(field: keyof MobileAutomationRuleDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement; const value = target.type === 'number' ? Number(target.value) : target.value; this.ruleDraft.update(draft => ({ ...draft, [field]: value })); }
  protected updateCondition(index: number, field: keyof MobileAutomationConditionDraft, event: Event): void { const value = (event.target as HTMLInputElement | HTMLSelectElement).value; this.ruleDraft.update(draft => ({ ...draft, conditions: draft.conditions.map((item, itemIndex) => itemIndex === index ? { ...item, [field]: value } : item) })); }
  protected updateAction(index: number, field: keyof MobileAutomationActionDraft, event: Event): void { const value = (event.target as HTMLInputElement | HTMLSelectElement).value; this.ruleDraft.update(draft => ({ ...draft, actions: draft.actions.map((item, itemIndex) => itemIndex === index ? { ...item, [field]: value } : item) })); }
  protected addCondition(): void { if (this.ruleDraft().conditions.length >= this.limits.ruleConditions) return; this.ruleDraft.update(draft => ({ ...draft, conditions: [...draft.conditions, { field: 'Status', operator: 'Equals', value: '' }] })); }
  protected removeCondition(index: number): void { this.ruleDraft.update(draft => ({ ...draft, conditions: draft.conditions.filter((_, itemIndex) => itemIndex !== index) })); }
  protected addAction(): void { if (this.ruleDraft().actions.length >= this.limits.ruleActions) return; this.ruleDraft.update(draft => ({ ...draft, actions: [...draft.actions, { type: 'AddLabel', value: '' }] })); }
  protected removeAction(index: number): void { if (this.ruleDraft().actions.length <= 1) return; this.ruleDraft.update(draft => ({ ...draft, actions: draft.actions.filter((_, itemIndex) => itemIndex !== index) })); }
  protected saveRule(): void { const draft = this.ruleDraft(); if (!this.canSaveRule()) return; this.mutate(this.service.saveRule(this.projectId(), draft), draft.id ? 'Kural taslağı güncellendi.' : 'Kural taslağı oluşturuldu.', value => { this.ruleDraft.set(mobileAutomationRuleDraft(value)); this.selectedRuleId.set(value.id); this.load(); }); }
  protected publishRule(): void { const id = this.ruleDraft().id; if (!id || !this.canManageRules() || this.mutationLocked()) return; this.mutate(this.service.publishRule(id), 'Kural yayınlandı.', () => this.load()); }
  protected setRuleState(rule: MobileAutomationRuleSummary, active: boolean): void { if (!this.canManageRules() || this.mutationLocked() || rule.archived || rule.active === active) return; this.mutate(this.service.setRuleState(rule.id, active, rule.version), active ? 'Kural etkinleştirildi.' : 'Kural duraklatıldı.', () => this.load()); }
  protected archiveRule(rule: MobileAutomationRuleSummary): void { if (!this.canManageRules() || this.mutationLocked() || !window.confirm(`“${rule.name}” kuralı arşivlensin mi?`)) return; this.mutate(this.service.archiveRule(rule.id, rule.version), 'Kural arşivlendi.', () => { this.selectedRuleId.set(''); this.ruleDraft.set(newMobileAutomationRuleDraft()); this.dryRun.set(null); this.audit.set([]); this.auditTarget.set(''); this.load(); }); }
  protected updateDryRun(field: keyof MobileAutomationDryRunInput, event: Event): void { this.dryRunInput.update(value => ({ ...value, [field]: (event.target as HTMLInputElement).value })); }
  protected runDryRun(): void { const draft = this.ruleDraft(); if (!draft.id || !this.canManageRules() || this.mutationLocked()) return; this.mutate(this.service.dryRun(draft.id, this.dryRunInput(), draft.triggerType, draft.eventType), 'Kural önizlemesi tamamlandı.', value => this.dryRun.set(value)); }
  protected chooseRunStatus(event: Event): void { this.runStatus.set((event.target as HTMLSelectElement).value); this.loadRuns(); }
  protected selectRun(run: MobileAutomationRun): void { this.selectedRunId.set(run.id); }
  protected replayRun(run: MobileAutomationRun): void { if (!this.canManageRules() || this.mutationLocked() || run.status !== 'DeadLetter') return; this.mutate(this.service.replayRun(run.id, run.version), 'Çalıştırma yeniden sıraya alındı.', () => this.loadRuns()); }
  protected newTemplate(): void { if (!this.canCreateWork() || this.mutationLocked()) return; const type = this.issueTypes().find(item => item.key === 'Task')?.key ?? this.issueTypes()[0]?.key ?? ''; this.templateDraft.set(newMobileWorkTemplateDraft(this.boards()[0]?.id ?? '', type)); this.audit.set([]); this.auditTarget.set(''); }
  protected editTemplate(value: MobileWorkTemplate): void { this.templateDraft.set(mobileWorkTemplateDraft(value)); this.loadAudit('WorkItemTemplate', value.id, value.name); }
  protected cancelTemplate(): void { this.templateDraft.set(null); }
  protected updateTemplate(field: keyof MobileWorkTemplateDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement; const value = target.type === 'number' ? (target.value ? Number(target.value) : null) : target.value; this.templateDraft.update(draft => draft ? { ...draft, [field]: value } : draft); }
  protected saveTemplate(): void { const draft = this.templateDraft(); if (!draft || !this.canSaveTemplate()) return; this.mutate(this.service.saveTemplate(this.projectId(), draft), draft.id ? 'İş şablonu güncellendi.' : 'İş şablonu oluşturuldu.', () => { this.templateDraft.set(null); this.load(); }); }
  protected archiveTemplate(value: MobileWorkTemplate): void { if (!this.canUpdateWork() || this.mutationLocked() || !window.confirm(`“${value.name}” şablonu arşivlensin mi?`)) return; this.mutate(this.service.archiveTemplate(value.id, value.version), 'İş şablonu arşivlendi.', () => { this.templateDraft.set(null); this.load(); }); }
  protected useTemplate(value: MobileWorkTemplate): void { this.recurrenceDraft.set(newMobileWorkRecurrenceDraft(value.id)); this.recurrencePreview.set(null); this.setTab('recurrences'); }
  protected updateRecurrence(field: keyof MobileWorkRecurrenceDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLSelectElement; const value = target.type === 'number' ? Number(target.value) : target.value; this.recurrenceDraft.update(draft => ({ ...draft, [field]: value })); }
  protected previewRecurrence(): void { const draft = this.recurrenceDraft(); if (!this.canCreateWork() || this.mutationLocked() || !validMobileWorkRecurrence(draft)) return; this.mutate(this.service.previewRecurrence(this.projectId(), draft), 'Takvim önizlemesi hazır.', value => this.recurrencePreview.set(value)); }
  protected createRecurrence(): void { const draft = this.recurrenceDraft(); if (!this.canCreateRecurrence()) return; this.mutate(this.service.createRecurrence(this.projectId(), draft), 'Yineleme etkinleştirildi.', () => { this.recurrencePreview.set(null); this.load(); }); }
  protected setRecurrenceState(value: MobileWorkRecurrence, active: boolean): void { if (!this.canUpdateWork() || this.mutationLocked() || value.archived || value.active === active) return; this.mutate(this.service.setRecurrenceState(value.id, active, value.version), active ? 'Yineleme etkinleştirildi.' : 'Yineleme duraklatıldı.', () => this.load()); }
  protected archiveRecurrence(value: MobileWorkRecurrence): void { if (!this.canUpdateWork() || this.mutationLocked() || !window.confirm('Yineleme arşivlensin mi?')) return; this.mutate(this.service.archiveRecurrence(value.id, value.version), 'Yineleme arşivlendi.', () => this.load()); }
  protected selectRecurrence(value: MobileWorkRecurrence): void { this.selectedRecurrenceId.set(value.id); this.occurrences.set([]); if (this.connectivity.offline()) { this.notice.set('Çevrimdışıyken yineleme geçmişi yüklenemez.'); return; } this.detailLoading.set(true); this.service.occurrences(value.id).pipe(finalize(() => this.detailLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: page => this.occurrences.set(page.items), error: error => this.error.set(mobileAutomationError(normalizeApiError(error), 'Yineleme geçmişi yüklenemedi.')) }); this.loadAudit('WorkItemRecurrence', value.id, this.templateName(value.templateId)); }
  protected templateName(id: string): string { return this.templates().find(item => item.id === id)?.name ?? 'Arşivlenmiş şablon'; }
  protected userName(id?: string | null): string { if (!id) return 'Atanmamış'; const user = this.context()?.users.find(item => item.id === id); return user?.username || user?.email || 'Bilinmeyen kullanıcı'; }
  protected ruleState = mobileAutomationRuleState;
  protected triggerLabel = mobileAutomationTriggerLabel;
  protected runState = mobileAutomationRunState;
  protected actionLabel = mobileAutomationActionTypeLabel;
  protected auditActionLabel = mobileAutomationAuditActionLabel;
  protected actionNeedsValue = mobileAutomationActionNeedsValue;
  protected conditionFieldLabel = mobileAutomationConditionFieldLabel;
  protected conditionOperatorLabel = mobileAutomationConditionOperatorLabel;
  protected conditionNeedsValue = mobileAutomationConditionNeedsValue;
  protected recurrenceState = mobileWorkRecurrenceState;
  protected recurrenceFrequency = mobileWorkRecurrenceFrequency;
  protected occurrenceState = mobileWorkOccurrenceState;
  protected labels = mobileAutomationLabels;

  private load(done?: () => void): void {
    if (!this.projectId()) { this.loading.set(false); this.error.set('Proje bağlantısı geçersiz.'); done?.(); return; }
    if (this.connectivity.offline()) { this.loading.set(false); this.error.set('Çevrimdışıyken otomasyon kayıtları yüklenemez.'); done?.(); return; }
    this.loading.set(true); this.error.set(null);
    this.service.load(this.projectId()).pipe(finalize(() => { this.loading.set(false); done?.(); }), takeUntilDestroyed(this.destroyRef)).subscribe({ next: context => { this.context.set(context); const rule = context.rules.find(item => item.id === this.selectedRuleId()) ?? context.rules.find(item => !item.archived) ?? context.rules[0]; const recurrence = context.recurrences.find(item => item.id === this.selectedRecurrenceId()) ?? context.recurrences[0]; this.selectedRunId.set(context.runs.some(item => item.id === this.selectedRunId()) ? this.selectedRunId() : (context.runs[0]?.id ?? '')); this.selectedRuleId.set(rule?.id ?? ''); this.selectedRecurrenceId.set(recurrence?.id ?? ''); if (!this.recurrenceDraft().templateId) this.recurrenceDraft.set(newMobileWorkRecurrenceDraft(context.templates.find(item => !item.archived)?.id)); if (this.tab() === 'rules' && rule && !this.ruleDraft().id) this.selectRule(rule); if (this.tab() === 'recurrences' && recurrence && !this.occurrences().length) this.selectRecurrence(recurrence); }, error: value => this.error.set(mobileAutomationError(normalizeApiError(value), 'Otomasyon kayıtları yüklenemedi.')) });
  }
  private loadRuns(): void { if (!this.projectId() || this.connectivity.offline()) return; this.detailLoading.set(true); this.service.runs(this.projectId(), this.runStatus()).pipe(finalize(() => this.detailLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: page => { this.context.update(value => value ? { ...value, runs: page.items } : value); this.selectedRunId.set(page.items.some(item => item.id === this.selectedRunId()) ? this.selectedRunId() : (page.items[0]?.id ?? '')); }, error: error => this.error.set(mobileAutomationError(normalizeApiError(error), 'Çalıştırmalar yüklenemedi.')) }); }
  private loadAudit(type: 'AutomationRule' | 'WorkItemTemplate' | 'WorkItemRecurrence', id: string, label: string): void { if (this.connectivity.offline()) return; this.auditTarget.set(label); const prefix = type === 'AutomationRule' ? 'Automation' : type; this.service.audit(type, id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: values => this.audit.set(values.filter(entry => entry.action.startsWith(prefix)).sort((left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt))), error: error => this.error.set(mobileAutomationError(normalizeApiError(error), 'Etkinlik kaydı yüklenemedi.')) }); }
  private mutate<T>(request: Observable<T>, message: string, complete: (value: T) => void): void { if (this.mutationLocked()) return; this.busy.set(true); this.clearFeedback(); request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { this.notice.set(message); complete(value); }, error: value => { const error = normalizeApiError(value); this.error.set(mobileAutomationError(error, 'Otomasyon işlemi tamamlanamadı.')); if (error.code === 'CONCURRENCY_CONFLICT') this.load(); } }); }
  private resetSelections(): void { this.selectedRuleId.set(''); this.selectedRunId.set(''); this.selectedRecurrenceId.set(''); this.ruleDraft.set(newMobileAutomationRuleDraft()); this.templateDraft.set(null); this.recurrenceDraft.set(newMobileWorkRecurrenceDraft()); this.occurrences.set([]); this.audit.set([]); this.auditTarget.set(''); }
  private clearFeedback(): void { this.error.set(null); this.notice.set(null); }
  private asTab(value: string | null): MobileAutomationTab { return value === 'runs' || value === 'templates' || value === 'recurrences' ? value : 'rules'; }
}
