import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { firstValueFrom } from 'rxjs';
import { ZumboRealtimeService } from '@zumbo/modern-shared';
import { MobileWorkItemRecord, MobileWorkflowStatus } from '../../shell/mobile-workspace.models';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';
import { mergeUniqueWorkItems } from './mobile-work.core';
import { MobileWorkService } from './mobile-work.service';

@Component({
  selector: 'zumbo-mobile-project-work',
  imports: [RouterLink, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar],
  templateUrl: './mobile-project-work.page.html',
  styleUrls: ['./mobile-project-work.page.scss', './mobile-work.shared.scss']
})
export class MobileProjectWorkPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(MobileWorkService);
  private readonly realtime = inject(ZumboRealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private loadInFlight = false;
  protected readonly store = inject(MobileWorkspaceStore);
  protected readonly projectId = this.route.snapshot.paramMap.get('projectId') ?? '';
  protected readonly project = computed(() => this.store.projects().find(item => item.id === this.projectId));
  protected readonly items = signal<readonly MobileWorkItemRecord[]>([]);
  protected readonly statuses = signal<readonly MobileWorkflowStatus[]>([]);
  protected readonly selectedStatus = signal('');
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly degraded = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.realtime.changes$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(change => {
      if (change.projectId === this.projectId) void this.load(false);
    });
    this.realtime.resync$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => void this.load(false));
  }

  async ngOnInit(): Promise<void> {
    void this.realtime.connect(this.projectId).catch(() => undefined);
    await this.load();
  }

  protected async load(showLoading = true): Promise<void> {
    if (!this.projectId || this.loadInFlight) return;
    this.loadInFlight = true;
    if (showLoading) this.loading.set(true);
    this.error.set(null);
    try {
      await this.store.load();
      const response = await firstValueFrom(this.service.loadProject(this.projectId, this.selectedStatus()));
      this.statuses.set([...response.workflow.statuses].sort((a, b) => (a.position ?? 0) - (b.position ?? 0)));
      this.items.set(response.result.items);
      this.realtime.synchronize(response.result.items);
      this.page.set(1);
      this.hasMore.set(response.result.items.length === 50);
      this.degraded.set(response.result.degraded === true);
    } catch {
      this.error.set('Proje işleri yüklenemedi. Yeniden deneyin.');
    } finally {
      this.loadInFlight = false;
      this.loading.set(false);
    }
  }

  protected setStatus(status: string): void {
    if (status === this.selectedStatus()) return;
    this.selectedStatus.set(status);
    void this.load();
  }

  protected async loadMore(): Promise<void> {
    if (!this.hasMore() || this.loading()) return;
    this.loading.set(true);
    try {
      const nextPage = this.page() + 1;
      const result = await firstValueFrom(this.service.search(this.projectId, '', nextPage, 50, this.selectedStatus()));
      this.items.update(current => mergeUniqueWorkItems(current, result.items));
      this.page.set(nextPage);
      this.hasMore.set(result.items.length === 50);
      this.degraded.set(result.degraded === true);
    } catch {
      this.error.set('Daha fazla iş yüklenemedi.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async refresh(event: Event): Promise<void> {
    try {
      await this.load();
    } finally {
      await (event.target as unknown as { complete(): Promise<void> }).complete();
    }
  }

  protected date(value?: string | null): string {
    return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value)) : '';
  }
}
