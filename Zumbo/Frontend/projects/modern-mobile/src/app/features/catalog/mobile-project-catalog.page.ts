import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { IonBackButton, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSegment, IonSegmentButton, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { canManageProjectCatalog, canReleaseProjectCatalog, normalizeProjectComponentNames, projectCatalogErrorMessage, projectCatalogLimits, projectRoleOf, ZumboSessionService } from '@zumbo/modern-shared';
import { finalize, Observable } from 'rxjs';
import { MobileConnectivityService } from '../../shell/mobile-connectivity.service';
import { mobileCatalogAuditLabel, mobileCatalogDateInput, mobileCatalogSnapshot, mobileCatalogVersionLabel } from './mobile-project-catalog.core';
import { MobileCatalogProject, MobileComponentDraft, MobileMilestoneDraft, MobileProjectCatalogData, MobileProjectCatalogTab, MobileProjectRelease, MobileProjectMilestone, MobileProjectTemplate, MobileProjectComponent, MobileProjectVersion, MobileReleaseDraft, MobileTemplateDraft } from './mobile-project-catalog.models';
import { MobileProjectCatalogService } from './mobile-project-catalog.service';

@Component({
  selector: 'zumbo-mobile-project-catalog',
  imports: [IonBackButton, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonSegment, IonSegmentButton, IonTitle, IonToolbar],
  providers: [MobileProjectCatalogService],
  templateUrl: './mobile-project-catalog.page.html',
  styleUrls: ['./mobile-project-catalog.page.scss', './mobile-project-catalog-responsive.scss']
})
export class MobileProjectCatalogPage {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(MobileProjectCatalogService);
  private readonly session = inject(ZumboSessionService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly projectId = signal('');
  protected readonly data = signal<MobileProjectCatalogData | null>(null);
  protected readonly tab = signal<MobileProjectCatalogTab>('releases');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly confirm = signal<{ readonly kind: string; readonly id: string } | null>(null);
  protected readonly versionName = signal('');
  protected readonly releaseDraft = signal<MobileReleaseDraft>({ versionId: '', name: '', scheduledAt: '' });
  protected readonly milestoneDraft = signal<MobileMilestoneDraft>({ name: '', dueAt: '' });
  protected readonly componentDraft = signal<MobileComponentDraft>({ name: '', description: '' });
  protected readonly templateDraft = signal<MobileTemplateDraft>({ name: '', isDefault: false, defaultComponentNamesText: '' });
  protected readonly limits = projectCatalogLimits;
  protected readonly snapshot = computed(() => mobileCatalogSnapshot(this.data()?.project));
  protected readonly role = computed(() => projectRoleOf(this.data()?.project, this.session.currentUser()?.id ?? ''));
  protected readonly canManage = computed(() => canManageProjectCatalog(this.role(), this.data()?.roles));
  protected readonly canRelease = computed(() => canReleaseProjectCatalog(this.role(), this.data()?.roles));
  protected readonly mutationLocked = computed(() => this.busy() || this.connectivity.offline() || !this.canManage());
  protected readonly componentNames = computed(() => normalizeProjectComponentNames(this.templateDraft().defaultComponentNamesText));

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const id = params.get('projectId') ?? '';
      if (id === this.projectId()) return;
      this.projectId.set(id);
      this.data.set(null);
      this.load();
    });
  }

  protected selectTab(event: CustomEvent): void { this.tab.set((event.detail.value || 'releases') as MobileProjectCatalogTab); this.confirm.set(null); }
  protected refresh(event: Event): void { this.load(() => void (event.target as unknown as { complete(): Promise<void> }).complete()); }
  protected reload(): void { this.load(); }
  protected requestConfirm(kind: string, id: string): void { this.confirm.set({ kind, id }); }
  protected isConfirm(kind: string, id: string): boolean { const value = this.confirm(); return value?.kind === kind && value.id === id; }
  protected date(value?: string | null): string { return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(value)) : 'Tarih planlanmadı'; }
  protected actorName(id?: string | null): string { const actor = this.data()?.users.find(user => user.id === id); return actor?.username || actor?.email || 'Sistem işlemi'; }
  protected auditLabel(action: string): string { return mobileCatalogAuditLabel(action); }
  protected versionLabel(id: string): string { return mobileCatalogVersionLabel(this.data()?.project, id); }
  protected editTemplate(value: MobileProjectTemplate): void { this.templateDraft.set({ id: value.id, name: value.name, isDefault: value.isDefault, defaultComponentNamesText: value.defaultComponentNames.join('\n') }); this.tab.set('templates'); }
  protected editComponent(value: MobileProjectComponent): void { this.componentDraft.set({ id: value.id, name: value.name, description: value.description ?? '' }); this.tab.set('components'); }
  protected editMilestone(value: MobileProjectMilestone): void { this.milestoneDraft.set({ id: value.id, name: value.name, dueAt: mobileCatalogDateInput(value.dueAt) }); this.tab.set('milestones'); }
  protected updateTemplate(field: keyof MobileTemplateDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLTextAreaElement; this.templateDraft.update(value => ({ ...value, [field]: field === 'isDefault' ? (target as HTMLInputElement).checked : target.value })); }
  protected updateComponent(field: keyof MobileComponentDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLTextAreaElement; this.componentDraft.update(value => ({ ...value, [field]: target.value })); }
  protected updateMilestone(field: keyof MobileMilestoneDraft, event: Event): void { const target = event.target as HTMLInputElement; this.milestoneDraft.update(value => ({ ...value, [field]: target.value })); }
  protected updateRelease(field: keyof MobileReleaseDraft, event: Event): void { const target = event.target as HTMLInputElement | HTMLSelectElement; this.releaseDraft.update(value => ({ ...value, [field]: target.value })); }
  protected updateVersion(event: Event): void { this.versionName.set((event.target as HTMLInputElement).value); }

  protected saveTemplate(): void { const draft = this.templateDraft(); const names = this.componentNames(); if (!draft.name.trim() || names.tooMany || names.tooLong) return; this.mutate(this.service.saveTemplate(this.projectId(), draft, names.values), draft.id ? 'Şablon güncellendi.' : 'Şablon oluşturuldu.', () => this.templateDraft.set({ name: '', isDefault: false, defaultComponentNamesText: '' }), 'Şablon kaydedilemedi.'); }
  protected saveComponent(): void { const draft = this.componentDraft(); if (!draft.name.trim()) return; this.mutate(this.service.saveComponent(this.projectId(), draft), draft.id ? 'Bileşen güncellendi.' : 'Bileşen oluşturuldu.', () => this.componentDraft.set({ name: '', description: '' }), 'Bileşen kaydedilemedi.'); }
  protected createVersion(): void { if (!this.versionName().trim()) return; this.mutate(this.service.createVersion(this.projectId(), this.versionName()), 'Sürüm oluşturuldu.', () => this.versionName.set(''), 'Sürüm oluşturulamadı.'); }
  protected createRelease(): void { const draft = this.releaseDraft(); if (!draft.versionId || !draft.name.trim()) return; this.mutate(this.service.createRelease(this.projectId(), draft), 'Yayın taslağı oluşturuldu.', () => this.releaseDraft.set({ versionId: '', name: '', scheduledAt: '' }), 'Yayın taslağı oluşturulamadı.'); }
  protected saveMilestone(): void { const draft = this.milestoneDraft(); if (!draft.name.trim() || !draft.dueAt) return; this.mutate(this.service.saveMilestone(this.projectId(), draft), draft.id ? 'Kilometre taşı güncellendi.' : 'Kilometre taşı oluşturuldu.', () => this.milestoneDraft.set({ name: '', dueAt: '' }), 'Kilometre taşı kaydedilemedi.'); }
  protected archiveTemplate(value: MobileProjectTemplate): void { this.mutate(this.service.archiveTemplate(this.projectId(), value.id), 'Şablon arşivlendi.', () => this.templateDraft.set({ name: '', isDefault: false, defaultComponentNamesText: '' }), 'Şablon arşivlenemedi.'); }
  protected archiveComponent(value: MobileProjectComponent): void { this.mutate(this.service.archiveComponent(this.projectId(), value.id), 'Bileşen arşivlendi.', () => this.componentDraft.set({ name: '', description: '' }), 'Bileşen arşivlenemedi.'); }
  protected archiveVersion(value: MobileProjectVersion): void { this.mutate(this.service.archiveVersion(this.projectId(), value.id), 'Sürüm arşivlendi.', () => this.versionName.set(''), 'Sürüm arşivlenemedi.'); }
  protected approveRelease(value: MobileProjectRelease): void { this.mutate(this.service.approveRelease(this.projectId(), value.id), 'Yayın onaylandı.', () => undefined, 'Yayın onaylanamadı.'); }
  protected publishRelease(value: MobileProjectRelease): void { this.mutate(this.service.publishRelease(this.projectId(), value.id), 'Yayınlandı ve sürüm tamamlandı.', () => undefined, 'Yayınlanamadı.'); }
  protected completeMilestone(value: MobileProjectMilestone): void { this.mutate(this.service.completeMilestone(this.projectId(), value.id), 'Kilometre taşı tamamlandı.', () => this.milestoneDraft.set({ name: '', dueAt: '' }), 'Kilometre taşı tamamlanamadı.'); }

  private load(done?: () => void): void {
    const id = this.projectId();
    if (!id) { this.loading.set(false); this.error.set('Proje bulunamadı.'); done?.(); return; }
    this.loading.set(true); this.error.set(null);
    this.service.load(id).pipe(finalize(() => { this.loading.set(false); done?.(); }), takeUntilDestroyed(this.destroyRef)).subscribe({ next: data => this.data.set(data), error: () => this.error.set('Proje kataloğu yüklenemedi.') });
  }

  private mutate(request: Observable<MobileCatalogProject>, message: string, reset: () => void, fallback: string): void {
    if (this.mutationLocked()) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null); this.confirm.set(null);
    request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: project => { this.data.update(data => data ? { ...data, project } : data); reset(); this.notice.set(message); this.refreshAudit(); },
      error: error => { this.error.set(projectCatalogErrorMessage(error, fallback)); if (error?.code === 'CONCURRENCY_CONFLICT') this.load(); }
    });
  }

  private refreshAudit(): void { this.service.refreshAudit(this.projectId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(audit => this.data.update(data => data ? { ...data, audit } : data)); }
}
