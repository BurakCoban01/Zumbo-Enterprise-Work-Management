import { Component, OnInit, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { InboxMode, NotificationItem, notificationLabel } from '../notifications/notification.models';
import { NotificationService } from '../notifications/notification.service';
import { PersonalWorkItem } from '../personal-work/personal-work.models';
import { PersonalWorkService } from '../personal-work/personal-work.service';

const MODE_KEY = 'zumbo.personal.inboxMode';
const MODES: readonly InboxMode[] = ['unread', 'actions', 'all'];

@Component({
  selector: 'zumbo-inbox-page',
  imports: [RouterLink, ZumboIconComponent],
  providers: [NotificationService, PersonalWorkService],
  templateUrl: './inbox.page.html',
  styleUrl: './inbox.page.scss'
})
export class InboxPage implements OnInit {
  readonly projects = input.required<readonly ProjectSummary[]>();
  readonly userId = input.required<string>();
  readonly unreadChange = output<number>();

  protected readonly notifications = signal<readonly NotificationItem[]>([]);
  protected readonly tasks = signal<readonly PersonalWorkItem[]>([]);
  protected readonly mode = signal<InboxMode>(readMode());
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly workLoading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly workError = signal(false);
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly unreadCount = computed(() => this.notifications().filter(item => !item.read).length);
  protected readonly visibleNotifications = computed(() => {
    if (this.mode() === 'all') return this.notifications();
    if (this.mode() === 'actions') return this.notifications().filter(item => item.category === 'Action');
    return this.notifications().filter(item => !item.read);
  });
  protected readonly pendingApprovals = computed(() => this.tasks().filter(task =>
    (task.approvals ?? []).some(approval => approval.status === 'Pending')
  ));

  constructor(
    private readonly notificationService: NotificationService,
    private readonly personalWork: PersonalWorkService
  ) {}

  ngOnInit(): void {
    this.loadNotifications();
    this.loadApprovals();
  }

  protected loadNotifications(page = 1, append = false): void {
    append ? this.loadingMore.set(true) : this.loading.set(true);
    this.error.set(null);
    this.notificationService.load(page).pipe(finalize(() => {
      this.loading.set(false);
      this.loadingMore.set(false);
    })).subscribe({
      next: result => {
        this.notifications.update(current => append ? mergeNotifications(current, result.items) : result.items);
        this.page.set(result.page);
        this.hasMore.set(result.hasMore);
        this.emitUnread();
      },
      error: () => this.error.set('Bildirimler yüklenemedi.')
    });
  }

  protected setMode(mode: InboxMode): void {
    this.mode.set(mode);
    localStorage.setItem(MODE_KEY, mode);
  }

  protected handleTabKey(event: KeyboardEvent, index: number): void {
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? MODES.length - 1
      : event.key === 'ArrowRight' ? (index + 1) % MODES.length
        : event.key === 'ArrowLeft' ? (index - 1 + MODES.length) % MODES.length : -1;
    if (next < 0) return;
    event.preventDefault();
    this.setMode(MODES[next]);
    setTimeout(() => document.querySelector<HTMLButtonElement>('.inbox-tabs [aria-selected="true"]')?.focus());
  }

  protected read(notification: NotificationItem): void {
    if (notification.read) return;
    this.notificationService.markRead(notification.id).subscribe({ next: () => this.setRead(notification.id) });
  }

  protected readAll(): void {
    this.notificationService.markAllRead(this.notifications()).subscribe({
      next: () => {
        this.notifications.update(items => items.map(item => ({ ...item, read: true })));
        this.emitUnread();
      },
      error: () => this.error.set('Bildirimler güncellenemedi.')
    });
  }

  protected label(notification: NotificationItem): string {
    return notificationLabel(notification);
  }

  protected sourceRoute(notification: NotificationItem): readonly string[] | null {
    if (notification.actionKind === 'OpenTeam' && notification.sourceId) return ['/workspace', 'section', 'teams'];
    if (notification.actionKind !== 'OpenWorkItem' || !notification.sourceId) return null;
    const task = this.tasks().find(item => item.id === notification.sourceId);
    if (task) return this.taskRoute(task);
    return notification.projectId ? ['/workspace', notification.projectId, 'board', 'task', notification.sourceId] : null;
  }

  protected taskRoute(task: PersonalWorkItem): readonly string[] {
    return ['/workspace', task.projectId, 'board', 'task', task.id];
  }

  protected formatDate(value: string): string {
    return new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(value));
  }

  private loadApprovals(): void {
    this.workLoading.set(true);
    this.personalWork.load(this.projects(), this.userId()).pipe(finalize(() => this.workLoading.set(false))).subscribe({
      next: result => this.tasks.set(result.tasks),
      error: () => this.workError.set(true)
    });
  }

  private setRead(notificationId: string): void {
    this.notifications.update(items => items.map(item => item.id === notificationId ? { ...item, read: true } : item));
    this.emitUnread();
  }

  private emitUnread(): void {
    this.unreadChange.emit(this.unreadCount());
  }
}

function readMode(): InboxMode {
  const value = localStorage.getItem(MODE_KEY);
  return MODES.includes(value as InboxMode) ? value as InboxMode : 'unread';
}

function mergeNotifications(current: readonly NotificationItem[], next: readonly NotificationItem[]): readonly NotificationItem[] {
  return [...current, ...next.filter(item => !current.some(existing => existing.id === item.id))];
}
