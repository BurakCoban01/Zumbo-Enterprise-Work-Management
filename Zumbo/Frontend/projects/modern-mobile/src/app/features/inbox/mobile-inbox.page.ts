import { Component, computed, inject, signal } from '@angular/core';
import { IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { MobileInboxMode, notificationLabel } from '../../shell/mobile-workspace.models';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';

@Component({
  selector: 'zumbo-mobile-inbox',
  imports: [IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar],
  templateUrl: './mobile-inbox.page.html',
  styleUrl: './mobile-inbox.page.scss'
})
export class MobileInboxPage {
  protected readonly store = inject(MobileWorkspaceStore);
  protected readonly mode = signal<MobileInboxMode>('unread');
  protected readonly visible = computed(() => {
    if (this.mode() === 'all') return this.store.notifications();
    if (this.mode() === 'actions') return this.store.notifications().filter(item => item.category === 'Action');
    return this.store.notifications().filter(item => !item.read);
  });
  protected readonly label = notificationLabel;

  protected setMode(value: MobileInboxMode): void {
    this.mode.set(value);
  }

  protected async read(id: string): Promise<void> {
    try {
      await this.store.markRead(id);
    } catch {
      this.store.error.set('Bildirim güncellenemedi.');
    }
  }

  protected async refresh(event: Event): Promise<void> {
    try {
      await this.store.load(true);
    } finally {
      await (event.target as unknown as { complete(): Promise<void> }).complete();
    }
  }

  protected date(value: string): string {
    return new Intl.DateTimeFormat('tr-TR', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit'
    }).format(new Date(value));
  }
}
