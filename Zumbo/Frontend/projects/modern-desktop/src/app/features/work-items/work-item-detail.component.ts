import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, OnDestroy, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, finalize, forkJoin, map, switchMap } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import {
  ProjectWorkItemCollection,
  ProjectWorkItemDetail,
  WorkItemActivityPage,
  WorkItemApproval,
  WorkItemAttachment,
  WorkItemChecklistEntry,
  WorkItemCollaboration,
  WorkItemComment,
  WorkItemCustomFieldDefinition,
  WorkItemCustomFieldValue,
  WorkItemDevelopmentLink,
  WorkItemRelation,
  WorkItemStatusEntry,
  WorkItemWorkLog
} from './project-work-item.models';
import { WorkItemDetailExtensions, WorkItemDetailService, WorkItemDetailStream } from './work-item-detail.service';
import { ProjectWorkItemService } from './project-work-item.service';

interface DetailDraft { readonly title: string; readonly description: string; readonly priority: string; readonly dueDate: string; }
interface DevelopmentDraft { readonly mappingId: string; readonly kind: string; readonly externalId: string; readonly title: string; readonly url: string; readonly branch: string; readonly commitSha: string; readonly status: string; }
type CustomFieldDraftValue = string | number | boolean | null;
type ActivityTab = 'activity' | 'comments' | 'timeline' | 'worklogs';

@Component({
  selector: 'zumbo-work-item-detail',
  imports: [ZumboIconComponent],
  templateUrl: './work-item-detail.component.html',
  styleUrls: ['./work-item-detail.component.scss', './work-item-detail-properties.scss', './work-item-detail-extensions.scss', './work-item-detail-resources.scss', './work-item-detail-advanced.scss', './work-item-detail-responsive.scss']
})
export class WorkItemDetailComponent implements OnDestroy {
  readonly project = input.required<ProjectSummary>();
  readonly userId = input.required<string>();
  readonly taskId = input.required<string>();
  readonly closed = output<void>();
  readonly archived = output<void>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly detailService = inject(WorkItemDetailService);
  private readonly workItems = inject(ProjectWorkItemService);
  private readonly previousBodyOverflow = this.document.body.style.overflow;
  private loadedTaskId = '';
  protected readonly detail = signal<ProjectWorkItemDetail | null>(null);
  protected readonly collection = signal<ProjectWorkItemCollection | null>(null);
  protected readonly extensions = signal<WorkItemDetailExtensions | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly draft = signal<DetailDraft>(emptyDraft());
  protected readonly comment = signal('');
  protected readonly editingCommentId = signal<string | null>(null);
  protected readonly commentDraft = signal('');
  protected readonly checklist = signal('');
  protected readonly activityTab = signal<ActivityTab>('activity');
  protected readonly labelText = signal('');
  protected readonly workLogHours = signal<number | null>(null);
  protected readonly workLogNote = signal('');
  protected readonly relationTarget = signal('');
  protected readonly relationType = signal('RelatesTo');
  protected readonly nextStatus = signal('');
  protected readonly attachmentFile = signal<File | null>(null);
  protected readonly approvalNote = signal('');
  protected readonly developmentEditorOpen = signal(false);
  protected readonly developmentDraft = signal<DevelopmentDraft>(emptyDevelopmentDraft());
  protected readonly planningSprintId = signal('');
  protected readonly planningEstimate = signal<string>('');
  protected readonly customFieldDraft = signal<Readonly<Record<string, CustomFieldDraftValue>>>({});
  protected readonly canEdit = computed(() => this.hasPermission('WorkItemUpdate'));
  protected readonly canArchive = computed(() => this.hasPermission('WorkItemDelete'));
  protected readonly canComment = computed(() => this.hasPermission('CommentCreate'));
  protected readonly canLogWork = computed(() => this.hasPermission('WorkLogCreate'));
  protected readonly canLink = computed(() => this.hasPermission('WorkItemLink'));
  protected readonly canMove = computed(() => this.hasPermission('WorkItemMove'));
  protected readonly canUploadAttachment = computed(() => this.hasPermission('AttachmentCreate'));
  protected readonly canDeleteAttachment = computed(() => this.hasPermission('AttachmentDelete'));
  protected readonly canApprove = computed(() => this.hasPermission('WorkItemApprove'));
  protected readonly availableTransitions = computed(() => {
    const status = this.detail()?.status;
    return (this.extensions()?.workflow?.transitions ?? []).filter(item => item.fromStatus === status);
  });
  protected readonly selectedTransition = computed(() => this.availableTransitions().find(item => item.toStatus === this.nextStatus()) ?? null);
  protected readonly customFieldDefinitions = computed(() => {
    const schema = this.extensions()?.schema;
    const type = this.detail()?.type;
    if (!schema || !type) return [];
    const definitions = schema.customFields ?? [];
    const layout = schema.layouts?.find(item => item.issueTypeKey.toLowerCase() === type.toLowerCase());
    if (layout) return layout.fieldKeys.map(key => definitions.find(field => field.key === key)).filter((field): field is WorkItemCustomFieldDefinition => !!field);
    return definitions.filter(field => !field.appliesToIssueTypes?.length || field.appliesToIssueTypes.some(value => value.toLowerCase() === type.toLowerCase())).sort((a, b) => a.position - b.position);
  });
  protected readonly planningValid = computed(() => {
    const raw = this.planningEstimate().trim();
    if (!raw) return true;
    const value = Number(raw);
    return Number.isFinite(value) && value >= 0 && value <= 1000;
  });
  protected readonly planningChanged = computed(() => {
    const task = this.detail();
    if (!task) return false;
    const estimate = this.planningEstimate().trim() ? Number(this.planningEstimate()) : null;
    return this.planningSprintId() !== (task.sprintId ?? '') || estimate !== (task.estimatePoints ?? null);
  });
  protected readonly customFieldsValid = computed(() => this.customFieldDefinitions().every(field => {
    const value = this.customFieldDraft()[field.key];
    if (field.required && isEmptyCustomFieldValue(value)) return false;
    if (isEmptyCustomFieldValue(value)) return true;
    if (field.type === 'Text') return String(value).length <= (field.maxLength ?? Number.MAX_SAFE_INTEGER);
    if (field.type === 'Number') { const number = Number(value); return Number.isFinite(number) && number >= (field.minimum ?? -Infinity) && number <= (field.maximum ?? Infinity); }
    if (field.type === 'Select') return !field.options?.length || field.options.includes(String(value));
    return true;
  }));
  protected readonly validDevelopmentDraft = computed(() => {
    const draft = this.developmentDraft();
    const mapping = this.extensions()?.developmentMappings.find(item => item.id === draft.mappingId && item.isActive);
    if (!mapping || !draft.externalId.trim() || !draft.title.trim() || !draft.url.trim()) return false;
    try { return new URL(draft.url).protocol === 'https:' && new URL(draft.url).hostname.toLowerCase() === new URL(mapping.repositoryUrl).hostname.toLowerCase(); }
    catch { return false; }
  });
  protected readonly relationCandidates = computed(() => {
    const task = this.detail();
    const related = new Set(task?.relations.map(item => item.relatedWorkItemId) ?? []);
    return (this.collection()?.tasks ?? []).filter(item => item.id !== task?.id && !related.has(item.id));
  });

  constructor() {
    this.document.body.style.overflow = 'hidden';
    effect(() => {
      const id = this.taskId();
      if (!id || id === this.loadedTaskId) return;
      this.loadedTaskId = id;
      this.load();
    });
  }

  ngOnDestroy(): void { this.document.body.style.overflow = this.previousBodyOverflow; }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      detail: this.workItems.get(this.taskId()),
      collection: this.workItems.load(this.project().id)
    })
      .pipe(
        switchMap(value => this.detailService
          .load(this.taskId(), this.project().id, this.hasPermissionInCollection(value.collection, 'WorkItemLink'))
          .pipe(map(extensions => ({ ...value, extensions })))),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: value => {
          if (value.detail.projectId !== this.project().id) { this.error.set('İş öğesi bu projeye ait değil.'); return; }
          this.collection.set(value.collection);
          this.accept(value.detail);
          this.extensions.set(this.withDetailFallbacks(value.extensions, value.detail));
          this.nextStatus.set(value.extensions.workflow?.transitions.find(item => item.fromStatus === value.detail.status)?.toStatus ?? '');
        },
        error: () => this.error.set('İş öğesi ayrıntıları yüklenemedi.')
      });
  }

  protected updateDraft(field: keyof DetailDraft, event: Event): void {
    this.draft.update(value => ({ ...value, [field]: (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value }));
    this.notice.set(null);
  }

  protected save(): void {
    const task = this.detail();
    const draft = this.draft();
    if (!task || !this.canEdit() || this.saving() || !draft.title.trim()) return;
    this.saving.set(true);
    this.workItems.update(task, {
      title: draft.title.trim(), description: draft.description, priority: draft.priority,
      dueDate: draft.dueDate ? `${draft.dueDate}T00:00:00Z` : null
    }).pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => { this.accept({ ...task, ...updated }); this.notice.set('İş öğesi kaydedildi.'); },
      error: () => { this.notice.set('Kayıt değişti veya doğrulanamadı; güncel ayrıntılar yükleniyor.'); this.load(); }
    });
  }

  protected addComment(): void {
    const task = this.detail();
    const body = this.comment().trim();
    if (!task || !this.canComment() || this.saving() || !body) return;
    this.mutate(this.workItems.addComment(task.id, body), 'Yorum eklendi.', () => this.comment.set(''), ['comments', 'activity']);
  }

  protected canManageComment(comment: WorkItemComment): boolean {
    return this.canComment() && comment.authorUserId === this.userId();
  }

  protected beginCommentEdit(comment: WorkItemComment): void {
    if (!this.canManageComment(comment) || this.saving()) return;
    this.editingCommentId.set(comment.id);
    this.commentDraft.set(comment.body);
  }

  protected cancelCommentEdit(): void {
    this.editingCommentId.set(null);
    this.commentDraft.set('');
  }

  protected saveComment(comment: WorkItemComment): void {
    const task = this.detail(); const body = this.commentDraft().trim();
    if (!task || !this.canManageComment(comment) || !body || body === comment.body || this.saving()) return;
    this.mutate(this.detailService.editComment(task.id, comment.id, body), 'Yorum güncellendi.', () => this.cancelCommentEdit(), ['comments', 'activity']);
  }

  protected deleteComment(comment: WorkItemComment): void {
    const task = this.detail();
    if (!task || !this.canManageComment(comment) || this.saving() || !confirm('Yorum silinsin mi?')) return;
    this.mutate(this.detailService.deleteComment(task.id, comment.id), 'Yorum silindi.', () => this.cancelCommentEdit(), ['comments', 'activity']);
  }

  protected addChecklist(): void {
    const task = this.detail();
    const text = this.checklist().trim();
    if (!task || !this.canEdit() || this.saving() || !text) return;
    this.mutate(this.workItems.addChecklist(task.id, text), 'Kontrol maddesi eklendi.', () => this.checklist.set(''));
  }

  protected toggleChecklist(entry: WorkItemChecklistEntry): void {
    const task = this.detail();
    if (!task || !this.canEdit() || this.saving()) return;
    this.mutate(this.workItems.setChecklist(task.id, entry.id, !entry.completed), 'Kontrol listesi güncellendi.');
  }

  protected toggleCollaboration(kind: 'watch' | 'vote'): void {
    const current = this.extensions()?.collaboration;
    if (!current || this.saving()) return;
    const watching = kind === 'watch' ? !current.watching : current.watching;
    const voted = kind === 'vote' ? !current.voted : current.voted;
    const optimistic: WorkItemCollaboration = {
      ...current,
      watching,
      voted,
      watcherCount: Math.max(0, current.watcherCount + (kind === 'watch' ? (watching ? 1 : -1) : 0)),
      voteCount: Math.max(0, current.voteCount + (kind === 'vote' ? (voted ? 1 : -1) : 0))
    };
    this.patchExtensions({ collaboration: optimistic });
    this.saving.set(true);
    const request = kind === 'watch'
      ? this.detailService.setWatching(this.taskId(), watching)
      : this.detailService.setVoted(this.taskId(), voted);
    request.pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: collaboration => { this.patchExtensions({ collaboration }); this.notice.set(kind === 'watch' ? (watching ? 'İş öğesi takip ediliyor.' : 'Takip bırakıldı.') : (voted ? 'Oy eklendi.' : 'Oy kaldırıldı.')); this.reloadStreams(['activity']); },
      error: () => { this.patchExtensions({ collaboration: current }); this.notice.set('İşbirliği tercihi kaydedilemedi.') ; }
    });
  }

  protected addLabel(): void {
    const task = this.detail(); const label = this.labelText().trim();
    if (!task || !this.canEdit() || !label || this.saving()) return;
    this.mutate(this.detailService.addLabel(task.id, label), 'Etiket eklendi.', () => this.labelText.set(''), ['activity']);
  }

  protected removeLabel(label: string): void {
    const task = this.detail();
    if (!task || !this.canEdit() || this.saving()) return;
    this.mutate(this.detailService.removeLabel(task.id, label), 'Etiket kaldırıldı.', undefined, ['activity']);
  }

  protected addWorkLog(): void {
    const task = this.detail(); const hours = Number(this.workLogHours());
    if (!task || !this.canLogWork() || this.saving() || !Number.isFinite(hours) || hours < .25 || hours > 24) return;
    this.mutate(this.detailService.addWorkLog(task.id, this.userId(), hours, this.workLogNote().trim() || null), 'Çalışma kaydı eklendi.', () => { this.workLogHours.set(null); this.workLogNote.set(''); }, ['worklogs', 'activity']);
  }

  protected linkRelation(): void {
    const task = this.detail(); const target = this.relationTarget();
    if (!task || !this.canLink() || !target || this.saving()) return;
    this.mutate(this.detailService.link(task.id, target, this.relationType()), 'İş ilişkisi eklendi.', () => this.relationTarget.set(''), ['activity']);
  }

  protected unlinkRelation(relation: WorkItemRelation): void {
    const task = this.detail();
    if (!task || !this.canLink() || this.saving()) return;
    this.mutate(this.detailService.unlink(task.id, relation.relatedWorkItemId, relation.relationType), 'İş ilişkisi kaldırıldı.', undefined, ['activity']);
  }

  protected moveStatus(): void {
    const task = this.detail(); const status = this.nextStatus();
    if (!task || !this.canMove() || !this.selectedTransition() || this.selectedTransition()?.requiresApproval || this.saving()) return;
    this.mutate(this.detailService.move(task.id, status), 'Durum güncellendi.', undefined, ['timeline', 'activity']);
  }

  protected selectAttachment(event: Event): void {
    this.attachmentFile.set((event.target as HTMLInputElement).files?.item(0) ?? null);
  }

  protected uploadAttachment(): void {
    const task = this.detail(); const file = this.attachmentFile();
    if (!task || !file || !this.canUploadAttachment() || this.saving()) return;
    this.mutate(this.detailService.uploadAttachment(task.id, file), 'Dosya yüklendi ve güvenlik kontrolüne alındı.', () => this.attachmentFile.set(null), ['attachments', 'activity']);
  }

  protected deleteAttachment(attachment: WorkItemAttachment): void {
    const task = this.detail();
    if (!task || !this.canDeleteAttachment() || this.saving() || !confirm(`“${attachment.fileName}” silinsin mi?`)) return;
    this.mutate(this.detailService.deleteAttachment(task.id, attachment.id), 'Dosya silindi.', undefined, ['attachments', 'activity']);
  }

  protected downloadAttachment(attachment: WorkItemAttachment): void {
    const task = this.detail(); const browser = this.document.defaultView;
    if (!task || !browser || this.saving()) return;
    this.saving.set(true);
    this.detailService.downloadAttachment(task.id, attachment.id)
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => {
          const url = browser.URL.createObjectURL(blob);
          const anchor = this.document.createElement('a'); anchor.href = url; anchor.download = attachment.fileName; anchor.click();
          browser.setTimeout(() => browser.URL.revokeObjectURL(url), 0);
        },
        error: () => this.notice.set('Dosya indirilemedi.')
      });
  }

  protected requestApproval(): void {
    const task = this.detail(); const transition = this.selectedTransition();
    if (!task || !transition?.requiresApproval || !this.canApprove() || this.saving()) return;
    this.mutate(this.detailService.requestApproval(task.id, transition.toStatus), 'Geçiş onayı istendi.', undefined, ['approvals', 'activity']);
  }

  protected decideApproval(approval: WorkItemApproval, approved: boolean): void {
    const task = this.detail();
    if (!task || !this.canApprove() || approval.status !== 'Pending' || approval.requestedByUserId === this.userId() || this.saving()) return;
    this.mutate(this.detailService.decideApproval(task.id, approval.id, approved, this.approvalNote().trim() || null), approved ? 'Geçiş onaylandı.' : 'Geçiş reddedildi.', () => this.approvalNote.set(''), ['approvals', 'timeline', 'activity']);
  }

  protected updateDevelopmentDraft(field: keyof DevelopmentDraft, event: Event): void {
    this.developmentDraft.update(value => ({ ...value, [field]: (event.target as HTMLInputElement | HTMLSelectElement).value }));
  }

  protected openDevelopmentEditor(): void {
    const mapping = this.extensions()?.developmentMappings.find(item => item.isActive);
    this.developmentDraft.set({ ...emptyDevelopmentDraft(), mappingId: mapping?.id ?? '' });
    this.developmentEditorOpen.set(true);
  }

  protected createDevelopmentLink(): void {
    const task = this.detail(); const draft = this.developmentDraft();
    if (!task || !this.canLink() || !this.validDevelopmentDraft() || this.saving()) return;
    this.saving.set(true);
    this.detailService.createDevelopmentLink(task.id, { ...draft, branch: draft.branch.trim() || null, commitSha: draft.commitSha.trim() || null })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: link => { this.patchExtensions({ developmentLinks: [link, ...(this.extensions()?.developmentLinks ?? [])] }); this.developmentEditorOpen.set(false); this.notice.set('Geliştirme bağlantısı eklendi.'); this.reloadStreams(['activity']); },
        error: () => this.notice.set('Geliştirme bağlantısı eklenemedi.')
      });
  }

  protected deleteDevelopmentLink(link: WorkItemDevelopmentLink): void {
    const task = this.detail();
    if (!task || !this.canLink() || link.source !== 'Manual' || this.saving() || !confirm(`“${link.title}” bağlantısı kaldırılsın mı?`)) return;
    this.saving.set(true);
    this.detailService.deleteDevelopmentLink(task.id, link.id, link.version)
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => { this.patchExtensions({ developmentLinks: (this.extensions()?.developmentLinks ?? []).filter(item => item.id !== link.id) }); this.notice.set('Geliştirme bağlantısı kaldırıldı.'); this.reloadStreams(['activity']); },
        error: () => this.notice.set('Geliştirme bağlantısı kaldırılamadı.')
      });
  }

  protected updatePlanning(field: 'sprintId' | 'estimatePoints', event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    if (field === 'sprintId') this.planningSprintId.set(value);
    else this.planningEstimate.set(value);
  }

  protected savePlanning(): void {
    const task = this.detail();
    if (!task || !this.canEdit() || !this.planningValid() || !this.planningChanged() || this.saving()) return;
    const raw = this.planningEstimate().trim();
    this.mutate(this.detailService.setPlanning(task.id, this.planningSprintId() || null, raw ? Number(raw) : null), 'Planlama güncellendi.', undefined, ['activity']);
  }

  protected updateCustomField(field: WorkItemCustomFieldDefinition, event: Event): void {
    const raw = (event.target as HTMLInputElement | HTMLSelectElement).value;
    let value: CustomFieldDraftValue = raw;
    if (field.type === 'Number') value = raw === '' ? null : Number(raw);
    if (field.type === 'Boolean') value = raw === '' ? null : raw === 'true';
    this.customFieldDraft.update(current => ({ ...current, [field.key]: value }));
  }

  protected saveCustomFields(): void {
    const task = this.detail();
    if (!task || !this.canEdit() || !this.customFieldsValid() || this.saving()) return;
    const values = this.customFieldDefinitions()
      .filter(field => !isEmptyCustomFieldValue(this.customFieldDraft()[field.key]))
      .map(field => toCustomFieldRequest(field, this.customFieldDraft()[field.key]));
    this.mutate(this.detailService.setCustomFields(task.id, values), 'Özel alanlar güncellendi.', undefined, ['activity']);
  }

  protected loadMore(stream: WorkItemDetailStream): void {
    const extensions = this.extensions();
    const current = extensions?.[stream] as WorkItemActivityPage<unknown> | undefined;
    if (!extensions || !current || current.items.length >= current.totalCount || this.saving()) return;
    this.saving.set(true);
    this.detailService.loadPage(this.taskId(), stream, current.page + 1)
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: page => this.patchStream(stream, { ...page, items: [...current.items, ...page.items] }),
        error: () => this.notice.set('Daha fazla etkinlik yüklenemedi.')
      });
  }

  protected archive(): void {
    const task = this.detail();
    if (!task || !this.canArchive() || this.saving() || !confirm(`“${task.title}” arşive taşınsın mı?`)) return;
    this.saving.set(true);
    this.workItems.archive(task).pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.archived.emit(),
      error: () => this.notice.set('İş öğesi arşivlenemedi; güncel kayıt korunuyor.')
    });
  }

  protected userName(userId?: string | null): string {
    if (!userId) return 'Atanmamış';
    const user = this.collection()?.users.find(item => item.id === userId);
    return user?.username || user?.email || 'Kullanıcı';
  }
  protected taskName(workItemId: string): string { return this.collection()?.tasks.find(item => item.id === workItemId)?.title ?? 'İş öğesi'; }
  protected relationLabel(value: string): string { return ({ RelatesTo: 'İlişkili', Blocks: 'Engelliyor', BlockedBy: 'Engelleniyor', Duplicates: 'Yinelenen' } as Record<string, string>)[value] ?? value; }
  protected activityLabel(value: string): string { return ACTIVITY_LABELS[value] ?? 'İş öğesi etkinliği'; }
  protected attachmentSecurityLabel(value: string): string { return ({ Clean: 'Güvenli', Pending: 'Taranıyor', Infected: 'Engellendi', Unavailable: 'Tarama bekliyor' } as Record<string, string>)[value] ?? 'Kontrol ediliyor'; }
  protected approvalStatusLabel(value: string): string { return ({ Pending: 'Bekliyor', Approved: 'Onaylandı', Rejected: 'Reddedildi', Expired: 'Süresi doldu', Consumed: 'Uygulandı' } as Record<string, string>)[value] ?? 'Bilinmiyor'; }
  protected developmentKindLabel(value: string): string { return ({ PullRequest: 'Pull request', Commit: 'Commit', Branch: 'Dal', Build: 'Build' } as Record<string, string>)[value] ?? 'Bağlantı'; }
  protected developmentStatusLabel(value: string): string { return ({ Open: 'Açık', Merged: 'Birleştirildi', Closed: 'Kapalı', Success: 'Başarılı', Failed: 'Başarısız', Pending: 'Bekliyor', Running: 'Çalışıyor', Pushed: 'Gönderildi', Unknown: 'Bilinmiyor' } as Record<string, string>)[value] ?? 'Bilinmiyor'; }
  protected sprintStatusLabel(value: string): string { return ({ Planned: 'Planlandı', Active: 'Aktif', Completed: 'Tamamlandı' } as Record<string, string>)[value] ?? value; }
  protected sprintName(sprintId?: string | null): string { return this.extensions()?.sprints.find(item => item.id === sprintId)?.name ?? (sprintId ? 'Sprint' : 'Sprint yok'); }
  protected customFieldDisplay(field: WorkItemCustomFieldDefinition): string {
    const value = this.customFieldDraft()[field.key];
    if (isEmptyCustomFieldValue(value)) return 'Belirtilmedi';
    if (field.type === 'Boolean') return value === true ? 'Evet' : 'Hayır';
    if (field.type === 'Date') return this.formatDate(String(value));
    return String(value);
  }
  protected formatFileSize(value: number): string { return value < 1024 ? `${value} B` : value < 1024 * 1024 ? `${(value / 1024).toFixed(1)} KB` : `${(value / (1024 * 1024)).toFixed(1)} MB`; }
  protected priorityLabel(value: string): string { return ({ Critical: 'Kritik', High: 'Yüksek', Medium: 'Orta', Low: 'Düşük' } as Record<string, string>)[value] ?? value; }
  protected formatDate(value?: string | null, time = false): string {
    if (!value) return 'Tarih yok';
    return new Intl.DateTimeFormat('tr-TR', time ? { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' } : { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(value));
  }

  private mutate(request: Observable<ProjectWorkItemDetail>, message: string, after?: () => void, reloadStreams: readonly WorkItemDetailStream[] = []): void {
    this.saving.set(true);
    request.pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: detail => { this.accept(detail); after?.(); this.notice.set(message); this.reloadStreams(reloadStreams); },
      error: () => this.notice.set('İşlem tamamlanamadı; güncel ayrıntılar korunuyor.')
    });
  }

  private accept(detail: ProjectWorkItemDetail): void {
    this.detail.set(detail);
    this.draft.set({ title: detail.title, description: detail.description || '', priority: detail.priority, dueDate: detail.dueDate?.slice(0, 10) ?? '' });
    this.planningSprintId.set(detail.sprintId ?? '');
    this.planningEstimate.set(detail.estimatePoints == null ? '' : String(detail.estimatePoints));
    this.customFieldDraft.set(Object.fromEntries((detail.customFields ?? []).map(value => [value.fieldKey, customFieldValue(value)])));
    const next = this.extensions()?.workflow?.transitions.find(item => item.fromStatus === detail.status)?.toStatus ?? '';
    this.nextStatus.set(next);
  }

  private reloadStreams(streams: readonly WorkItemDetailStream[]): void {
    if (!streams.length) return;
    forkJoin(streams.map(stream => this.detailService.loadPage(this.taskId(), stream, 1).pipe(map(page => ({ stream, page })))))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: pages => pages.forEach(({ stream, page }) => this.patchStream(stream, page)),
        error: () => this.notice.set('Bazı işbirliği akışları yenilenemedi.')
      });
  }

  private withDetailFallbacks(extensions: WorkItemDetailExtensions, detail: ProjectWorkItemDetail | null): WorkItemDetailExtensions {
    if (!extensions.partial || !detail) return extensions;
    return {
      ...extensions,
      attachments: extensions.attachments.items.length ? extensions.attachments : fallbackPage(detail.attachments ?? []),
      approvals: extensions.approvals.items.length ? extensions.approvals : fallbackPage(detail.approvals ?? []),
      comments: extensions.comments.items.length ? extensions.comments : fallbackPage(detail.comments),
      timeline: extensions.timeline.items.length ? extensions.timeline : fallbackPage(detail.statusHistory),
      worklogs: extensions.worklogs.items.length ? extensions.worklogs : fallbackPage(detail.workLogs)
    };
  }

  private patchExtensions(patch: Partial<WorkItemDetailExtensions>): void {
    this.extensions.update(value => value ? { ...value, ...patch } : value);
  }

  private patchStream(stream: WorkItemDetailStream, page: WorkItemActivityPage<unknown>): void {
    this.extensions.update(value => value ? ({ ...value, [stream]: page } as WorkItemDetailExtensions) : value);
  }

  private hasPermission(permission: string): boolean {
    const collection = this.collection();
    return collection ? this.hasPermissionInCollection(collection, permission) : false;
  }

  private hasPermissionInCollection(collection: ProjectWorkItemCollection, permission: string): boolean {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role = collection.roles.find(item => item.name === membership?.role && item.isActive);
    return !!role?.permissions.some(value => value === '*' || value === permission);
  }
}

function emptyDraft(): DetailDraft { return { title: '', description: '', priority: 'Medium', dueDate: '' }; }
function emptyDevelopmentDraft(): DevelopmentDraft { return { mappingId: '', kind: 'PullRequest', externalId: '', title: '', url: '', branch: '', commitSha: '', status: 'Open' }; }
function fallbackPage<T>(items: readonly T[]): WorkItemActivityPage<T> { return { items, page: 1, pageSize: 50, totalCount: items.length }; }
function isEmptyCustomFieldValue(value: CustomFieldDraftValue | undefined): boolean { return value === undefined || value === null || value === ''; }
function customFieldValue(value: WorkItemCustomFieldValue): CustomFieldDraftValue {
  if (value.type === 'Number') return value.numberValue ?? null;
  if (value.type === 'Boolean') return value.booleanValue ?? null;
  if (value.type === 'Date') return value.dateValue ?? '';
  if (value.type === 'Select') return value.optionKey ?? '';
  return value.textValue ?? '';
}
function toCustomFieldRequest(field: WorkItemCustomFieldDefinition, value: CustomFieldDraftValue | undefined): WorkItemCustomFieldValue {
  if (field.type === 'Number') return { fieldKey: field.key, numberValue: Number(value) };
  if (field.type === 'Boolean') return { fieldKey: field.key, booleanValue: value === true };
  if (field.type === 'Date') return { fieldKey: field.key, dateValue: String(value) };
  if (field.type === 'Select') return { fieldKey: field.key, optionKey: String(value) };
  return { fieldKey: field.key, textValue: String(value) };
}

const ACTIVITY_LABELS: Readonly<Record<string, string>> = {
  WorkItemCreated: 'İş öğesi oluşturuldu', WorkItemUpdated: 'Ayrıntılar güncellendi', WorkItemAssigned: 'Atanan kişi değiştirildi',
  WorkItemMoved: 'Durum değişti', WorkItemReordered: 'Sıra değiştirildi', WorkItemPlanningUpdated: 'Planlama güncellendi',
  WorkItemChecklistItemAdded: 'Kontrol maddesi eklendi', WorkItemChecklistItemUpdated: 'Kontrol maddesi güncellendi',
  WorkItemLabelAdded: 'Etiket eklendi', WorkItemLabelRemoved: 'Etiket kaldırıldı', WorkItemCommentAdded: 'Yorum eklendi',
  WorkItemWorkLogAdded: 'Çalışma kaydı eklendi', WorkItemLinked: 'İş ilişkisi eklendi', WorkItemUnlinked: 'İş ilişkisi kaldırıldı',
  WorkItemWatched: 'Takip başladı', WorkItemUnwatched: 'Takip sona erdi', WorkItemVoted: 'Oy eklendi', WorkItemVoteRemoved: 'Oy kaldırıldı',
  WorkItemAttachmentAdded: 'Dosya eklendi', WorkItemAttachmentDeleted: 'Dosya silindi', WorkItemApprovalRequested: 'Geçiş onayı istendi', WorkItemApprovalDecided: 'Geçiş onayı sonuçlandı'
};
