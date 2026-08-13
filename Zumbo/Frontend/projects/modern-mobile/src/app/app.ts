import { Component, inject } from '@angular/core';
import { IonApp, IonRouterOutlet } from '@ionic/angular/standalone';
import { PwaUpdateService } from '@zumbo/modern-shared';
import { MobileThemeService } from './shell/mobile-theme.service';

@Component({
  selector: 'zumbo-root',
  imports: [IonApp, IonRouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly pwaUpdate = inject(PwaUpdateService);
  protected readonly theme = inject(MobileThemeService);
}
