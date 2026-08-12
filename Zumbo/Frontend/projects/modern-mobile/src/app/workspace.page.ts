import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { MobileWorkspaceStore } from './shell/mobile-workspace.store';

@Component({
  selector: 'zumbo-mobile-workspace',
  imports: [FormsModule, RouterLink, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar],
  templateUrl: './workspace.page.html',
  styleUrl: './workspace.page.scss'
})
export class MobileWorkspacePage {
  protected readonly store = inject(MobileWorkspaceStore);
  protected readonly query = signal('');
  protected readonly visible = computed(() => {
    const query = this.query().trim().toLocaleLowerCase('tr');
    if (!query) return this.store.projects();
    return this.store.projects().filter(item => `${item.key} ${item.name}`.toLocaleLowerCase('tr').includes(query));
  });

  protected async refresh(event: Event): Promise<void> {
    try {
      await this.store.load(true);
    } finally {
      await (event.target as unknown as { complete(): Promise<void> }).complete();
    }
  }
}
