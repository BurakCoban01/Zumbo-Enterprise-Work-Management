import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { provideZumboFoundation, trustedServiceWorkerScript } from '@zumbo/modern-shared';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideZumboFoundation(),
    provideServiceWorker(trustedServiceWorkerScript(), { enabled: !isDevMode() })
  ]
};
