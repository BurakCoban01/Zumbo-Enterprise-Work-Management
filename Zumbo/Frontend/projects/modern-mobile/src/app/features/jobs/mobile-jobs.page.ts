import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { IonBackButton, IonButtons, IonContent, IonHeader, IonProgressBar, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { ZumboSessionService } from '@zumbo/modern-shared';
import { finalize, Observable } from 'rxjs';
import { MobileConnectivityService } from '../../shell/mobile-connectivity.service';
import { canCancelMobileJob, canRetryMobileJob, hasMobileJobPermission, isMobileJobTerminal, mobileJobArtifactsExpired, mobileJobIsActive, mobileJobLimits, mobileJobProgress, mobileJobState, mobileJobType, parseMobileImport } from './mobile-jobs.core';
import { MobileBulkJob, MobileBulkJobPage, MobileJobRole, MobileJobsProject, MobileParsedImport } from './mobile-jobs.models';
import { MobileJobsService } from './mobile-jobs.service';

@Component({
  selector: 'zumbo-mobile-jobs',
  imports: [CommonModule, IonBackButton, IonButtons, IonContent, IonHeader, IonProgressBar, IonRefresher, IonRefresherContent, IonSpinner, IonTitle, IonToolbar],
  providers: [MobileJobsService],
  templateUrl: './mobile-jobs.page.html',
  styleUrls: ['./mobile-jobs.page.scss', './mobile-jobs-responsive.scss']
})
export class MobileJobsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(MobileJobsService);
  private readonly session = inject(ZumboSessionService);
  private readonly destroyRef = inject(DestroyRef);
  private pollHandle?: ReturnType<typeof setTimeout>;

  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly projectId = signal('');
  protected readonly project = signal<MobileJobsProject | null>(null);
  protected readonly roles = signal<readonly MobileJobRole[]>([]);
  protected readonly jobs = signal<readonly MobileBulkJob[]>([]);
  protected readonly total = signal(0);
  protected readonly selectedId = signal('');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly showStartControls = signal(false);
  protected readonly importFile = signal<File | null>(null);
  protected readonly parsed = signal<MobileParsedImport | null>(null);
  protected readonly includeArchived = signal(false);
  protected readonly limits = mobileJobLimits;
  protected readonly selected = computed(() => this.jobs().find(job => job.id === this.selectedId()) ?? null);
  protected readonly roleName = computed(() => this.project()?.members?.find(member => member.userId === this.session.currentUser()?.id)?.role ?? null);
  protected readonly canImport = computed(() => hasMobileJobPermission(this.roleName(), this.roles(), 'WorkItemCreate'));
  protected readonly canExport = computed(() => hasMobileJobPermission(this.roleName(), this.roles(), 'WorkItemView'));
  protected readonly canManage = computed(() => hasMobileJobPermission(this.roleName(), this.roles(), 'WorkItemUpdate'));
  protected readonly activeCount = computed(() => this.jobs().filter(mobileJobIsActive).length);
  protected readonly failedCount = computed(() => this.jobs().filter(job => ['Failed', 'CompletedWithErrors'].includes(job.state)).length);
  protected readonly mutationLocked = computed(() => this.busy() || this.connectivity.offline());

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const id = params.get('projectId') ?? '';
      if (id === this.projectId()) return;
      this.projectId.set(id);
      this.project.set(null);
      this.jobs.set([]);
      this.selectedId.set('');
      this.load();
    });
  }

  ngOnDestroy(): void { this.stopPolling(); }

  protected refresh(event: Event): void { this.load(() => void (event.target as unknown as { complete(): Promise<void> }).complete()); }
  protected reload(): void { this.load(); }
  protected select(job: MobileBulkJob): void { this.selectedId.set(job.id); this.error.set(null); }
  protected toggleStartControls(): void { this.showStartControls.update(value => !value); }
  protected dismissError(): void { this.error.set(null); }
  protected dismissNotice(): void { this.notice.set(null); }
  protected updateIncludeArchived(event: Event): void { this.includeArchived.set((event.target as HTMLInputElement).checked); }

  protected chooseFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.importFile.set(file);
    this.parsed.set(null);
    this.error.set(null);
    if (!file) return;
    if (!/\.json$/i.test(file.name)) { this.error.set('İçe aktarım dosyası .json uzantılı olmalı.'); return; }
    const reader = new FileReader();
    reader.onload = () => {
      const parsed = parseMobileImport(String(reader.result ?? ''), file.size);
      this.parsed.set(parsed);
      if (!parsed.valid) this.error.set(parsed.errors[0]);
    };
    reader.onerror = () => this.error.set('Dosya okunamadı.');
    reader.readAsText(file);
  }

  protected submitImport(dryRun: boolean): void {
    const parsed = this.parsed();
    if (this.mutationLocked() || !this.canImport() || !parsed?.valid) return;
    this.mutate(this.service.import(this.projectId(), parsed.rows, dryRun), dryRun ? 'İçe aktarım önizlemesi sıraya alındı.' : 'İçe aktarım sıraya alındı.');
  }
  protected submitExport(dryRun: boolean): void {
    if (this.mutationLocked() || !this.canExport()) return;
    this.mutate(this.service.export(this.projectId(), this.includeArchived(), dryRun), dryRun ? 'Dışa aktarım önizlemesi sıraya alındı.' : 'Dışa aktarım sıraya alındı.');
  }
  protected requestCancel(job: MobileBulkJob): void {
    if (this.mutationLocked() || !this.canManage() || !canCancelMobileJob(job) || !window.confirm('Çalışan işi iptal etmek istiyor musunuz?')) return;
    this.mutate(this.service.cancel(job), 'İptal isteği kaydedildi.');
  }
  protected retry(job: MobileBulkJob): void {
    if (this.mutationLocked() || !this.canManage() || !canRetryMobileJob(job)) return;
    this.mutate(this.service.retry(job), 'Başarısız satırlar yeniden sıraya alındı.');
  }
  protected download(job: MobileBulkJob, errors: boolean): void {
    if (this.mutationLocked() || mobileJobArtifactsExpired(job) || (errors ? !job.hasErrorFile : !job.hasResult)) return;
    this.busy.set(true);
    this.error.set(null);
    this.service.artifact(job, errors).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `zumbo-${job.type.toLowerCase()}-${job.id.slice(0, 8)}${errors ? '-errors' : '-result'}.ndjson`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: error => this.error.set(error?.message ?? 'İş dosyası indirilemedi.')
    });
  }

  protected state(job: MobileBulkJob) { return mobileJobState(job); }
  protected type(job: MobileBulkJob): string { return mobileJobType(job); }
  protected progress(job: MobileBulkJob): number { return mobileJobProgress(job); }
  protected canCancelJob(job: MobileBulkJob): boolean { return canCancelMobileJob(job); }
  protected canRetryJob(job: MobileBulkJob): boolean { return canRetryMobileJob(job); }
  protected expired(job: MobileBulkJob): boolean { return mobileJobArtifactsExpired(job); }

  private load(completed?: () => void): void {
    this.stopPolling();
    if (!this.projectId()) { this.loading.set(false); completed?.(); return; }
    this.loading.set(true);
    this.error.set(null);
    this.service.load(this.projectId()).pipe(finalize(() => { this.loading.set(false); completed?.(); }), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => this.applyPage(data.page, data.project, data.roles),
      error: error => this.error.set(error?.message ?? 'İş merkezi yüklenemedi.')
    });
  }
  private refreshJobs(): void {
    this.stopPolling();
    this.service.list(this.projectId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => this.applyPage(page),
      error: error => this.error.set(error?.message ?? 'İş geçmişi güncellenemedi.')
    });
  }
  private applyPage(page: MobileBulkJobPage, project?: MobileJobsProject, roles?: readonly MobileJobRole[]): void {
    if (project) this.project.set(project);
    if (roles) this.roles.set(roles);
    this.jobs.set(page.items);
    this.total.set(page.totalCount);
    const selected = this.selectedId();
    this.selectedId.set(page.items.some(job => job.id === selected) ? selected : (page.items[0]?.id ?? ''));
    this.schedulePolling();
  }
  private mutate(request: Observable<MobileBulkJob>, message: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: job => { this.selectedId.set(job.id); this.showStartControls.set(false); this.notice.set(message); this.refreshJobs(); },
      error: error => this.error.set(error?.message ?? 'İş merkezi işlemi tamamlanamadı.')
    });
  }
  private schedulePolling(): void {
    if (!this.jobs().some(job => !isMobileJobTerminal(job))) return;
    this.pollHandle = setTimeout(() => this.refreshJobs(), 2750);
  }
  private stopPolling(): void { if (this.pollHandle) clearTimeout(this.pollHandle); this.pollHandle = undefined; }
}
