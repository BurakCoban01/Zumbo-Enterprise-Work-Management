import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { IonContent, IonHeader, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { firstValueFrom } from 'rxjs';
import { MobileSearchResult, MobileWorkItemRecord, priorityLabel } from '../../shell/mobile-workspace.models';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';
import { mergeUniqueWorkItems } from './mobile-work.core';
import { MobileWorkService } from './mobile-work.service';

@Component({
  selector: 'zumbo-mobile-search',
  imports: [FormsModule, RouterLink, IonContent, IonHeader, IonTitle, IonToolbar],
  templateUrl: './mobile-search.page.html',
  styleUrls: ['./mobile-search.page.scss', './mobile-work.shared.scss']
})
export class MobileSearchPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(MobileWorkService);
  protected readonly store = inject(MobileWorkspaceStore);
  protected readonly priorityLabel = priorityLabel;
  protected query = this.route.snapshot.queryParamMap.get('q') ?? '';
  protected projectId = this.route.snapshot.queryParamMap.get('project') ?? '';
  protected readonly items = signal<readonly MobileWorkItemRecord[]>([]);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly searched = signal(false);
  protected readonly degraded = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly project = computed(() => this.store.projects().find(item => item.id === this.projectId));

  async ngOnInit(): Promise<void> {
    try {
      await this.store.load();
      if (!this.store.projects().some(project => project.id === this.projectId)) this.projectId = this.store.projects()[0]?.id ?? '';
      if (this.query.trim().length >= 2 && this.projectId) await this.search();
    } catch {
      this.error.set('Arama kapsamı yüklenemedi.');
    }
  }

  protected async search(page = 1, append = false): Promise<void> {
    const query = this.query.trim();
    if (query.length < 2) {
      this.error.set('Aramak için en az 2 karakter yazın.');
      this.items.set([]);
      this.searched.set(false);
      return;
    }
    if (!this.projectId) {
      this.error.set('Arama kapsamı için bir proje seçin.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await firstValueFrom(this.service.search(this.projectId, query, page));
      this.applyResult(result, page, append);
      await this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { q: query, project: this.projectId },
        replaceUrl: true
      });
    } catch {
      this.error.set('Arama tamamlanamadı. Yeniden deneyin.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async loadMore(): Promise<void> {
    if (!this.hasMore() || this.loading()) return;
    await this.search(this.page() + 1, true);
  }

  protected changeProject(): void {
    this.clearResults();
    if (this.query.trim().length >= 2) void this.search();
  }

  protected clear(): void {
    this.query = '';
    this.clearResults();
    void this.router.navigate([], { relativeTo: this.route, queryParams: { project: this.projectId || null }, replaceUrl: true });
  }

  protected date(value?: string | null): string {
    return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value)) : '';
  }

  private applyResult(result: MobileSearchResult, page: number, append: boolean): void {
    this.query = this.query.trim();
    this.page.set(page);
    this.searched.set(true);
    this.degraded.set(result.degraded === true);
    this.hasMore.set(result.items.length === 50);
    this.items.update(current => append ? mergeUniqueWorkItems(current, result.items) : result.items);
  }

  private clearResults(): void {
    this.items.set([]);
    this.page.set(1);
    this.searched.set(false);
    this.degraded.set(false);
    this.hasMore.set(false);
    this.error.set(null);
  }
}
