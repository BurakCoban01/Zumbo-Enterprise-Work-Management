import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { IonContent, IonHeader, IonIcon, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { chevronForwardOutline, folderOpenOutline, logOutOutline, personCircleOutline, searchOutline } from 'ionicons/icons';
import { ZumboRealtimeService, ZumboSessionService } from '@zumbo/modern-shared';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';

@Component({
  selector: 'zumbo-mobile-more',
  imports: [RouterLink, IonContent, IonHeader, IonIcon, IonTitle, IonToolbar],
  templateUrl: './mobile-more.page.html',
  styleUrl: './mobile-more.page.scss'
})
export class MobileMorePage {
  protected readonly session = inject(ZumboSessionService);
  protected readonly store = inject(MobileWorkspaceStore);
  private readonly realtime = inject(ZumboRealtimeService);
  private readonly router = inject(Router);

  constructor() {
    addIcons({ folderOpenOutline, logOutOutline, chevronForwardOutline, personCircleOutline, searchOutline });
  }

  protected logout(): void {
    void this.realtime.stop().finally(() =>
      this.session.logout().subscribe(() => void this.router.navigate(['/login']))
    );
  }
}
