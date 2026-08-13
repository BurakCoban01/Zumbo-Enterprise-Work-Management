import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { IonContent, IonHeader, IonIcon, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { bookOutline, chevronForwardOutline, flagOutline, folderOpenOutline, layersOutline, linkOutline, logOutOutline, moonOutline, peopleOutline, personCircleOutline, searchOutline, serverOutline, sunnyOutline } from 'ionicons/icons';
import { ZumboApiClient, ZumboRealtimeService, ZumboSessionService } from '@zumbo/modern-shared';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';
import { MobileThemeService } from '../../shell/mobile-theme.service';

@Component({
  selector: 'zumbo-mobile-more',
  imports: [RouterLink, IonContent, IonHeader, IonIcon, IonTitle, IonToolbar],
  templateUrl: './mobile-more.page.html',
  styleUrl: './mobile-more.page.scss'
})
export class MobileMorePage {
  protected readonly session = inject(ZumboSessionService);
  protected readonly store = inject(MobileWorkspaceStore);
  protected readonly theme = inject(MobileThemeService);
  private readonly realtime = inject(ZumboRealtimeService);
  private readonly router = inject(Router);
  private readonly api = inject(ZumboApiClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly systemRoles = signal<readonly { readonly name: string; readonly permissions: readonly string[]; readonly isActive: boolean }[]>([]);
  protected readonly canIntegrations = computed(() => this.hasSystemPermission('IntegrationManage'));
  protected readonly canOperations = computed(() => this.hasSystemPermission('OperationsManage'));

  constructor() {
    addIcons({ bookOutline, folderOpenOutline, linkOutline, logOutOutline, moonOutline, chevronForwardOutline, flagOutline, layersOutline, peopleOutline, personCircleOutline, searchOutline, serverOutline, sunnyOutline });
    this.api.get<readonly { readonly name: string; readonly permissions: readonly string[]; readonly isActive: boolean }[]>('/api/auth/roles?scope=System').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: roles => this.systemRoles.set(roles) });
  }

  protected logout(): void {
    void this.realtime.stop().finally(() =>
      this.session.logout().subscribe(() => void this.router.navigate(['/login']))
    );
  }

  private hasSystemPermission(permission: string): boolean { const names = this.session.currentUser()?.roles ?? []; return this.systemRoles().some(role => role.isActive && names.includes(role.name) && (role.permissions.includes('*') || role.permissions.includes(permission))); }
}
