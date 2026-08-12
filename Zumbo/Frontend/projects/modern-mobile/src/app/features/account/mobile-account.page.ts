import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IonBackButton, IonButton, IonContent, IonHeader, IonIcon, IonSegment, IonSegmentButton, IonSpinner, IonTitle, IonToggle, IonToolbar } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { cloudDownloadOutline, copyOutline, keyOutline, lockClosedOutline, notificationsOutline, phonePortraitOutline, shieldCheckmarkOutline, trashOutline } from 'ionicons/icons';
import { finalize } from 'rxjs';
import { normalizeApiError, ZumboSessionService } from '@zumbo/modern-shared';
import { boundedExpiryDays, isSessionActive, normalizeMutedTypes, visibleSessions } from './mobile-account.core';
import { AccountSession, AccountTab, ApiKeySummary, CreatedApiKey, MfaSetup, MfaStatus, NotificationPreferences, PrivacyJob } from './mobile-account.models';
import { MobileAccountService } from './mobile-account.service';

@Component({
  selector: 'zumbo-mobile-account',
  imports: [DatePipe, FormsModule, IonBackButton, IonButton, IonContent, IonHeader, IonIcon, IonSegment, IonSegmentButton, IonSpinner, IonTitle, IonToggle, IonToolbar],
  providers: [MobileAccountService],
  templateUrl: './mobile-account.page.html',
  styleUrls: ['./mobile-account.page.scss', './mobile-account-forms.scss']
})
export class MobileAccountPage {
  private readonly api = inject(MobileAccountService);
  private readonly session = inject(ZumboSessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly user = this.session.currentUser;
  protected readonly tab = signal<AccountTab>('account');
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly success = signal<string | null>(null);
  protected readonly mfa = signal<MfaStatus>({ enabled: false, remainingRecoveryCodes: 0 });
  protected readonly sessions = signal<readonly AccountSession[]>([]);
  protected readonly apiKeys = signal<readonly ApiKeySummary[]>([]);
  protected readonly preferences = signal<NotificationPreferences>({ inAppEnabled: true, emailEnabled: true, mutedTypes: [] });
  protected readonly mfaSetup = signal<MfaSetup | null>(null);
  protected readonly recoveryCodes = signal<readonly string[]>([]);
  protected readonly createdApiKey = signal<CreatedApiKey | null>(null);
  protected readonly privacyJob = signal<PrivacyJob | null>(null);
  protected readonly showAllSessions = signal(false);
  protected readonly shownSessions = computed(() => visibleSessions(this.sessions(), this.showAllSessions()));
  protected readonly hiddenSessionCount = computed(() => this.sessions().length - this.shownSessions().length);
  protected readonly online = signal(navigator.onLine);
  protected mutedTypesText = '';
  protected passwordDraft = { currentPassword: '', newPassword: '' };
  protected mfaDraft = { password: '', code: '' };
  protected recoveryDraft = { password: '', code: '' };
  protected apiKeyDraft = { name: '', password: '', mfaCode: '', expiresInDays: 90 };
  protected privacyDraft = { password: '', confirmation: '' };

  constructor() {
    addIcons({ cloudDownloadOutline, copyOutline, keyOutline, lockClosedOutline, notificationsOutline, phonePortraitOutline, shieldCheckmarkOutline, trashOutline });
    const updateOnline = () => this.online.set(navigator.onLine);
    window.addEventListener('online', updateOnline); window.addEventListener('offline', updateOnline);
    this.destroyRef.onDestroy(() => { window.removeEventListener('online', updateOnline); window.removeEventListener('offline', updateOnline); });
    this.load();
  }

  protected selectTab(event: CustomEvent): void { this.tab.set((event.detail.value || 'account') as AccountTab); this.clearMessages(); }
  protected load(): void {
    this.loading.set(true); this.error.set(null); this.clearSecrets();
    this.api.load().pipe(finalize(() => this.loading.set(false))).subscribe({
      next: value => { this.mfa.set(value.mfa); this.sessions.set(value.sessions); this.apiKeys.set(value.apiKeys); this.preferences.set(value.preferences); this.mutedTypesText = value.preferences.mutedTypes.join(', '); if (value.failures.length) this.error.set(`${value.failures.join(', ')} alınamadı; diğer hesap bilgileri kullanılabilir.`); this.restorePrivacyJob(); },
      error: () => this.error.set('Hesap bilgileri şu anda alınamadı.')
    });
  }

  protected savePreferences(): void { this.mutate('preferences', this.api.savePreferences({ ...this.preferences(), mutedTypes: normalizeMutedTypes(this.mutedTypesText) }), 'Bildirim tercihleri kaydedildi.', value => { this.preferences.set(value); this.mutedTypesText = value.mutedTypes.join(', '); }); }
  protected changePassword(): void { if (!this.passwordDraft.currentPassword || this.passwordDraft.newPassword.length < 12) return; this.mutate('password', this.api.changePassword(this.passwordDraft.currentPassword, this.passwordDraft.newPassword), 'Parolanız güncellendi.', () => this.passwordDraft = { currentPassword: '', newPassword: '' }); }
  protected beginMfa(): void { if (!this.mfaDraft.password) return; this.mutate('mfa', this.api.beginMfa(this.mfaDraft.password), 'Doğrulayıcı kurulumu hazır.', value => this.mfaSetup.set(value)); }
  protected confirmMfa(): void { if (!this.mfaDraft.code) return; this.mutate('mfa', this.api.confirmMfa(this.mfaDraft.code), 'İki adımlı doğrulama etkinleştirildi. Kurtarma kodlarını şimdi saklayın.', value => { this.mfa.set({ enabled: value.enabled, remainingRecoveryCodes: value.recoveryCodes.length }); this.recoveryCodes.set(value.recoveryCodes); this.mfaSetup.set(null); this.mfaDraft = { password: '', code: '' }; }); }
  protected regenerateCodes(): void { if (!this.recoveryDraft.password || !this.recoveryDraft.code || !confirm('Mevcut kurtarma kodları hemen geçersiz olacak. Devam edilsin mi?')) return; this.recoveryCodes.set([]); this.mutate('recovery', this.api.regenerateRecoveryCodes(this.recoveryDraft.password, this.recoveryDraft.code), 'Yeni kurtarma kodları oluşturuldu.', value => { this.recoveryCodes.set(value.recoveryCodes); this.mfa.set({ enabled: value.enabled, remainingRecoveryCodes: value.recoveryCodes.length }); this.recoveryDraft = { password: '', code: '' }; }); }
  protected disableMfa(): void { if (!this.mfaDraft.password || !this.mfaDraft.code || !confirm('İki adımlı doğrulama kapatılsın mı?')) return; this.mutate('mfa', this.api.disableMfa(this.mfaDraft.password, this.mfaDraft.code), 'İki adımlı doğrulama kapatıldı.', value => { this.mfa.set(value); this.mfaDraft = { password: '', code: '' }; this.recoveryCodes.set([]); }); }
  protected sessionActive(value: AccountSession): boolean { return isSessionActive(value); }
  protected revokeSession(value: AccountSession): void { if (!this.sessionActive(value) || !confirm(value.isCurrent ? 'Bu cihazdaki oturum kapatılacak. Devam edilsin mi?' : `${value.deviceName || 'Seçilen cihaz'} oturumu kapatılsın mı?`)) return; this.mutate(`session:${value.id}`, this.api.revokeSession(value.id), 'Oturum kapatıldı.', () => { this.sessions.update(items => items.map(item => item.id === value.id ? { ...item, revokedAt: new Date().toISOString() } : item)); if (value.isCurrent) { this.session.clear(); void this.router.navigate(['/login']); } }); }
  protected createApiKey(): void { if (!this.apiKeyDraft.name.trim() || !this.apiKeyDraft.password) return; const expires = new Date(); expires.setDate(expires.getDate() + boundedExpiryDays(this.apiKeyDraft.expiresInDays)); this.mutate('api-key', this.api.createApiKey({ name: this.apiKeyDraft.name.trim(), password: this.apiKeyDraft.password, mfaCode: this.apiKeyDraft.mfaCode || null, expiresAt: expires.toISOString(), scopes: ['api:full'] }), 'API anahtarı oluşturuldu. Tam değeri yalnızca şimdi görüntülenir.', value => { this.createdApiKey.set(value); this.apiKeys.update(items => [value, ...items]); this.apiKeyDraft = { name: '', password: '', mfaCode: '', expiresInDays: 90 }; }); }
  protected revokeApiKey(value: ApiKeySummary): void { if (value.revokedAt || !confirm(`${value.name} API anahtarı kapatılsın mı?`)) return; this.mutate(`key:${value.id}`, this.api.revokeApiKey(value.id), 'API anahtarı kapatıldı.', () => this.apiKeys.update(items => items.map(item => item.id === value.id ? { ...item, revokedAt: new Date().toISOString() } : item))); }
  protected exportData(): void { if (this.busy()) return; this.busy.set('export'); this.api.exportPrivacyData().pipe(finalize(() => this.busy.set(null))).subscribe({ next: blob => { const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = 'zumbo-verilerim.ndjson'; anchor.click(); URL.revokeObjectURL(url); this.success.set('Veri arşiviniz indirildi.'); }, error: value => this.error.set(normalizeApiError(value).message || 'Veri arşivi indirilemedi.') }); }
  protected anonymize(): void { if (!this.privacyDraft.password || this.privacyDraft.confirmation !== 'ANONYMIZE' || !confirm('Hesap erişimi kalıcı olarak kapanacak. Bu işlem geri alınamaz. Devam edilsin mi?')) return; this.mutate('privacy', this.api.anonymize(this.privacyDraft.password), 'Anonimleştirme işlemi sıraya alındı.', value => { this.privacyJob.set(value.job); sessionStorage.setItem(this.privacyKey(), value.job.id); this.privacyDraft = { password: '', confirmation: '' }; }); }
  protected copy(value: string | readonly string[]): void { const text = Array.isArray(value) ? value.join('\n') : String(value); void navigator.clipboard?.writeText(text); this.success.set('Güvenli değer panoya kopyalandı.'); }
  protected dismissSecrets(): void { this.clearSecrets(); }

  private mutate<T>(key: string, request: import('rxjs').Observable<T>, message: string, accept: (value: T) => void): void {
    if (this.busy() || !this.online()) return; this.busy.set(key); this.clearMessages();
    request.pipe(finalize(() => this.busy.set(null))).subscribe({ next: value => { accept(value); this.success.set(message); }, error: value => this.error.set(normalizeApiError(value).message || 'İşlem tamamlanamadı.') });
  }
  private clearMessages(): void { this.error.set(null); this.success.set(null); }
  private clearSecrets(): void { this.mfaSetup.set(null); this.recoveryCodes.set([]); this.createdApiKey.set(null); }
  private privacyKey(): string { return `zumbo.modern.mobile.privacy.${this.user()?.id || 'me'}`; }
  private restorePrivacyJob(): void {
    const id = sessionStorage.getItem(this.privacyKey());
    if (!id) return;
    this.api.privacyJob(id).subscribe({ next: value => this.privacyJob.set(value), error: () => sessionStorage.removeItem(this.privacyKey()) });
  }
}
