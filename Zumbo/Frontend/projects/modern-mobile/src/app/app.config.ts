import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { provideIonicAngular } from '@ionic/angular/standalone';
import { provideZumboFoundation } from '@zumbo/modern-shared';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideIonicAngular({ mode: 'md' }),
    provideRouter(routes),
    provideZumboFoundation(),
    provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode() })
  ]
};
