import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IonBackButton, IonButtons, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { normalizeApiError, ZumboSessionService } from '@zumbo/modern-shared';
import { finalize, Observable } from 'rxjs';
import { hasMobileOperationsPermission, mobileDependencyLabel, mobileDependencyNeedsAttention, mobileDependencyState, mobileMessageLabel, mobileNotificationLabel, mobileOperationsAttentionCount, mobileOperationsErrorMessage, mobileOperationsReadLabel } from './mobile-operations.core';
import { MobileDependencyMetric, MobileOperationsRead, MobileOperationsSnapshot, MobileSearchReconcileResult } from './mobile-operations.models';
import { MobileOperationsService } from './mobile-operations.service';
import { MobileConnectivityService } from '../../shell/mobile-connectivity.service';

@Component({
  selector: 'zumbo-mobile-operations',
  imports: [CommonModule, IonBackButton, IonButtons, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar],
  providers: [MobileOperationsService],
  templateUrl: './mobile-operations.page.html',
  styleUrls: ['./mobile-operations.page.scss', './mobile-operations-states.scss', './mobile-operations-responsive.scss']
})
export class MobileOperationsPage {
  private readonly destroyRef = inject(DestroyRef);
  private readonly session = inject(ZumboSessionService);
  private readonly service = inject(MobileOperationsService);
  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly roles = signal<readonly import('./mobile-operations.models').MobileOperationsRole[]>([]);
  protected readonly data = signal<MobileOperationsSnapshot | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly searchResult = signal<MobileSearchReconcileResult | null>(null);
  protected readonly canManage = computed(() => hasMobileOperationsPermission(this.roles(), this.session.currentUser()?.roles ?? [], 'OperationsManage'));
  protected readonly mutationLocked = computed(() => this.busy() !== null || this.connectivity.offline() || !this.canManage());

  constructor() { this.load(); }

  protected refresh(event: Event): void { this.load(() => void (event.target as HTMLIonRefresherElement).complete()); }
  protected reload(): void { this.load(); }
  protected dismissError(): void { this.error.set(null); }
  protected dismissNotice(): void { this.notice.set(null); }
  protected readLabel(read: MobileOperationsRead): string { return mobileOperationsReadLabel(read); }
  protected readFailed(read: MobileOperationsRead): boolean { return this.data()?.failures.includes(read) ?? false; }
  protected dependencyNeedsAttention(value: MobileDependencyMetric): boolean { return mobileDependencyNeedsAttention(value); }
  protected dependencyState(value: MobileDependencyMetric): string { return mobileDependencyState(value); }
  protected dependencyLabel(value: string): string { return mobileDependencyLabel(value); }
  protected messageLabel(value: string | null | undefined): string { return mobileMessageLabel(value); }
  protected notificationLabel(value: string | null | undefined): string { return mobileNotificationLabel(value); }
  protected attentionCount(value: MobileOperationsSnapshot): number { return mobileOperationsAttentionCount(value); }

  protected reconcile(): void {
    if (this.mutationLocked() || !window.confirm('Arama görünümü güncel kayıtlarla uzlaştırılsın mı?')) return;
    this.mutate('search', this.service.reconcile(), 'Arama görünümü uzlaştırıldı.', result => this.searchResult.set(result));
  }

  protected replayMessage(id: string): void {
    if (this.mutationLocked() || !window.confirm('Seçilen sistem olayı yeniden sıraya alınsın mı?')) return;
    this.mutate(`message-${id}`, this.service.replayMessage(id), 'Sistem olayı yeniden sıraya alındı.', () => this.loadSnapshot());
  }

  protected replayNotification(id: string): void {
    const organizationId = this.session.currentUser()?.organizationId;
    if (!organizationId || this.mutationLocked() || !window.confirm('Seçilen bildirim yeniden sıraya alınsın mı?')) return;
    this.mutate(`notification-${id}`, this.service.replayNotification(id, organizationId), 'Bildirim yeniden sıraya alındı.', () => this.loadSnapshot());
  }

  protected maintainStorage(): void {
    const organizationId = this.session.currentUser()?.organizationId;
    if (!organizationId || this.mutationLocked() || !window.confirm('Karantina kayıtları yeniden denetlensin mi?')) return;
    this.mutate('storage', this.service.maintainStorage(organizationId), 'Dosya güvenliği bakımı tamamlandı.', () => this.loadSnapshot());
  }

  private load(completed?: () => void): void {
    if (!this.session.currentUser()) {
      this.loading.set(false);
      this.data.set(null);
      this.error.set('Operasyon durumunu görüntülemek için oturum açın.');
      completed?.();
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.service.roles().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: roles => {
        this.roles.set(roles);
        if (this.canManage()) this.loadSnapshot(completed);
        else {
          this.data.set(null);
          this.loading.set(false);
          completed?.();
        }
      },
      error: value => {
        this.loading.set(false);
        this.error.set(this.errorMessage(value, 'Yetkiler yüklenemedi.'));
        completed?.();
      }
    });
  }

  private loadSnapshot(completed?: () => void): void {
    const organizationId = this.session.currentUser()?.organizationId;
    if (!organizationId || !this.canManage()) {
      this.loading.set(false);
      completed?.();
      return;
    }
    this.loading.set(true);
    this.service.load(organizationId).pipe(finalize(() => { this.loading.set(false); completed?.(); }), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: snapshot => {
        this.data.set(snapshot);
        this.error.set(snapshot.failures.length ? `${snapshot.failures.map(read => this.readLabel(read)).join(', ')} alınamadı; kullanılabilen durum gösteriliyor.` : null);
      },
      error: value => this.error.set(this.errorMessage(value, 'Operasyon durumu yüklenemedi.'))
    });
  }

  private mutate<T>(key: string, request: Observable<T>, message: string, accept: (value: T) => void): void {
    if (this.mutationLocked()) return;
    this.busy.set(key);
    this.error.set(null);
    request.pipe(finalize(() => this.busy.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: value => { accept(value); this.notice.set(message); },
      error: value => this.error.set(this.errorMessage(value, 'İşlem tamamlanamadı.'))
    });
  }

  private errorMessage(value: unknown, fallback: string): string {
    const error = normalizeApiError(value);
    return mobileOperationsErrorMessage(error.code, fallback);
  }
}
