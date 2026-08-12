import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { applyLegacyMobileLocation } from './app/legacy-mobile-route';
import { installIoniconsTrustedTypesPolicy } from './app/trusted-ionicons';

installIoniconsTrustedTypesPolicy();
applyLegacyMobileLocation();

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
