import { Component, OnInit, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { HomeData, HomeNotification, PersonalWorkItem } from './home.models';
import { HomeService } from './home.service';

@Component({
  selector: 'zumbo-home-page',
  imports: [RouterLink],
  providers: [HomeService],
  templateUrl: './home.page.html',
  styleUrl: './home.page.scss'
})
export class HomePage implements OnInit {
  readonly projects = input.required<readonly ProjectSummary[]>();
  readonly userId = input.required<string>();
  readonly username = input.required<string>();
  readonly unreadChange = output<number>();

  protected readonly data = signal<HomeData>({ tasks: [], notifications: [], partial: false });
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly assigned = computed(() => this.data().tasks.filter(task => !task.completedAt));
  protected readonly due = computed(() => this.assigned().filter(task => task.dueDate).sort(compareDueDates));
  protected readonly blocked = computed(() => this.assigned().filter(isBlocked));
  protected readonly unread = computed(() => this.data().notifications.filter(notification => !notification.read));

  constructor(private readonly home: HomeService) {}

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.home.load(this.projects(), this.userId()).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: data => {
        this.data.set(data);
        this.unreadChange.emit(data.notifications.filter(notification => !notification.read).length);
      },
      error: () => this.error.set('Kişisel çalışma görünümü yüklenemedi.')
    });
  }

  protected rememberMode(mode: string): void {
    localStorage.setItem('zumbo.personal.mode', mode);
  }

  protected read(notification: HomeNotification): void {
    if (notification.read) return;
    this.home.markNotificationRead(notification.id).subscribe({
      next: () => {
        const notifications = this.data().notifications.map(item => item.id === notification.id ? { ...item, read: true } : item);
        this.data.update(data => ({ ...data, notifications }));
        this.unreadChange.emit(this.unread().length);
      }
    });
  }

  protected taskRoute(task: PersonalWorkItem): readonly string[] {
    return ['/workspace', task.projectId, 'board', 'task', task.id];
  }

  protected notificationLabel(notification: HomeNotification): string {
    return NOTIFICATION_LABELS[notification.type] ?? 'Bildirim';
  }

  protected formatDate(value: string | null | undefined): string {
    return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value)) : '';
  }
}

const NOTIFICATION_LABELS: Readonly<Record<string, string>> = {
  Mention: 'Bahsetme',
  Assignment: 'Atama',
  ApprovalRequest: 'Onay isteği',
  Approval: 'Onay sonucu',
  DueDateReminder: 'Tarih hatırlatması',
  TeamInvitation: 'Ekip daveti'
};

function isBlocked(task: PersonalWorkItem): boolean {
  return (task.relations ?? []).some(relation => ['blockedby', 'isblockedby', 'dependson'].includes(String(relation.relationType ?? '').toLowerCase()));
}

function compareDueDates(left: PersonalWorkItem, right: PersonalWorkItem): number {
  return new Date(left.dueDate ?? 0).getTime() - new Date(right.dueDate ?? 0).getTime();
}
