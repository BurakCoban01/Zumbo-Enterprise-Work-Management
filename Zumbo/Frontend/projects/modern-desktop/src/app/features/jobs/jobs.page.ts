import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnDestroy, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { artifactsExpired, canCancel, canRetry, hasJobPermission, isTerminal, jobLimits, jobState, jobType, parseImport, progress } from './jobs.core';
import { BulkJob, JobRole, ParsedImport } from './jobs.models';
import { JobsService } from './jobs.service';

@Component({ selector: 'zumbo-jobs-page', imports: [CommonModule, ZumboIconComponent], providers: [JobsService], templateUrl: './jobs.page.html', styleUrls: ['./jobs.page.scss', './jobs-layout.scss', './jobs-responsive.scss', './jobs-theme.scss'] })
export class JobsPage implements OnDestroy {
  readonly project = input.required<ProjectSummary>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();
  private readonly api = inject(JobsService);
  private readonly destroyRef = inject(DestroyRef);
  private projectId = '';
  private pollHandle?: ReturnType<typeof setTimeout>;

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly jobs = signal<readonly BulkJob[]>([]);
  protected readonly total = signal(0);
  protected readonly selectedId = signal('');
  protected readonly roles = signal<readonly JobRole[]>([]);
  protected readonly importFile = signal<File | null>(null);
  protected readonly parsed = signal<ParsedImport | null>(null);
  protected readonly includeArchived = signal(false);
  protected readonly limits = jobLimits;
  protected readonly roleName = computed(() => this.project().members?.find(member => member.userId === this.userId())?.role ?? null);
  protected readonly canImport = computed(() => this.hasPermission('WorkItemCreate'));
  protected readonly canExport = computed(() => this.hasPermission('WorkItemView'));
  protected readonly canManage = computed(() => this.hasPermission('WorkItemUpdate'));
  protected readonly selected = computed(() => this.jobs().find(job => job.id === this.selectedId()) ?? null);
  protected readonly activeCount = computed(() => this.jobs().filter(job => !isTerminal(job) && job.state !== 'Failed').length);
  protected readonly failedCount = computed(() => this.jobs().filter(job => ['Failed', 'CompletedWithErrors'].includes(job.state)).length);

  constructor() { effect(() => { const id = this.project().id; if (!this.contextReady() || id === this.projectId) return; this.projectId = id; this.load(); }); }
  ngOnDestroy(): void { if (this.pollHandle) clearTimeout(this.pollHandle); }

  protected load(quiet = false): void { if (this.pollHandle) clearTimeout(this.pollHandle); if (!quiet) this.loading.set(true); this.error.set(null); forkJoin({ page: this.api.list(this.project().id), roles: this.roles().length ? [this.roles()] : this.api.roles() }).pipe(finalize(() => { if (!quiet) this.loading.set(false); }), takeUntilDestroyed(this.destroyRef)).subscribe({ next: ({ page, roles }) => { this.jobs.set(page.items); this.total.set(page.totalCount); this.roles.set(roles); if (this.selectedId() && !page.items.some(job => job.id === this.selectedId())) this.selectedId.set(''); this.schedulePoll(); }, error: error => { if (!quiet) this.error.set(error?.message ?? 'İş merkezi yüklenemedi.'); } }); }
  protected select(job: BulkJob): void { this.selectedId.set(job.id); this.error.set(null); }
  protected chooseFile(event: Event): void { const file = (event.target as HTMLInputElement).files?.[0] ?? null; this.importFile.set(file); this.parsed.set(null); this.error.set(null); if (!file) return; if (!/\.json$/i.test(file.name)) { this.error.set('İçe aktarım dosyası .json uzantılı olmalı.'); return; } const reader = new FileReader(); reader.onload = () => { const result = parseImport(String(reader.result ?? ''), file.size); this.parsed.set(result); if (!result.valid) this.error.set(result.errors[0]); }; reader.onerror = () => this.error.set('Dosya okunamadı.'); reader.readAsText(file); }
  protected submitImport(dryRun: boolean): void { const parsed = this.parsed(); if (!this.canImport() || !parsed?.valid) return; this.mutate(this.api.import(this.project().id, parsed.rows, dryRun), dryRun ? 'İçe aktarım önizlemesi sıraya alındı.' : 'İçe aktarım sıraya alındı.'); }
  protected submitExport(dryRun: boolean): void { if (!this.canExport()) return; this.mutate(this.api.export(this.project().id, this.includeArchived(), dryRun), dryRun ? 'Dışa aktarım önizlemesi sıraya alındı.' : 'Dışa aktarım sıraya alındı.'); }
  protected requestCancel(job: BulkJob): void { if (!this.canManage() || !canCancel(job) || !window.confirm('Çalışan işi iptal etmek istiyor musunuz?')) return; this.mutate(this.api.cancel(job), 'İptal isteği kaydedildi.'); }
  protected retry(job: BulkJob): void { if (!this.canManage() || !canRetry(job)) return; this.mutate(this.api.retry(job), 'Başarısız satırlar yeniden sıraya alındı.'); }
  protected download(job: BulkJob, errors: boolean): void { if (artifactsExpired(job) || (errors ? !job.hasErrorFile : !job.hasResult)) return; this.api.artifact(job, errors).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: blob => { const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = `zumbo-${job.type.toLowerCase()}-${job.id.slice(0, 8)}${errors ? '-errors' : '-result'}.ndjson`; link.click(); URL.revokeObjectURL(url); }, error: error => this.error.set(error?.message ?? 'İş dosyası indirilemedi.') }); }
  protected state(job: BulkJob) { return jobState(job); }
  protected type(job: BulkJob): string { return jobType(job); }
  protected progress(job: BulkJob): number { return progress(job); }
  protected canCancelJob(job: BulkJob): boolean { return canCancel(job); }
  protected canRetryJob(job: BulkJob): boolean { return canRetry(job); }
  protected expired(job: BulkJob): boolean { return artifactsExpired(job); }
  private hasPermission(permission: string): boolean { return hasJobPermission(this.roleName(), this.roles(), permission); }
  private mutate(request: import('rxjs').Observable<BulkJob>, message: string): void { if (this.busy()) return; this.busy.set(true); this.error.set(null); this.notice.set(null); request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: job => { this.selectedId.set(job.id); this.notice.set(message); this.load(true); }, error: error => this.error.set(error?.message ?? 'İş merkezi işlemi tamamlanamadı.') }); }
  private schedulePoll(): void { if (!this.activeCount()) return; this.pollHandle = setTimeout(() => this.load(true), 2500); }
}
