import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { DEPENDENCY_STATUSES, PORTFOLIO_HEALTH, PORTFOLIO_STATUSES, dependencyError, healthLabel, initiativeError, initiativeTree, newDependencyDraft, newInitiativeDraft, newPortfolioDraft, portfolioError, statusLabel } from './portfolio.core';
import { DependencyDraft, Initiative, InitiativeDraft, Portfolio, PortfolioDraft, PortfolioRoadmap, PortfolioTab, PortfolioUser, StatusDraft } from './portfolio.models';
import { PortfolioService } from './portfolio.service';

@Component({ selector: 'zumbo-portfolio-page', imports: [CommonModule, FormsModule, ZumboIconComponent], providers: [PortfolioService], templateUrl: './portfolio.page.html', styleUrls: ['./portfolio.page.scss', './portfolio-layout.scss', './portfolio-responsive.scss', './portfolio-theme.scss', './portfolio-controls.scss'] })
export class PortfolioPage implements OnInit {
  readonly projects = input.required<readonly ProjectSummary[]>();
  readonly userId = input.required<string>();
  private readonly api = inject(PortfolioService); private readonly destroyRef = inject(DestroyRef);
  protected readonly loading = signal(true); protected readonly busy = signal(false); protected readonly error = signal<string | null>(null); protected readonly notice = signal<string | null>(null);
  protected readonly portfolios = signal<readonly Portfolio[]>([]); protected readonly users = signal<readonly PortfolioUser[]>([]); protected readonly selected = signal<Portfolio | null>(null); protected readonly roadmap = signal<PortfolioRoadmap | null>(null); protected readonly tab = signal<PortfolioTab>('roadmap');
  protected portfolioDraft: PortfolioDraft = newPortfolioDraft(); protected initiativeDraft: InitiativeDraft = newInitiativeDraft(''); protected dependencyDraft: DependencyDraft = newDependencyDraft(); protected statusDraft: StatusDraft = { status: 'Active', health: 'OnTrack', confidence: 75, note: '' };
  protected readonly statuses = PORTFOLIO_STATUSES; protected readonly healthStates = PORTFOLIO_HEALTH; protected readonly dependencyStatuses = DEPENDENCY_STATUSES;
  protected readonly tree = computed(() => initiativeTree(this.selected()?.initiatives ?? [])); protected readonly statusCandidates = computed(() => this.selected()?.initiatives.filter(item => item.canUpdateStatus) ?? []);
  protected readonly activeCount = computed(() => this.selected()?.initiatives.filter(item => item.status === 'Active').length ?? 0); protected readonly riskCount = computed(() => this.selected()?.initiatives.filter(item => ['AtRisk', 'OffTrack'].includes(item.health)).length ?? 0);
  ngOnInit(): void { this.load(); }
  protected load(): void { this.loading.set(true); this.error.set(null); forkJoin({ page: this.api.list(), users: this.api.users() }).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: ({ page, users }) => { this.portfolios.set(page.items); this.users.set(users); const id = this.selected()?.id; const next = page.items.find(item => item.id === id) ?? page.items[0]; if (next) this.select(next); else this.createPortfolio(); }, error: e => this.fail(e, 'Portföyler yüklenemedi.') }); }
  protected select(item: Portfolio): void { this.busy.set(true); this.error.set(null); this.api.detail(item.id).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: ({ portfolio, roadmap }) => { this.selected.set(portfolio); this.roadmap.set(roadmap); this.portfolioDraft = newPortfolioDraft(portfolio); this.newInitiative(); this.newDependency(); }, error: e => this.fail(e, 'Portföy ayrıntısı yüklenemedi.') }); }
  protected createPortfolio(): void { this.selected.set(null); this.roadmap.set(null); this.portfolioDraft = newPortfolioDraft(); this.tab.set('roadmap'); }
  protected savePortfolio(): void { const message = portfolioError(this.portfolioDraft); if (message) return this.error.set(message); this.mutate(this.api.savePortfolio(this.portfolioDraft), this.portfolioDraft.id ? 'Portföy güncellendi.' : 'Portföy oluşturuldu.', saved => { this.selected.set(saved); this.load(); }); }
  protected archive(): void { const item = this.selected(); if (!item?.canEdit || !window.confirm('Bu portföyü arşivlemek istiyor musunuz?')) return; this.mutate(this.api.archive(item), 'Portföy arşivlendi.', () => { this.selected.set(null); this.load(); }); }
  protected newInitiative(parentId = ''): void { this.initiativeDraft = newInitiativeDraft(this.userId(), undefined, parentId); }
  protected editInitiative(item: Initiative): void { this.initiativeDraft = newInitiativeDraft(this.userId(), item); this.tab.set('initiatives'); }
  protected prepareStatus(item: Initiative): void { this.initiativeDraft = newInitiativeDraft(this.userId(), item); this.statusDraft = { status: item.status, health: item.health, confidence: item.confidence ?? null, note: '' }; this.tab.set('updates'); }
  protected saveInitiative(): void { const portfolio = this.selected(); if (!portfolio?.canEdit) return; const message = initiativeError(this.initiativeDraft); if (message) return this.error.set(message); this.mutate(this.api.saveInitiative(portfolio, this.initiativeDraft), 'İnisiyatif kaydedildi.', saved => this.refresh(saved)); }
  protected publishStatus(): void { const portfolio = this.selected(); const initiative = portfolio?.initiatives.find(item => item.id === this.initiativeDraft.id); if (!portfolio || !initiative?.canUpdateStatus || !this.statusDraft.note.trim()) return this.error.set('Bu güncellemeyi yayımlama yetkiniz veya durum notunuz yok.'); this.mutate(this.api.addStatus(portfolio, initiative.id, this.statusDraft), 'Durum güncellemesi yayımlandı.', saved => this.refresh(saved)); }
  protected newDependency(): void { this.dependencyDraft = newDependencyDraft(); }
  protected editDependency(item: import('./portfolio.models').PortfolioDependency): void { this.dependencyDraft = newDependencyDraft(item); this.tab.set('dependencies'); }
  protected saveDependency(): void { const portfolio = this.selected(); if (!portfolio?.canEdit) return; const message = dependencyError(this.dependencyDraft); if (message) return this.error.set(message); this.mutate(this.api.saveDependency(portfolio, this.dependencyDraft), 'Bağımlılık kaydedildi.', saved => this.refresh(saved)); }
  protected toggleViewer(id: string, checked: boolean): void { this.portfolioDraft.viewerUserIds = checked ? [...new Set([...this.portfolioDraft.viewerUserIds, id])] : this.portfolioDraft.viewerUserIds.filter(value => value !== id); }
  protected toggleProject(id: string, checked: boolean): void { this.initiativeDraft.projectIds = checked ? [...new Set([...this.initiativeDraft.projectIds, id])] : this.initiativeDraft.projectIds.filter(value => value !== id); }
  protected userName(id: string): string { const user = this.users().find(item => item.id === id); return user?.username || user?.email || 'Bilinmeyen kullanıcı'; }
  protected projectName(id: string): string { return this.projects().find(item => item.id === id)?.name ?? 'Erişilemeyen proje'; }
  protected statusLabel = statusLabel; protected healthLabel = healthLabel;
  private refresh(saved: Portfolio): void { this.selected.set(saved); this.select(saved); }
  private mutate<T>(request: import('rxjs').Observable<T>, notice: string, done: (value: T) => void): void { if (this.busy()) return; this.busy.set(true); this.error.set(null); this.notice.set(null); request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { this.notice.set(notice); done(value); }, error: e => this.fail(e, 'İşlem tamamlanamadı.') }); }
  private fail(error: any, fallback: string): void { this.error.set(error?.message ?? fallback); }
}
