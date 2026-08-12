import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { canReplay, createIdentifier, isSafeMethod, normalizeApiError, syntheticApiError } from './api-core';
import { ZUMBO_CORRELATION_ID, ZUMBO_IDEMPOTENCY_KEY, ZUMBO_IF_MATCH, ZUMBO_SKIP_REFRESH } from './api-context';
import { ZUMBO_RUNTIME_CONFIG } from './runtime-config';
import { ZumboSessionService } from './session.service';

const PUBLIC_AUTH_PATHS = new Set([
  '/api/browser-auth/login',
  '/api/browser-auth/register',
  '/api/browser-auth/session',
  '/api/browser-auth/refresh',
  '/api/browser-auth/logout',
  '/api/auth/forgot-password',
  '/api/auth/reset-password'
]);

export const zumboApiInterceptor: HttpInterceptorFn = (request, next) => {
  const runtime = inject(ZUMBO_RUNTIME_CONFIG);
  const session = inject(ZumboSessionService);
  if (!request.url.startsWith(runtime.apiBaseUrl)) return next(request);

  const correlationId = request.context.get(ZUMBO_CORRELATION_ID) || createIdentifier('web-');
  const idempotencyKey = request.context.get(ZUMBO_IDEMPOTENCY_KEY);
  const decorated = decorate(request, session, correlationId, idempotencyKey);

  return next(decorated).pipe(catchError(error => {
    const path = new URL(request.url).pathname;
    if (error?.status !== 401 || PUBLIC_AUTH_PATHS.has(path) || request.context.get(ZUMBO_SKIP_REFRESH)) {
      return throwError(() => normalizeApiError(error, correlationId));
    }
    return session.refresh().pipe(
      switchMap(() => {
        if (!canReplay(request.method, idempotencyKey)) {
          return throwError(() => syntheticApiError(
            'REQUEST_REPLAY_REQUIRED',
            'Your session was renewed. Retry this action to avoid a duplicate change.',
            409,
            correlationId
          ));
        }
        return next(decorate(request, session, correlationId, idempotencyKey));
      }),
      catchError(refreshError => throwError(() => normalizeApiError(refreshError, correlationId)))
    );
  }));
};

function decorate(
  request: HttpRequest<unknown>,
  session: ZumboSessionService,
  correlationId: string,
  idempotencyKey: string | null
): HttpRequest<unknown> {
  let headers = request.headers.set('X-Correlation-Id', correlationId);
  if (!isSafeMethod(request.method)) {
    const csrf = session.getCsrf();
    if (csrf) headers = headers.set('X-CSRF-Token', csrf);
  }
  if (idempotencyKey) headers = headers.set('Idempotency-Key', idempotencyKey);
  const ifMatch = request.context.get(ZUMBO_IF_MATCH);
  if (ifMatch) headers = headers.set('If-Match', ifMatch);
  return request.clone({ headers, withCredentials: true });
}
