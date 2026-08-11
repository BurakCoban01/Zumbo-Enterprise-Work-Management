import { HttpClient, HttpContext, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, fromEvent, map, mergeMap, Observable, takeUntil, throwError } from 'rxjs';
import { createIdentifier, isSafeMethod, normalizeApiError, resourceIdentity, unwrapEnvelope, validateIdempotencyKey } from './api-core';
import { ZUMBO_CORRELATION_ID, ZUMBO_IDEMPOTENCY_KEY, ZUMBO_IF_MATCH } from './api-context';
import { ZUMBO_RUNTIME_CONFIG } from './runtime-config';

export interface ApiRequestOptions {
  readonly correlationId?: string;
  readonly idempotencyKey?: string;
  readonly ifMatch?: string | number;
  readonly rawResponse?: boolean;
  readonly signal?: AbortSignal;
}

@Injectable({ providedIn: 'root' })
export class ZumboApiClient {
  private readonly http = inject(HttpClient);
  private readonly runtime = inject(ZUMBO_RUNTIME_CONFIG);
  private readonly resourceVersions = new Map<string, number>();

  get<T>(path: string, options: ApiRequestOptions = {}): Observable<T> {
    return this.execute<T>('GET', path, undefined, options);
  }

  post<T>(path: string, body: unknown, options: ApiRequestOptions = {}): Observable<T> {
    return this.execute<T>('POST', path, body, options);
  }

  put<T>(path: string, body: unknown, options: ApiRequestOptions = {}): Observable<T> {
    return this.execute<T>('PUT', path, body, options);
  }

  patch<T>(path: string, body: unknown, options: ApiRequestOptions = {}): Observable<T> {
    return this.execute<T>('PATCH', path, body, options);
  }

  delete<T>(path: string, options: ApiRequestOptions = {}): Observable<T> {
    return this.execute<T>('DELETE', path, undefined, options);
  }

  upload<T>(path: string, file: File, options: ApiRequestOptions = {}): Observable<T> {
    const form = new FormData();
    form.append('file', file);
    return this.execute<T>('POST', path, form, options);
  }

  download(path: string, options: ApiRequestOptions = {}): Observable<Blob> {
    const context = this.contextFor('GET', path, options);
    return this.http.get(this.runtime.apiBaseUrl + path, {
      context,
      observe: 'body',
      responseType: 'blob',
      withCredentials: true
    }).pipe(this.cancelWhen(options.signal));
  }

  newIdempotencyKey(): string {
    return createIdentifier('idem-');
  }

  clearVersions(): void {
    this.resourceVersions.clear();
  }

  private execute<T>(method: string, path: string, body: unknown, options: ApiRequestOptions): Observable<T> {
    const context = this.contextFor(method, path, options);
    return this.http.request(method, this.runtime.apiBaseUrl + path, {
      body,
      context,
      observe: 'response',
      withCredentials: true
    }).pipe(
      this.cancelWhen(options.signal),
      map(response => options.rawResponse
        ? response as unknown as T
        : this.remember(path, unwrapEnvelope(response.body) as T)),
      catchError(error => {
        const target = resourceIdentity(path);
        const normalized = normalizeApiError(error);
        if (normalized.status === 409 && normalized.code === 'CONCURRENCY_CONFLICT' && target) {
          this.resourceVersions.delete(`${target.kind}:${target.id}`);
        }
        return throwError(() => normalized);
      })
    );
  }

  private contextFor(method: string, path: string, options: ApiRequestOptions): HttpContext {
    const idempotencyKey = validateIdempotencyKey(options.idempotencyKey);
    let context = new HttpContext()
      .set(ZUMBO_CORRELATION_ID, options.correlationId || createIdentifier('web-'))
      .set(ZUMBO_IDEMPOTENCY_KEY, idempotencyKey);
    if (!isSafeMethod(method)) {
      const target = resourceIdentity(path);
      const remembered = target ? this.resourceVersions.get(`${target.kind}:${target.id}`) : null;
      const version = options.ifMatch ?? remembered;
      if (version != null) context = context.set(ZUMBO_IF_MATCH, `"${String(version).replace(/^"|"$/g, '')}"`);
    }
    return context;
  }

  private remember<T>(path: string, value: T): T {
    const target = resourceIdentity(path);
    if (!target || !value || typeof value !== 'object') return value;
    const values = Array.isArray(value) ? value : [value];
    for (const item of values) {
      const candidate = item as { id?: string; version?: number };
      if (candidate.id && Number(candidate.version) > 0) {
        this.resourceVersions.set(`${target.kind}:${candidate.id}`, Number(candidate.version));
      }
    }
    return value;
  }

  private cancelWhen<T>(signal?: AbortSignal): (source: Observable<T>) => Observable<T> {
    if (!signal) return source => source;
    return source => source.pipe(takeUntil(fromEvent(signal, 'abort').pipe(
      mergeMap(() => throwError(() => normalizeApiError(new DOMException('Aborted', 'AbortError'))))
    )));
  }
}
