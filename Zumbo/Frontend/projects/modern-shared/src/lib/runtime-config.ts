import { InjectionToken } from '@angular/core';

export interface ZumboRuntimeConfig {
  readonly apiBaseUrl: string;
}

declare global {
  interface Window {
    __ZUMBO_RUNTIME_CONFIG__?: Partial<ZumboRuntimeConfig>;
  }
}

function resolveRuntimeConfig(): ZumboRuntimeConfig {
  const configured = window.__ZUMBO_RUNTIME_CONFIG__?.apiBaseUrl?.trim() ?? '';
  return Object.freeze({ apiBaseUrl: (configured || window.location.origin).replace(/\/+$/, '') });
}

export const ZUMBO_RUNTIME_CONFIG = new InjectionToken<ZumboRuntimeConfig>('ZUMBO_RUNTIME_CONFIG', {
  providedIn: 'root',
  factory: resolveRuntimeConfig
});
