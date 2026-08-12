import { inject, Injectable } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { catchError, forkJoin, map, Observable, of } from 'rxjs';
import { AccountSession, AccountSnapshot, ApiKeySummary, CreatedApiKey, MfaConfirmation, MfaSetup, MfaStatus, NotificationPreferences, PrivacyJob, PrivacyReceipt } from './mobile-account.models';

@Injectable()
export class MobileAccountService {
  private readonly api = inject(ZumboApiClient);

  load(): Observable<AccountSnapshot> {
    const failures: string[] = [];
    const safe = <T>(name: string, request: Observable<T>, fallback: T) => request.pipe(catchError(() => { failures.push(name); return of(fallback); }));
    return forkJoin({
      mfa: safe('iki adımlı doğrulama', this.api.get<MfaStatus>('/api/auth/mfa'), { enabled: false, remainingRecoveryCodes: 0 }),
      sessions: safe('cihaz oturumları', this.api.get<readonly AccountSession[]>('/api/auth/sessions'), []),
      apiKeys: safe('API anahtarları', this.api.get<readonly ApiKeySummary[]>('/api/auth/api-keys'), []),
      preferences: safe('bildirim tercihleri', this.api.get<NotificationPreferences>('/api/notifications/preferences/me'), { inAppEnabled: true, emailEnabled: true, mutedTypes: [] })
    }).pipe(map(value => ({ ...value, failures })));
  }

  changePassword(currentPassword: string, newPassword: string) { return this.api.post<void>('/api/auth/change-password', { currentPassword, newPassword }); }
  savePreferences(value: NotificationPreferences) { return this.api.put<NotificationPreferences>('/api/notifications/preferences/me', value); }
  beginMfa(password: string) { return this.api.post<MfaSetup>('/api/auth/mfa/setup', { password }); }
  confirmMfa(code: string) { return this.api.post<MfaConfirmation>('/api/auth/mfa/confirm', { code }); }
  disableMfa(password: string, code: string) { return this.api.post<MfaStatus>('/api/auth/mfa/disable', { password, code }); }
  regenerateRecoveryCodes(password: string, code: string) { return this.api.post<MfaConfirmation>('/api/auth/mfa/recovery-codes', { password, code }); }
  revokeSession(id: string) { return this.api.delete<void>(`/api/auth/sessions/${encodeURIComponent(id)}`); }
  createApiKey(request: { readonly name: string; readonly password: string; readonly mfaCode: string | null; readonly expiresAt: string; readonly scopes: readonly string[] }) { return this.api.post<CreatedApiKey>('/api/auth/api-keys', request); }
  revokeApiKey(id: string) { return this.api.delete<void>(`/api/auth/api-keys/${encodeURIComponent(id)}`); }
  exportPrivacyData() { return this.api.download('/api/auth/privacy/export.ndjson'); }
  anonymize(password: string) { return this.api.post<PrivacyReceipt>('/api/auth/privacy/anonymization-jobs', { password, confirmation: 'ANONYMIZE' }); }
  privacyJob(id: string) { return this.api.get<PrivacyJob>(`/api/auth/privacy/jobs/${encodeURIComponent(id)}`); }
}
