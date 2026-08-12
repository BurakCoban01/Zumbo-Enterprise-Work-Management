import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { auditActionLabel, auditEntityLabel, auditFieldLabel, auditQuery, defaultAuditFilters, hasAuditPermission, integrityState, safeAuditChanges, shortId, userName } from './audit.core';
import { AuditEntry, AuditFilters, AuditIntegrity, AuditProject, AuditRole, AuditUser, AuditUserContext } from './audit.models';
import { AuditService } from './audit.service';

@Component({
  selector: 'zumbo-audit-page',
  imports: [CommonModule, FormsModule, ZumboIconComponent],
  providers: [AuditService],
  templateUrl: './audit.page.html',
  styleUrls: ['./audit.page.scss', './audit-layout.scss', './audit-responsive.scss', './audit-theme.scss']
})
export class AuditPage implements OnInit {
  readonly context = input.required<AuditUserContext>();
  readonly projects = input<readonly AuditProject[]>([]);
  private readonly api = inject(AuditService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly capabilityLoading = signal(true);
  protected readonly loading = signal(false);
  protected readonly loadingMore = signal(false);
  protected readonly exporting = signal(false);
  protected readonly integrityLoading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly roles = signal<readonly AuditRole[]>([]);
  protected readonly users = signal<readonly AuditUser[]>([]);
  protected readonly entries = signal<readonly AuditEntry[]>([]);
  protected readonly selected = signal<AuditEntry | null>(null);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly integrity = signal<AuditIntegrity | null>(null);
  protected filters: AuditFilters = defaultAuditFilters();

  protected readonly allowed = computed(() => hasAuditPermission(this.roles(), this.context(), 'AuditReadAll'));
  protected readonly selectedChanges = computed(() => safeAuditChanges(this.selected()));
  protected readonly integrityStatus = computed(() => integrityState(this.integrity()));
  protected readonly organizationUsers = computed(() => this.users().filter(user => user.organizationId === this.context().organizationId));
  protected readonly actionLabel = auditActionLabel;
  protected readonly fieldLabel = auditFieldLabel;
  protected readonly referenceLabel = shortId;

  ngOnInit(): void {
    this.api.roles().pipe(finalize(() => this.capabilityLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: roles => { this.roles.set(roles); if (this.allowed()) this.loadInitial(); },
      error: error => this.fail(error, 'Denetim yetkileri yüklenemedi.')
    });
  }

  protected search(reset = true): void {
    if (!this.allowed() || this.loading() || this.loadingMore()) return;
    let query: string;
    try { query = auditQuery(this.filters, this.context().organizationId, reset ? null : this.nextCursor()); }
    catch (error) { this.fail(error, 'Filtreler doğrulanamadı.'); return; }
    reset ? this.loading.set(true) : this.loadingMore.set(true);
    this.error.set(null);
    this.api.search(query).pipe(finalize(() => { this.loading.set(false); this.loadingMore.set(false); }), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => {
        const items = reset ? page.items : [...this.entries(), ...page.items];
        this.entries.set(items);
        this.nextCursor.set(page.nextCursor ?? null);
        if (reset || !this.selected()) this.selected.set(items[0] ?? null);
      },
      error: error => this.fail(error, 'Denetim kayıtları yüklenemedi.')
    });
  }

  protected reset(): void { this.filters = defaultAuditFilters(); this.search(); }
  protected select(entry: AuditEntry): void { this.selected.set(entry); }
  protected entityLabel(entry: AuditEntry): string { return auditEntityLabel(entry, this.projects(), this.users()); }
  protected actorName(actorUserId: string): string { return userName(actorUserId, this.users()); }

  protected verifyIntegrity(): void {
    if (!this.allowed() || this.integrityLoading()) return;
    this.integrityLoading.set(true);
    this.error.set(null);
    this.api.integrity(this.context().organizationId).pipe(finalize(() => this.integrityLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => this.integrity.set(result),
      error: error => this.fail(error, 'Denetim bütünlüğü doğrulanamadı.')
    });
  }

  protected exportAudit(): void {
    if (!this.allowed() || this.exporting()) return;
    let query: string;
    try { query = auditQuery(this.filters, this.context().organizationId, null, false); }
    catch (error) { this.fail(error, 'Filtreler doğrulanamadı.'); return; }
    this.exporting.set(true);
    this.error.set(null);
    this.api.export(query).pipe(finalize(() => this.exporting.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `zumbo-denetim-${new Date().toISOString().slice(0, 10)}.ndjson`;
        link.click();
        URL.revokeObjectURL(url);
        this.notice.set('Filtrelenen denetim kayıtları dışa aktarıldı.');
      },
      error: error => this.fail(error, 'Denetim dışa aktarımı tamamlanamadı.')
    });
  }

  private loadInitial(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({ users: this.api.users(), page: this.api.search(auditQuery(this.filters, this.context().organizationId)) })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ users, page }) => {
          this.users.set(users);
          this.entries.set(page.items);
          this.nextCursor.set(page.nextCursor ?? null);
          this.selected.set(page.items[0] ?? null);
        },
        error: error => this.fail(error, 'Denetim merkezi yüklenemedi.')
      });
  }

  private fail(error: unknown, fallback: string): void {
    this.error.set(error instanceof Error && error.message ? error.message : fallback);
  }
}
