import { HttpBackend, HttpClient, HttpHeaders } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { catchError, finalize, firstValueFrom, from, map, Observable, of, tap, throwError } from 'rxjs';
import { ApiEnvelope, createIdentifier, normalizeApiError, readCookie, unwrapEnvelope } from './api-core';
import { ZUMBO_RUNTIME_CONFIG } from './runtime-config';

export interface ZumboUser {
  readonly id: string;
  readonly username: string;
  readonly email: string;
  readonly organizationId: string;
  readonly roles: readonly string[];
}

export interface AuthResponse {
  readonly user: ZumboUser;
  readonly csrfToken?: string | null;
}

export interface LoginRequest {
  readonly usernameOrEmail: string;
  readonly password: string;
  readonly mfaCode?: string;
}

const USER_KEY = 'zumbo.modern.currentUser';
const CSRF_KEY = 'zumbo.modern.csrfToken';
const TENANT_LOCAL_KEYS = [USER_KEY, 'zumbo.modern.projectId', 'zumbo.modern.recentProjects'];
const TENANT_SESSION_KEYS = [CSRF_KEY];

@Injectable({ providedIn: 'root' })
export class ZumboSessionService {
  private readonly runtime = inject(ZUMBO_RUNTIME_CONFIG);
  private readonly rawHttp = new HttpClient(inject(HttpBackend));
  private restorePromise: Promise<AuthResponse | null> | null = null;
  private refreshPromise: Promise<AuthResponse> | null = null;
  private readonly userState = signal<ZumboUser | null>(readStoredUser());

  readonly currentUser = this.userState.asReadonly();
  readonly authenticated = computed(() => this.userState() !== null);

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.authPost('/api/browser-auth/login', request).pipe(tap(auth => this.accept(auth)));
  }

  forgotPassword(email: string): Observable<void> {
    return this.authPost<unknown>('/api/auth/forgot-password', { email }).pipe(map(() => undefined));
  }

  resetPassword(token: string, newPassword: string): Observable<void> {
    return this.authPost<unknown>('/api/auth/reset-password', { token, newPassword }).pipe(map(() => undefined));
  }

  restore(): Observable<AuthResponse | null> {
    if (!this.restorePromise) {
      this.restorePromise = firstValueFrom(
        this.authGet('/api/browser-auth/session').pipe(
          tap(auth => this.accept(auth)),
          catchError(() => {
            this.clear();
            return of(null);
          })
        )
      ).finally(() => { this.restorePromise = null; });
    }
    return from(this.restorePromise);
  }

  refresh(): Observable<AuthResponse> {
    if (!this.refreshPromise) {
      this.refreshPromise = firstValueFrom(
        this.authPost('/api/browser-auth/refresh', {}).pipe(
          tap(auth => this.accept(auth)),
          catchError(error => {
            this.clear();
            return throwError(() => normalizeApiError(error));
          })
        )
      ).finally(() => { this.refreshPromise = null; });
    }
    return from(this.refreshPromise);
  }

  logout(allSessions = false): Observable<void> {
    return this.authPost<unknown>('/api/browser-auth/logout', { allSessions }).pipe(
      map(() => undefined),
      catchError(() => of(undefined)),
      finalize(() => this.clear())
    );
  }

  getCsrf(): string | null {
    return sessionStorage.getItem(CSRF_KEY) || readCookie(document.cookie, 'zumbo-csrf');
  }

  clear(): void {
    this.userState.set(null);
    for (const key of TENANT_LOCAL_KEYS) localStorage.removeItem(key);
    for (const key of TENANT_SESSION_KEYS) sessionStorage.removeItem(key);
  }

  private accept(auth: AuthResponse): void {
    this.userState.set(auth.user);
    localStorage.setItem(USER_KEY, JSON.stringify(auth.user));
    if (auth.csrfToken) sessionStorage.setItem(CSRF_KEY, auth.csrfToken);
  }

  private authGet(path: string): Observable<AuthResponse> {
    return this.rawHttp.get<ApiEnvelope<AuthResponse> | AuthResponse>(this.runtime.apiBaseUrl + path, {
      withCredentials: true,
      headers: this.authHeaders('GET')
    }).pipe(map(unwrapEnvelope));
  }

  private authPost<T = AuthResponse>(path: string, body: unknown): Observable<T> {
    return this.rawHttp.post<ApiEnvelope<T> | T>(this.runtime.apiBaseUrl + path, body, {
      withCredentials: true,
      headers: this.authHeaders('POST')
    }).pipe(map(unwrapEnvelope));
  }

  private authHeaders(method: string): HttpHeaders {
    let headers = new HttpHeaders({ 'X-Correlation-Id': createIdentifier('web-') });
    const csrf = this.getCsrf();
    if (method !== 'GET' && csrf) headers = headers.set('X-CSRF-Token', csrf);
    return headers;
  }
}

function readStoredUser(): ZumboUser | null {
  try {
    return JSON.parse(localStorage.getItem(USER_KEY) || 'null') as ZumboUser | null;
  } catch {
    return null;
  }
}
