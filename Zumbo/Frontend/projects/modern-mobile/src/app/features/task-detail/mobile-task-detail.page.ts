import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { IonBackButton, IonButtons, IonContent, IonHeader, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { ZumboSessionService } from '@zumbo/modern-shared';
import { Observable, finalize, firstValueFrom } from 'rxjs';
import { MobileConnectivityService } from '../../shell/mobile-connectivity.service';
import { MobileTaskAttachment, MobileTaskDetail } from '../../shell/mobile-workspace.models';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';
import { MobileTaskDetailContext, MobileTaskDetailTab, MobileTaskDraft, MobileTaskStream } from './mobile-task-detail.models';
import { MobileTaskDetailService } from './mobile-task-detail.service';

@Component({
  selector: 'zumbo-mobile-task-detail',
  imports: [FormsModule, IonBackButton, IonButtons, IonContent, IonHeader, IonTitle, IonToolbar],
  templateUrl: './mobile-task-detail.page.html',
  styleUrls: ['./mobile-task-detail.page.scss', './mobile-task-detail.forms.scss', './mobile-task-detail.streams.scss']
})
export class MobileTaskDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(MobileTaskDetailService);
  private readonly session = inject(ZumboSessionService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly store = inject(MobileWorkspaceStore);
  protected readonly context = signal<MobileTaskDetailContext | null>(null);
  protected readonly task = computed(() => this.context()?.detail ?? null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly tab = signal<MobileTaskDetailTab>('summary');
  protected readonly draft = signal<MobileTaskDraft>({ title: '', description: '', priority: 'Medium', dueDate: '' });
  protected commentBody = '';
  protected checklistText = '';
  protected labelText = '';
  protected workLogHours: number | null = null;
  protected workLogNote = '';
  protected nextStatus = '';
  protected attachmentFile: File | null = null;
  protected readonly project = computed(() => this.store.projects().find(item => item.id === this.task()?.projectId));
  protected readonly permissions = computed(() => {
    const userId = this.session.currentUser()?.id;
    const roleName = this.project()?.members?.find(member => member.userId === userId)?.role;
    return this.context()?.roles.find(role => role.name === roleName && role.isActive)?.permissions ?? [];
  });
  protected readonly canEdit = computed(() => this.hasPermission('WorkItemUpdate'));
  protected readonly canMove = computed(() => this.hasPermission('WorkItemMove'));
  protected readonly canComment = computed(() => this.hasPermission('CommentCreate'));
  protected readonly canLogWork = computed(() => this.hasPermission('WorkLogCreate'));
  protected readonly canUpload = computed(() => this.hasPermission('AttachmentCreate'));
  protected readonly transitions = computed(() => (this.context()?.workflow?.transitions ?? []).filter(item => item.fromStatus === this.task()?.status));
  protected readonly checklistProgress = computed(() => {
    const items = this.task()?.checklist ?? [];
    return { complete: items.filter(item => item.completed).length, total: items.length };
  });
  protected readonly loggedHours = computed(() => (this.task()?.workLogs ?? []).reduce((total, item) => total + item.hours, 0));

  async ngOnInit(): Promise<void> {
    const taskId = this.route.snapshot.paramMap.get('taskId');
    if (!taskId) {
      this.error.set('İş kimliği bulunamadı.');
      this.loading.set(false);
      return;
    }
    try {
      await this.store.load();
      this.accept(await firstValueFrom(this.service.load(taskId)));
    } catch {
      this.error.set('İş ayrıntısı yüklenemedi. Erişiminizi kontrol edip yeniden deneyin.');
    } finally {
      this.loading.set(false);
    }
  }

  protected setTab(tab: MobileTaskDetailTab): void {
    this.tab.set(tab);
  }

  protected saveTask(): void {
    const task = this.task();
    const draft = this.draft();
    if (!task || !this.canEdit() || this.blocked() || !draft.title.trim()) return;
    this.mutateDetail(this.service.update(task, draft), 'İş ayrıntıları güncellendi.');
  }

  protected moveStatus(): void {
    const task = this.task();
    const transition = this.transitions().find(item => item.toStatus === this.nextStatus && !item.requiresApproval);
    if (!task || !transition || !this.canMove() || this.blocked()) return;
    this.mutateDetail(this.service.move(task.id, transition.toStatus), 'Durum güncellendi.', 'activity');
  }

  protected addComment(): void {
    const task = this.task();
    const body = this.commentBody.trim();
    if (!task || !body || !this.canComment() || this.blocked()) return;
    this.mutateDetail(this.service.addComment(task.id, body), 'Yorum eklendi.', 'comments', () => this.commentBody = '');
  }

  protected addChecklist(): void {
    const task = this.task();
    const text = this.checklistText.trim();
    if (!task || !text || !this.canEdit() || this.blocked()) return;
    this.mutateDetail(this.service.addChecklist(task.id, text), 'Kontrol maddesi eklendi.', 'activity', () => this.checklistText = '');
  }

  protected toggleChecklist(entry: { readonly id: string; readonly completed: boolean }): void {
    const task = this.task();
    if (!task || !this.canEdit() || this.blocked()) return;
    this.mutateDetail(this.service.setChecklist(task.id, entry.id, !entry.completed), 'Kontrol maddesi güncellendi.', 'activity');
  }

  protected toggleCollaboration(kind: 'watch' | 'vote'): void {
    const task = this.task();
    const collaboration = this.context()?.collaboration;
    if (!task || !collaboration || this.blocked()) return;
    this.saving.set(true);
    const request = kind === 'watch'
      ? this.service.setWatching(task.id, !collaboration.watching)
      : this.service.setVoted(task.id, !collaboration.voted);
    request.pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: value => {
        this.context.update(context => context ? { ...context, collaboration: value } : context);
        this.notice.set(kind === 'watch' ? 'Takip tercihi güncellendi.' : 'Oy tercihi güncellendi.');
        this.reloadStream('activity');
      },
      error: () => this.error.set('İşbirliği tercihi kaydedilemedi.')
    });
  }

  protected addWorkLog(): void {
    const task = this.task();
    const userId = this.session.currentUser()?.id;
    const hours = Number(this.workLogHours);
    if (!task || !userId || !this.canLogWork() || this.blocked() || !Number.isFinite(hours) || hours < .25 || hours > 24) return;
    this.mutateDetail(this.service.addWorkLog(task.id, userId, hours, this.workLogNote.trim() || null), 'Çalışma kaydı eklendi.', 'worklogs', () => {
      this.workLogHours = null;
      this.workLogNote = '';
    });
  }

  protected selectAttachment(event: Event): void {
    this.attachmentFile = (event.target as HTMLInputElement).files?.item(0) ?? null;
  }

  protected uploadAttachment(): void {
    const task = this.task();
    if (!task || !this.attachmentFile || !this.canUpload() || this.blocked()) return;
    this.mutateDetail(this.service.upload(task.id, this.attachmentFile), 'Dosya güvenlik kontrolüne alındı.', 'attachments', () => this.attachmentFile = null);
  }

  protected downloadAttachment(attachment: MobileTaskAttachment): void {
    const task = this.task();
    if (!task || this.saving()) return;
    this.saving.set(true);
    this.service.download(task.id, attachment.id).pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = attachment.fileName;
        anchor.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
      },
      error: () => this.error.set('Dosya indirilemedi.')
    });
  }

  protected addLabel(): void {
    const task = this.task();
    const label = this.labelText.trim();
    if (!task || !label || !this.canEdit() || this.blocked()) return;
    this.mutateDetail(this.service.addLabel(task.id, label), 'Etiket eklendi.', 'activity', () => this.labelText = '');
  }

  protected removeLabel(label: string): void {
    const task = this.task();
    if (!task || !this.canEdit() || this.blocked()) return;
    this.mutateDetail(this.service.removeLabel(task.id, label), 'Etiket kaldırıldı.', 'activity');
  }

  protected userName(userId: string): string {
    const user = this.context()?.users.find(item => item.id === userId);
    return user?.username || user?.email || 'Ekip üyesi';
  }

  protected activityLabel(type: string): string {
    return ({ WorkItemCreated: 'İş oluşturuldu', WorkItemUpdated: 'Ayrıntılar güncellendi', WorkItemMoved: 'Durum değişti', WorkItemCommentAdded: 'Yorum eklendi', WorkItemChecklistItemAdded: 'Kontrol maddesi eklendi', WorkItemWorkLogAdded: 'Çalışma kaydı eklendi', WorkItemWatched: 'Takip başladı', WorkItemVoted: 'Oy eklendi' } as Record<string, string>)[type] ?? 'İş etkinliği';
  }

  protected fileSize(value: number): string {
    return value < 1024 ? `${value} B` : value < 1024 * 1024 ? `${(value / 1024).toFixed(1)} KB` : `${(value / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected dateTime(value: string): string {
    return new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(value));
  }

  protected date(value?: string | null): string {
    return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'long', year: 'numeric' }).format(new Date(value)) : 'Tarih yok';
  }

  private hasPermission(permission: string): boolean {
    return this.permissions().some(value => value === '*' || value === permission);
  }

  private blocked(): boolean {
    return this.saving() || this.connectivity.offline();
  }

  private accept(context: MobileTaskDetailContext): void {
    this.context.set(context);
    const detail = context.detail;
    this.draft.set({ title: detail.title, description: detail.description ?? '', priority: detail.priority, dueDate: detail.dueDate?.slice(0, 10) ?? '' });
    this.nextStatus = context.workflow?.transitions.find(item => item.fromStatus === detail.status && !item.requiresApproval)?.toStatus ?? '';
  }

  private acceptDetail(detail: MobileTaskDetail): void {
    this.context.update(context => context ? { ...context, detail } : context);
    this.draft.set({ title: detail.title, description: detail.description ?? '', priority: detail.priority, dueDate: detail.dueDate?.slice(0, 10) ?? '' });
    this.nextStatus = this.context()?.workflow?.transitions.find(item => item.fromStatus === detail.status && !item.requiresApproval)?.toStatus ?? '';
  }

  private mutateDetail(request: Observable<MobileTaskDetail>, message: string, stream?: MobileTaskStream, after?: () => void): void {
    this.saving.set(true);
    this.error.set(null);
    request.pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: detail => {
        this.acceptDetail(detail);
        after?.();
        this.notice.set(message);
        if (stream) this.reloadStream(stream);
      },
      error: () => this.error.set('İşlem tamamlanamadı; güncel ayrıntılar korunuyor.')
    });
  }

  private reloadStream(stream: MobileTaskStream): void {
    const task = this.task();
    if (!task) return;
    this.service.loadStream(task.id, stream).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => this.context.update(context => context ? { ...context, [stream]: page } : context),
      error: () => this.error.set('İlgili çalışma akışı yenilenemedi.')
    });
  }
}
