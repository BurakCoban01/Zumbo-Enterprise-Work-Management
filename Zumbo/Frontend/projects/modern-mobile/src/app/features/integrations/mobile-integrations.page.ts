import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, NavigationStart, Router } from '@angular/router';
import { IonBackButton, IonButtons, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { ZumboSessionService, normalizeApiError } from '@zumbo/modern-shared';
import { filter, finalize, switchMap } from 'rxjs';
import { developmentRequest, hasMobileIntegrationPermission, mobileDeliveryLabel, mobileHealthLabel, mobileIntegrationError, mobileMappingRequest, mobileSafeDeliveryError, mobileSafeUrlLabel, mobileWebhookDraftFrom, mobileWebhookScopeLabel, mobileWebhookScopes, newMobileDevelopmentDraft, newMobileWebhookDraft, validateMobileDevelopmentDraft, validateMobileWebhookDraft, webhookRequest } from './mobile-integrations.core';
import { MobileDevelopmentConnection, MobileDevelopmentMapping, MobileDevelopmentRepository, MobileIntegrationsTab, MobileIntegrationsView, MobileSecretReceipt, MobileWebhook, MobileWebhookDelivery, MobileWebhookMetrics, MobileWebhookReceipt } from './mobile-integrations.models';
import { MobileIntegrationsService } from './mobile-integrations.service';
import { MobileConnectivityService } from '../../shell/mobile-connectivity.service';

@Component({
  selector: 'zumbo-mobile-integrations',
  imports: [CommonModule, FormsModule, IonBackButton, IonButtons, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar],
  providers: [MobileIntegrationsService],
  templateUrl: './mobile-integrations.page.html',
  styleUrls: ['./mobile-integrations.page.scss', './mobile-integrations-responsive.scss']
})
export class MobileIntegrationsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly session = inject(ZumboSessionService);
  private readonly service = inject(MobileIntegrationsService);
  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly tab = signal<MobileIntegrationsTab>('webhooks');
  protected readonly view = signal<MobileIntegrationsView>('list');
  protected readonly roles = signal<readonly import('./mobile-integrations.models').MobileIntegrationRole[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly webhooks = signal<readonly MobileWebhook[]>([]);
  protected readonly metrics = signal<MobileWebhookMetrics | null>(null);
  protected readonly selectedWebhook = signal<MobileWebhook | null>(null);
  protected readonly deliveries = signal<readonly MobileWebhookDelivery[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly development = signal<readonly MobileDevelopmentConnection[]>([]);
  protected readonly selectedDevelopment = signal<MobileDevelopmentConnection | null>(null);
  protected readonly projects = signal<readonly import('./mobile-integrations.models').MobileIntegrationProject[]>([]);
  protected readonly mappings = signal<readonly MobileDevelopmentMapping[]>([]);
  protected readonly repositories = signal<readonly MobileDevelopmentRepository[]>([]);
  protected readonly repositoryStatus = signal<string | null>(null);
  protected readonly secret = signal<MobileSecretReceipt | null>(null);
  protected readonly scopes = mobileWebhookScopes;
  protected readonly canManage = computed(() => hasMobileIntegrationPermission(this.roles(), this.session.currentUser()?.roles ?? [], 'IntegrationManage'));
  protected readonly mutationLocked = computed(() => this.busy() !== null || this.connectivity.offline() || !this.canManage());
  protected webhookDraft = newMobileWebhookDraft();
  protected developmentDraft = newMobileDevelopmentDraft();
  protected mappingDraft = { projectId: '', repositoryId: '' };
  protected credentialDraft = '';

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const tab: MobileIntegrationsTab = params.get('tab') === 'development' ? 'development' : 'webhooks';
      if (tab === this.tab()) return;
      this.clearSensitive(); this.tab.set(tab); this.view.set('list'); this.error.set(null); this.loadSurface();
    });
    this.router.events.pipe(filter(event => event instanceof NavigationStart), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.clearSensitive());
    this.destroyRef.onDestroy(() => this.clearSensitive());
    this.load();
  }

  ionViewWillLeave(): void { this.clearSensitive(); }
  protected refresh(event: Event): void { this.load(() => void (event.target as HTMLIonRefresherElement).complete()); }
  protected reload(): void { this.load(); }
  protected selectTab(tab: MobileIntegrationsTab): void { if (tab === this.tab()) return; void this.router.navigate([], { relativeTo: this.route, queryParams: { tab }, replaceUrl: true }); }
  protected dismissError(): void { this.error.set(null); }
  protected dismissNotice(): void { this.notice.set(null); }
  protected scopeLabel(value: string | null | undefined): string { return mobileWebhookScopeLabel(value); }
  protected deliveryLabel(value: string): string { return mobileDeliveryLabel(value); }
  protected healthLabel(value: MobileDevelopmentConnection): string { return mobileHealthLabel(value.healthStatus, value.isConnected); }
  protected safeUrl(value: string): string { return mobileSafeUrlLabel(value); }
  protected deliveryError(value: string | null | undefined): string { return mobileSafeDeliveryError(value); }
  protected selectWebhook(value: MobileWebhook): void { this.clearSensitive(); this.selectedWebhook.set(value); this.view.set('webhook-detail'); this.error.set(null); this.loadDeliveries(true); }
  protected newWebhook(): void { this.clearSensitive(); this.selectedWebhook.set(null); this.webhookDraft = newMobileWebhookDraft(); this.view.set('webhook-editor'); this.error.set(null); }
  protected editWebhook(): void { const selected = this.selectedWebhook(); if (!selected) return; this.clearSensitive(); this.webhookDraft = mobileWebhookDraftFrom(selected); this.view.set('webhook-editor'); this.error.set(null); }
  protected closeEditor(): void { this.webhookDraft = newMobileWebhookDraft(); this.view.set(this.selectedWebhook() ? 'webhook-detail' : 'list'); }
  protected toggleScope(value: string): void { const index = this.webhookDraft.eventScopes.indexOf(value); if (index >= 0) this.webhookDraft.eventScopes.splice(index, 1); else this.webhookDraft.eventScopes.push(value); }
  protected scopeSelected(value: string): boolean { return this.webhookDraft.eventScopes.includes(value); }
  protected saveWebhook(): void {
    const validation = validateMobileWebhookDraft(this.webhookDraft), selected = this.selectedWebhook();
    if (validation) { this.error.set(validation); return; }
    if (this.mutationLocked()) return;
    this.mutate('webhook', this.service.saveWebhook(webhookRequest(this.webhookDraft), selected), selected ? 'Webhook güncellendi.' : 'Webhook oluşturuldu.', result => {
      const receipt = result as MobileWebhookReceipt, subscription = receipt.subscription ?? result as MobileWebhook;
      this.upsertWebhook(subscription); this.selectedWebhook.set(subscription); this.view.set('webhook-detail');
      if (receipt.secret) this.secret.set({ secret: receipt.secret, fingerprint: subscription.secretFingerprint, version: subscription.secretVersion });
      this.loadWebhookOperationalState();
    });
  }
  protected rotateWebhook(): void { const selected = this.selectedWebhook(); if (!selected || this.mutationLocked() || !window.confirm('Mevcut sır geçiş süresinden sonra geçersiz olacak. Yeni sır oluşturulsun mu?')) return; this.clearSensitive(); this.mutate('webhook-secret', this.service.rotateWebhook(selected), 'Yeni webhook sırrı oluşturuldu.', receipt => { this.upsertWebhook(receipt.subscription); this.selectedWebhook.set(receipt.subscription); if (receipt.secret) this.secret.set({ secret: receipt.secret, fingerprint: receipt.subscription.secretFingerprint, version: receipt.subscription.secretVersion }); }); }
  protected setWebhookActive(): void { const selected = this.selectedWebhook(), active = !selected?.isActive; if (!selected || this.mutationLocked() || (!active && !window.confirm('Bu webhook için yeni teslimatlar durdurulsun mu?'))) return; this.clearSensitive(); this.mutate('webhook-active', this.service.setWebhookActive(selected, active), active ? 'Webhook etkinleştirildi.' : 'Webhook durduruldu.', value => { this.upsertWebhook(value); this.selectedWebhook.set(value); }); }
  protected testWebhook(): void { const selected = this.selectedWebhook(); if (!selected || !selected.isActive || this.mutationLocked()) return; this.mutate('webhook-test', this.service.testWebhook(selected.id), 'Test teslimatı sıraya alındı.', delivery => { this.deliveries.update(items => [delivery, ...items].slice(0, 30)); this.service.metrics().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: metrics => this.metrics.set(metrics) }); }); }
  protected replay(delivery: MobileWebhookDelivery): void { if (delivery.status !== 'DeadLetter' || this.mutationLocked() || !window.confirm('Teslimat yeniden sıraya alınsın mı?')) return; this.mutate(`delivery-${delivery.id}`, this.service.replayDelivery(delivery.id), 'Teslimat yeniden sıraya alındı.', updated => { this.deliveries.update(items => items.map(item => item.id === updated.id ? updated : item)); this.service.metrics().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: metrics => this.metrics.set(metrics) }); }); }
  protected loadMoreDeliveries(): void { const selected = this.selectedWebhook(), cursor = this.nextCursor(); if (!selected || !cursor || this.busy() || this.connectivity.offline()) return; this.busy.set('deliveries'); this.service.deliveries(selected.id, cursor).pipe(finalize(() => this.busy.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: page => { this.deliveries.update(items => [...items, ...page.items].slice(0, 90)); this.nextCursor.set(page.nextCursor ?? null); }, error: value => this.error.set(this.errorMessage(value, 'Teslimatlar yüklenemedi.')) }); }
  protected selectDevelopment(value: MobileDevelopmentConnection): void { this.clearSensitive(); this.selectedDevelopment.set(value); this.view.set('development-detail'); this.repositories.set([]); this.repositoryStatus.set(null); this.error.set(null); this.loadMappings(); }
  protected newDevelopment(): void { this.clearSensitive(); this.selectedDevelopment.set(null); this.developmentDraft = newMobileDevelopmentDraft(); this.view.set('development-editor'); this.error.set(null); }
  protected setProvider(value: string): void { const provider = value === 'GitLab' ? 'GitLab' : 'GitHub', previous = this.developmentDraft.provider; this.developmentDraft.provider = provider; if (!this.developmentDraft.baseUrl || this.developmentDraft.baseUrl === (previous === 'GitLab' ? 'https://gitlab.com/api/v4' : 'https://api.github.com')) this.developmentDraft.baseUrl = provider === 'GitLab' ? 'https://gitlab.com/api/v4' : 'https://api.github.com'; }
  protected closeDevelopmentEditor(): void { this.developmentDraft = newMobileDevelopmentDraft(); this.view.set(this.selectedDevelopment() ? 'development-detail' : 'list'); }
  protected createDevelopment(): void { const validation = validateMobileDevelopmentDraft(this.developmentDraft); if (validation) { this.error.set(validation); return; } if (this.mutationLocked()) return; this.mutate('development-create', this.service.createDevelopment(developmentRequest(this.developmentDraft)), 'Sağlayıcı bağlantısı oluşturuldu.', receipt => { this.upsertDevelopment(receipt.connection); this.selectedDevelopment.set(receipt.connection); this.view.set('development-detail'); this.secret.set({ secret: receipt.webhookSecret, fingerprint: receipt.connection.webhookSecretFingerprint, version: receipt.connection.webhookSecretVersion }); this.developmentDraft = newMobileDevelopmentDraft(); this.loadMappings(); }); }
  protected checkHealth(): void { const selected = this.selectedDevelopment(); if (!selected?.isConnected || this.mutationLocked()) return; this.mutate('development-health', this.service.health(selected).pipe(switchMap(() => this.service.developmentConnection(selected.id))), 'Sağlayıcı bağlantısı denetlendi.', value => this.replaceDevelopment(value)); }
  protected discoverRepositories(): void { const selected = this.selectedDevelopment(); if (!selected?.isConnected || this.mutationLocked()) return; this.mutate('development-repositories', this.service.repositories(selected.id), 'Repository listesi yenilendi.', page => { this.repositories.set(page.items); this.repositoryStatus.set(page.sourceStatus ?? 'Tamamlandı'); }); }
  protected createMapping(): void { const selected = this.selectedDevelopment(), repository = this.repositories().find(item => item.externalRepositoryId === this.mappingDraft.repositoryId), request = mobileMappingRequest(this.mappingDraft.projectId, repository); if (!selected || !request) { this.error.set('Bir proje ve repository seçin.'); return; } if (this.mutationLocked()) return; this.mutate('development-mapping', this.service.createMapping(selected.id, request), 'Repository projeye bağlandı.', mapping => { this.mappings.update(items => [...items, mapping]); this.mappingDraft = { projectId: '', repositoryId: '' }; }); }
  protected deleteMapping(mapping: MobileDevelopmentMapping): void { if (this.mutationLocked() || !window.confirm(`${mapping.repositoryFullName} eşlemesi ve ilgili bağlantılar kaldırılsın mı?`)) return; this.mutate(`mapping-${mapping.id}`, this.service.deleteMapping(mapping), 'Repository eşlemesi kaldırıldı.', () => this.mappings.update(items => items.filter(item => item.id !== mapping.id))); }
  protected rotateCredential(): void { const selected = this.selectedDevelopment(), credential = this.credentialDraft.trim(); if (!selected || credential.length < 16 || credential.length > 512 || /\s/.test(credential)) { this.error.set('Erişim anahtarı 16 ile 512 arasında boşluksuz karakter içermelidir.'); return; } if (this.mutationLocked()) return; this.mutate('development-credential', this.service.rotateCredential(selected, credential), 'Erişim anahtarı döndürüldü.', value => { this.replaceDevelopment(value); this.credentialDraft = ''; }); }
  protected rotateDevelopmentSecret(): void { const selected = this.selectedDevelopment(); if (!selected || this.mutationLocked() || !window.confirm('Mevcut webhook sırrı geçiş süresinden sonra geçersiz olacak. Yeni sır oluşturulsun mu?')) return; this.clearSensitive(); this.mutate('development-secret', this.service.rotateDevelopmentSecret(selected), 'Yeni webhook sırrı oluşturuldu.', receipt => { this.replaceDevelopment(receipt.connection); this.secret.set({ secret: receipt.webhookSecret, fingerprint: receipt.connection.webhookSecretFingerprint, version: receipt.connection.webhookSecretVersion }); }); }
  protected disconnectDevelopment(): void { const selected = this.selectedDevelopment(); if (!selected?.isConnected || this.mutationLocked() || !window.confirm('Erişim anahtarı ve webhook sırları kalıcı olarak silinsin mi?')) return; this.clearSensitive(); this.mutate('development-disconnect', this.service.disconnect(selected), 'Bağlantı kesildi.', value => { this.replaceDevelopment(value); this.repositories.set([]); this.repositoryStatus.set(null); this.loadMappings(); }); }
  protected deleteDevelopment(): void { const selected = this.selectedDevelopment(); if (!selected || this.mutationLocked() || !window.confirm(`${selected.name} ve tüm eşlemeleri kalıcı olarak silinsin mi?`)) return; this.clearSensitive(); this.mutate('development-delete', this.service.deleteDevelopment(selected), 'Bağlantı silindi.', () => { this.development.update(items => items.filter(item => item.id !== selected.id)); this.selectedDevelopment.set(null); this.mappings.set([]); this.repositories.set([]); this.view.set('list'); }); }
  protected copySecret(): void { const value = this.secret()?.secret; if (!value || !navigator.clipboard) return; void navigator.clipboard.writeText(value).then(() => this.notice.set('Sır panoya kopyalandı.')); }
  protected dismissSecret(): void { this.clearSensitive(); }

  private load(completed?: () => void): void { this.loading.set(true); this.error.set(null); this.service.roles().pipe(finalize(() => { this.loading.set(false); completed?.(); }), takeUntilDestroyed(this.destroyRef)).subscribe({ next: roles => { this.roles.set(roles); if (this.canManage()) this.loadSurface(); }, error: value => this.error.set(this.errorMessage(value, 'Yetkiler yüklenemedi.')) }); }
  private loadSurface(): void { if (!this.canManage()) return; if (this.tab() === 'webhooks') { this.service.webhookData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { this.webhooks.set(value.webhooks); this.metrics.set(value.metrics); const current = this.selectedWebhook(); const selected = value.webhooks.find(item => item.id === current?.id) ?? null; this.selectedWebhook.set(selected); if (selected && this.view() === 'webhook-detail') this.loadDeliveries(true); }, error: value => this.error.set(this.errorMessage(value, 'Webhook kayıtları yüklenemedi.')) }); return; }
    const organizationId = this.session.currentUser()?.organizationId; if (!organizationId) return;
    this.service.developmentData(organizationId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { this.development.set(value.development); this.projects.set(value.projects); const current = this.selectedDevelopment(); const selected = value.development.find(item => item.id === current?.id) ?? null; this.selectedDevelopment.set(selected); if (selected && this.view() === 'development-detail') this.loadMappings(); }, error: value => this.error.set(this.errorMessage(value, 'Geliştirme bağlantıları yüklenemedi.')) });
  }
  private loadDeliveries(reset: boolean): void { const selected = this.selectedWebhook(); if (!selected) return; if (reset) { this.deliveries.set([]); this.nextCursor.set(null); } this.service.deliveries(selected.id, reset ? null : this.nextCursor()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: page => { this.deliveries.set(reset ? page.items : [...this.deliveries(), ...page.items].slice(0, 90)); this.nextCursor.set(page.nextCursor ?? null); }, error: value => this.error.set(this.errorMessage(value, 'Teslimatlar yüklenemedi.')) }); }
  private loadMappings(): void { const selected = this.selectedDevelopment(); if (!selected) return; this.service.mappings(selected.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: mappings => this.mappings.set(mappings), error: value => this.error.set(this.errorMessage(value, 'Repository eşlemeleri yüklenemedi.')) }); }
  private loadWebhookOperationalState(): void { const selected = this.selectedWebhook(); this.service.metrics().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: metrics => this.metrics.set(metrics) }); if (selected) this.loadDeliveries(true); }
  private upsertWebhook(value: MobileWebhook): void { this.webhooks.update(items => { const exists = items.some(item => item.id === value.id); return exists ? items.map(item => item.id === value.id ? value : item) : [value, ...items]; }); }
  private upsertDevelopment(value: MobileDevelopmentConnection): void { this.development.update(items => { const exists = items.some(item => item.id === value.id); return exists ? items.map(item => item.id === value.id ? value : item) : [value, ...items]; }); }
  private replaceDevelopment(value: MobileDevelopmentConnection): void { this.upsertDevelopment(value); this.selectedDevelopment.set(value); }
  private clearSensitive(): void { this.secret.set(null); this.webhookDraft = newMobileWebhookDraft(); this.developmentDraft.accessToken = ''; this.credentialDraft = ''; }
  private errorMessage(value: unknown, fallback: string): string { return mobileIntegrationError(normalizeApiError(value).code, fallback); }
  private mutate<T>(key: string, request: import('rxjs').Observable<T>, notice: string, accept: (value: T) => void): void { if (this.mutationLocked()) return; this.busy.set(key); this.error.set(null); this.notice.set(null); request.pipe(finalize(() => this.busy.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { accept(value); this.notice.set(notice); }, error: value => { const normalized = normalizeApiError(value); this.error.set(mobileIntegrationError(normalized.code, 'İşlem tamamlanamadı.')); if (normalized.code === 'CONCURRENCY_CONFLICT') this.loadSurface(); } }); }
}
