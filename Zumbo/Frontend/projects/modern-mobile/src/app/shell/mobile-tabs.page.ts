import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { IonBadge, IonIcon, IonLabel, IonTabBar, IonTabButton, IonTabs } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { addCircleOutline, briefcaseOutline, gridOutline, homeOutline, notificationsOutline } from 'ionicons/icons';
import { ZumboSessionService } from '@zumbo/modern-shared';
import { filter } from 'rxjs';
import { MobileConnectivityService } from './mobile-connectivity.service';
import { MobileWorkspaceStore } from './mobile-workspace.store';

@Component({
  selector: 'zumbo-mobile-tabs',
  imports: [IonBadge, IonIcon, IonLabel, IonTabBar, IonTabButton, IonTabs],
  templateUrl: './mobile-tabs.page.html',
  styleUrl: './mobile-tabs.page.scss'
})
export class MobileTabsPage {
  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly store = inject(MobileWorkspaceStore);
  private readonly router = inject(Router);
  private readonly session = inject(ZumboSessionService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly currentUrl = signal(this.router.url);
  protected readonly unread = computed(() => this.store.notifications().filter(item => !item.read).length);
  protected readonly moreContext = computed(() =>
    ['/workspace/projects', '/workspace/search', '/workspace/account', '/workspace/portfolios', '/workspace/goals', '/workspace/capacity', '/workspace/knowledge', '/workspace/teams', '/teams/'].some(path => this.currentUrl().includes(path))
  );

  constructor() {
    addIcons({ homeOutline, briefcaseOutline, addCircleOutline, notificationsOutline, gridOutline });
    this.router.events.pipe(filter(event => event instanceof NavigationEnd), takeUntilDestroyed(this.destroyRef)).subscribe(event => this.currentUrl.set(event.urlAfterRedirects));
    this.session.restore().subscribe(auth => {
      if (!auth) void this.router.navigate(['/login']);
      else void this.store.load().catch(() => undefined);
    });
  }
}
