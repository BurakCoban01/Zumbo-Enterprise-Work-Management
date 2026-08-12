import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { ZumboSessionService } from '@zumbo/modern-shared';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';
import { dueTime, isBlocked, isOpen, notificationLabel } from '../../shell/mobile-workspace.models';

@Component({ selector: 'zumbo-mobile-home', imports: [RouterLink, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar], templateUrl: './mobile-home.page.html', styleUrls: ['./mobile-home.page.scss', './mobile-daily-work.shared.scss'] })
export class MobileHomePage {
  protected readonly store = inject(MobileWorkspaceStore);
  protected readonly session = inject(ZumboSessionService);
  protected readonly open = computed(() => this.store.tasks().filter(isOpen));
  protected readonly due = computed(() => this.open().filter(item => item.dueDate).sort((a, b) => dueTime(a) - dueTime(b)));
  protected readonly blocked = computed(() => this.open().filter(isBlocked));
  protected readonly unread = computed(() => this.store.notifications().filter(item => !item.read));
  protected readonly label = notificationLabel;

  protected async refresh(event: Event): Promise<void> { try { await this.store.load(true); } finally { await complete(event); } }
  protected formatDate(value?: string | null): string { return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value)) : ''; }
}

async function complete(event: Event): Promise<void> { await (event.target as unknown as { complete(): Promise<void> }).complete(); }
