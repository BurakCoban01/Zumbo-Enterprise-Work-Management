import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { zumboApiInterceptor } from './api.interceptor';

export function provideZumboFoundation(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideHttpClient(withInterceptors([zumboApiInterceptor]))
  ]);
}
