import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, OnDestroy, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ZumboIconComponent } from '../../../shell/zumbo-icon.component';
import { boundedExpiryDays, isSessionActive, normalizeMutedTypes, privacyProgress, visibleSessions } from './account-settings.core';
import { AccountSettingsContext, AccountSession, ApiKeySummary, CreatedApiKey, MfaSetup, MfaStatus, NotificationPreferences, PrivacyJob } from './account-settings.models';
import { AccountSettingsService } from './account-settings.service';

const PRIVACY_KEY_PREFIX = 'zumbo.modern.privacyJob.';

@Component({
  selector: 'zumbo-account-settings-page', imports: [DatePipe, FormsModule, ZumboIconComponent], providers: [AccountSettingsService],
  templateUrl: './account-settings.page.html', styleUrls: ['./account-settings.page.scss', './account-settings-disclosure.scss', './account-settings-responsive.scss']
})
export class AccountSettingsPage implements OnDestroy {
  private readonly api = inject(AccountSettingsService); private readonly destroyRef = inject(DestroyRef);
  readonly context = input.required<AccountSettingsContext>(); readonly currentSessionRevoked = output<void>();
  protected readonly loading = signal(true); protected readonly busy = signal<string | null>(null); protected readonly error = signal<string | null>(null); protected readonly notice = signal<string | null>(null);
  protected readonly mfa = signal<MfaStatus>({ enabled: false, remainingRecoveryCodes: 0 }); protected readonly sessions = signal<readonly AccountSession[]>([]); protected readonly apiKeys = signal<readonly ApiKeySummary[]>([]); protected readonly preferences = signal<NotificationPreferences>({ inAppEnabled: true, emailEnabled: true, mutedTypes: [] });
  protected readonly mfaSetup = signal<MfaSetup | null>(null); protected readonly recoveryCodes = signal<readonly string[]>([]); protected readonly createdApiKey = signal<CreatedApiKey | null>(null); protected readonly privacyJob = signal<PrivacyJob | null>(null);
  protected readonly showAllSessions = signal(false); protected readonly activeSessionCount = computed(() => this.sessions().filter(item => isSessionActive(item)).length); protected readonly shownSessions = computed(() => this.showAllSessions() ? visibleSessions(this.sessions(), Date.now(), Number.MAX_SAFE_INTEGER, Number.MAX_SAFE_INTEGER) : visibleSessions(this.sessions())); protected readonly hiddenSessionCount = computed(() => Math.max(0, this.sessions().length - this.shownSessions().length)); protected readonly privacyProgress = computed(() => privacyProgress(this.privacyJob()));
  protected passwordDraft = { currentPassword: '', newPassword: '' }; protected mfaDraft = { password: '', code: '' }; protected recoveryDraft = { password: '', code: '' }; protected apiKeyDraft = { name: '', password: '', mfaCode: '', expiresInDays: 90 }; protected mutedTypesText = ''; protected privacyDraft = { password: '', confirmation: '' };

  constructor() { effect(() => { this.context(); untracked(() => this.load()); }); }
  ngOnDestroy(): void { this.clearOneTimeSecrets(); }

  protected load(): void {
    if (this.busy() || this.mfaSetup() || this.recoveryCodes().length || this.createdApiKey()) return;
    this.loading.set(true); this.error.set(null);
    this.api.load().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: value => { this.mfa.set(value.mfa); this.sessions.set(value.sessions); this.apiKeys.set(value.apiKeys); this.preferences.set(value.preferences); this.mutedTypesText = value.preferences.mutedTypes.join(', '); this.error.set(value.failures.length ? `${value.failures.join(', ')} alınamadı; diğer hesap bilgileri kullanılabilir.` : null); this.restorePrivacyJob(); },
      error: () => this.error.set('Hesap ayarları şu anda yüklenemedi.')
    });
  }

  protected changePassword(): void { if (!this.passwordDraft.currentPassword || !this.passwordDraft.newPassword) return; this.mutate('password', this.api.changePassword(this.passwordDraft.currentPassword, this.passwordDraft.newPassword), 'Parolanız güncellendi.', () => this.passwordDraft = { currentPassword: '', newPassword: '' }); }
  protected beginMfa(): void { if (!this.mfaDraft.password) return; this.mutate('mfa', this.api.beginMfa(this.mfaDraft.password), 'Doğrulayıcı kurulumu hazır.', value => this.mfaSetup.set(value)); }
  protected confirmMfa(): void { if (!this.mfaDraft.code) return; this.mutate('mfa', this.api.confirmMfa(this.mfaDraft.code), 'İki adımlı doğrulama etkinleştirildi. Kurtarma kodlarını şimdi saklayın.', value => { this.mfa.set({ enabled: value.enabled, remainingRecoveryCodes: value.recoveryCodes.length }); this.recoveryCodes.set(value.recoveryCodes); this.mfaSetup.set(null); this.mfaDraft = { password: '', code: '' }; }); }
  protected disableMfa(): void { if (!this.mfaDraft.password || !this.mfaDraft.code || !confirm('İki adımlı doğrulama devre dışı bırakılsın mı?')) return; this.mutate('mfa', this.api.disableMfa(this.mfaDraft.password, this.mfaDraft.code), 'İki adımlı doğrulama kapatıldı.', value => { this.mfa.set(value); this.mfaDraft = { password: '', code: '' }; }); }
  protected regenerateRecoveryCodes(): void { if (!this.recoveryDraft.password || !this.recoveryDraft.code || !confirm('Mevcut kurtarma kodları hemen geçersiz olacak. Devam edilsin mi?')) return; this.recoveryCodes.set([]); this.mutate('recovery', this.api.regenerateRecoveryCodes(this.recoveryDraft.password, this.recoveryDraft.code), 'Yeni kurtarma kodları oluşturuldu.', value => { this.recoveryCodes.set(value.recoveryCodes); this.mfa.update(status => ({ ...status, remainingRecoveryCodes: value.recoveryCodes.length })); this.recoveryDraft = { password: '', code: '' }; }); }
  protected dismissRecoveryCodes(): void { this.recoveryCodes.set([]); }

  protected sessionActive(value: AccountSession): boolean { return isSessionActive(value); }
  protected revokeSession(value: AccountSession): void { if (!this.sessionActive(value) || !confirm(value.isCurrent ? 'Bu cihazdaki oturum kapatılacak. Devam edilsin mi?' : `${value.deviceName || 'Seçilen cihaz'} oturumu kapatılsın mı?`)) return; this.mutate(`session:${value.id}`, this.api.revokeSession(value.id), 'Oturum kapatıldı.', () => { this.sessions.update(items => items.map(item => item.id === value.id ? { ...item, revokedAt: new Date().toISOString() } : item)); if (value.isCurrent) this.currentSessionRevoked.emit(); }); }
  protected createApiKey(): void { if (!this.apiKeyDraft.name.trim() || !this.apiKeyDraft.password) return; const expires = new Date(); expires.setDate(expires.getDate() + boundedExpiryDays(this.apiKeyDraft.expiresInDays)); this.mutate('api-key', this.api.createApiKey({ name: this.apiKeyDraft.name.trim(), password: this.apiKeyDraft.password, mfaCode: this.apiKeyDraft.mfaCode || null, expiresAt: expires.toISOString(), scopes: ['api:full'] }), 'API anahtarı oluşturuldu. Tam değeri şimdi güvenli bir yerde saklayın.', value => { this.createdApiKey.set(value); this.apiKeys.update(items => [value, ...items.filter(item => item.id !== value.id)]); this.apiKeyDraft = { name: '', password: '', mfaCode: '', expiresInDays: 90 }; }); }
  protected revokeApiKey(value: ApiKeySummary): void { if (!confirm(`${value.name} anahtarı kalıcı olarak iptal edilsin mi?`)) return; this.mutate(`key:${value.id}`, this.api.revokeApiKey(value.id), 'API anahtarı iptal edildi.', () => { this.apiKeys.update(items => items.map(item => item.id === value.id ? { ...item, revokedAt: new Date().toISOString() } : item)); this.createdApiKey.set(null); }); }
  protected dismissApiKey(): void { this.createdApiKey.set(null); }
  protected copy(value: string): void { if (!value || !navigator.clipboard) return; void navigator.clipboard.writeText(value).then(() => this.notice.set('Değer panoya kopyalandı.')); }
  protected savePreferences(): void { const value = { ...this.preferences(), mutedTypes: normalizeMutedTypes(this.mutedTypesText) }; this.mutate('preferences', this.api.savePreferences(value), 'Bildirim tercihleri kaydedildi.', saved => { this.preferences.set(saved); this.mutedTypesText = saved.mutedTypes.join(', '); }); }

  protected exportPrivacy(): void { this.busy.set('privacy-export'); this.api.exportPrivacyData().pipe(finalize(() => this.busy.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: blob => { const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = 'zumbo-privacy-export.ndjson'; link.click(); URL.revokeObjectURL(url); this.notice.set('Kişisel veri aktarımı indirildi.'); }, error: () => this.error.set('Kişisel veriler aktarılamadı.') }); }
  protected anonymize(): void { if (!this.privacyDraft.password || this.privacyDraft.confirmation !== 'ANONYMIZE' || !confirm('Hesap erişimi kalıcı olarak kapanacak ve kişisel referanslar anonimleştirilecek. Devam edilsin mi?')) return; this.mutate('privacy', this.api.anonymize(this.privacyDraft.password), 'Anonimleştirme işi sıraya alındı.', receipt => { this.privacyJob.set(receipt.job); sessionStorage.setItem(this.privacyKey(), receipt.job.id); this.privacyDraft = { password: '', confirmation: '' }; }); }
  protected refreshPrivacy(): void { const job = this.privacyJob(); if (!job) return; this.mutate('privacy-refresh', this.api.privacyJob(job.id), 'İş durumu yenilendi.', value => this.privacyJob.set(value)); }
  protected retryPrivacy(): void { const job = this.privacyJob(); if (!job) return; this.mutate('privacy-retry', this.api.retryPrivacyJob(job.id), 'İş yeniden sıraya alındı.', value => this.privacyJob.set(value)); }
  protected reconcilePrivacy(): void { const job = this.privacyJob(); if (!job || !confirm('Sunucu mevcut checkpoint durumunu yeniden değerlendirsin mi?')) return; this.mutate('privacy-reconcile', this.api.reconcilePrivacyJob(job.id), 'Uzlaştırma başlatıldı.', value => this.privacyJob.set(value)); }
  protected dismissPrivacy(): void { sessionStorage.removeItem(this.privacyKey()); this.privacyJob.set(null); }
  protected privacyTerminal(): boolean { return ['Completed', 'Expired', 'Canceled'].includes(this.privacyJob()?.state || ''); }
  protected privacyFailed(): boolean { return this.privacyJob()?.state === 'Failed'; }

  private restorePrivacyJob(): void { const id = sessionStorage.getItem(this.privacyKey()); if (!id) return; this.api.privacyJob(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => this.privacyJob.set(value), error: () => sessionStorage.removeItem(this.privacyKey()) }); }
  private privacyKey(): string { return `${PRIVACY_KEY_PREFIX}${this.context().organizationId}.${this.context().id}`; }
  private clearOneTimeSecrets(): void { this.mfaSetup.set(null); this.recoveryCodes.set([]); this.createdApiKey.set(null); this.passwordDraft = { currentPassword: '', newPassword: '' }; this.mfaDraft = { password: '', code: '' }; this.recoveryDraft = { password: '', code: '' }; this.apiKeyDraft.password = ''; this.apiKeyDraft.mfaCode = ''; this.privacyDraft.password = ''; }
  private mutate<T>(key: string, request: import('rxjs').Observable<T>, success: string, accept: (value: T) => void): void { if (this.busy()) return; this.busy.set(key); this.error.set(null); this.notice.set(null); request.pipe(finalize(() => this.busy.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { accept(value); this.notice.set(success); }, error: (value: { message?: string }) => this.error.set(value?.message || 'İşlem tamamlanamadı.') }); }
}
