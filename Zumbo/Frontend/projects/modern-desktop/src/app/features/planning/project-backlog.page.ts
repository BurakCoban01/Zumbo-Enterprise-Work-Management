import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ZumboApiError, ZumboRealtimeService } from '@zumbo/modern-shared';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { ProjectWorkItemRole } from '../work-items/project-work-item.models';
import {
  ProjectPlanningData,
  ProjectSprint,
  SprintBacklogItem,
  SprintScopeItem
} from './project-planning.models';
import { ProjectPlanningService } from './project-planning.service';

const TARGET_KEY = 'zumbo.planningSprint';
const PRIORITY_ORDER: Readonly<Record<string, number>> = { Critical: 0, High: 1, Medium: 2, Low: 3 };

@Component({
  selector: 'zumbo-project-backlog-page',
  imports: [RouterLink, ZumboIconComponent],
  providers: [ProjectPlanningService],
  templateUrl: './project-backlog.page.html',
  styleUrls: ['./project-backlog.page.scss', './project-backlog-workspace.scss', './project-backlog-responsive.scss']
})
export class ProjectBacklogPage {
  readonly project = input.required<ProjectSummary>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(ZumboRealtimeService);
  private contextProjectId = '';
  protected readonly data = signal<ProjectPlanningData | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly query = signal('');
  protected readonly priority = signal('');
  protected readonly selectedSprintId = signal('');
  protected readonly pendingIds = signal<ReadonlySet<string>>(new Set());
  protected readonly lastPlanned = signal<{ readonly item: SprintBacklogItem; readonly sprintId: string; readonly version: number } | null>(null);

  protected readonly selectedSprint = computed(() => this.data()?.sprints.find(item => item.id === this.selectedSprintId()) ?? null);
  protected readonly canPlan = computed(() => this.hasPermission('WorkItemMove') && this.hasPermission('WorkItemUpdate'));
  protected readonly canChangeScope = computed(() => this.canPlan() && this.selectedSprint()?.status === 'Planned');
  protected readonly filteredBacklog = computed(() => {
    const query = this.query().trim().toLocaleLowerCase('tr-TR');
    return (this.data()?.backlog ?? []).filter(item => (!this.priority() || item.priority === this.priority())
      && (!query || `${item.title} ${item.type}`.toLocaleLowerCase('tr-TR').includes(query)))
      .slice().sort((left, right) => (PRIORITY_ORDER[left.priority] ?? 99) - (PRIORITY_ORDER[right.priority] ?? 99) || left.rank - right.rank || left.id.localeCompare(right.id));
  });
  protected readonly sprintItems = computed(() => {
    const sprintId = this.selectedSprintId();
    return (this.data()?.tasks ?? []).filter(item => item.sprintId === sprintId) as readonly SprintScopeItem[];
  });
  protected readonly planningPoints = computed(() => this.sprintItems().reduce((sum, item) => sum + Number(item.estimatePoints || 0), 0));
  protected readonly capacityBaseline = computed(() => {
    const points = (this.data()?.velocity ?? []).map(item => Number(item.completedPoints || 0));
    return points.length ? points.reduce((sum, value) => sum + value, 0) / points.length : null;
  });
  protected readonly capacityPercent = computed(() => this.capacityBaseline() ? Math.round(this.planningPoints() / this.capacityBaseline()! * 100) : null);

  constructor(private readonly planning: ProjectPlanningService) {
    effect(() => {
      const projectId = this.project().id;
      if (!this.contextReady() || projectId === this.contextProjectId) return;
      this.contextProjectId = projectId;
      this.load();
    });
    this.realtime.changes$.pipe(takeUntilDestroyed()).subscribe(change => {
      if (change.projectId === this.project().id && !this.pendingIds().has(change.workItemId)) this.load(false);
    });
    this.realtime.resync$.pipe(takeUntilDestroyed()).subscribe(() => this.load(false));
  }

  protected load(showLoading = true): void {
    if (showLoading) this.loading.set(true);
    this.error.set(null);
    this.planning.load(this.project().id).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: data => {
        this.data.set(data);
        const stored = localStorage.getItem(`${TARGET_KEY}.${this.project().id}`);
        const current = data.sprints.find(item => item.id === this.selectedSprintId());
        const selected = current
          ?? data.sprints.find(item => item.id === stored)
          ?? data.sprints.find(item => item.status === 'Active')
          ?? data.sprints.find(item => item.status === 'Planned')
          ?? data.sprints[0];
        this.selectedSprintId.set(selected?.id ?? '');
        this.realtime.synchronize(data.tasks);
      },
      error: () => this.error.set('Backlog ve sprint kapsamı yüklenemedi.')
    });
  }

  protected selectSprint(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    if (!this.data()?.sprints.some(item => item.id === id)) return;
    this.selectedSprintId.set(id);
    localStorage.setItem(`${TARGET_KEY}.${this.project().id}`, id);
    this.notice.set(null);
    this.lastPlanned.set(null);
  }

  protected setQuery(event: Event): void { this.query.set((event.target as HTMLInputElement).value); }
  protected setPriority(event: Event): void { this.priority.set((event.target as HTMLSelectElement).value); }

  protected planItem(item: SprintBacklogItem): void {
    const sprint = this.selectedSprint();
    const snapshot = this.data();
    if (!sprint || !snapshot || !this.canChangeScope() || this.pendingIds().has(item.id)) return;
    const task = snapshot.tasks.find(candidate => candidate.id === item.id);
    this.pendingIds.update(ids => new Set(ids).add(item.id));
    this.notice.set(null);
    this.data.set({
      ...snapshot,
      backlog: snapshot.backlog.filter(candidate => candidate.id !== item.id),
      tasks: task ? snapshot.tasks.map(candidate => candidate.id === item.id ? { ...candidate, sprintId: sprint.id } : candidate) : snapshot.tasks
    });
    this.planning.plan(item, sprint.id).pipe(
      finalize(() => this.pendingIds.update(ids => { const next = new Set(ids); next.delete(item.id); return next; })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.data.update(data => data ? {
          ...data,
          tasks: data.tasks.map(candidate => candidate.id === item.id ? { ...candidate, sprintId: result.sprintId, estimatePoints: result.estimatePoints, version: result.version } : candidate)
        } : data);
        this.notice.set('İş sprint kapsamına alındı.');
        this.lastPlanned.set({ item, sprintId: sprint.id, version: result.version });
      },
      error: error => {
        this.data.set(snapshot);
        const normalized = error as ZumboApiError;
        this.notice.set(planningError(normalized.code));
        if (normalized.code === 'CONCURRENCY_CONFLICT') this.load(false);
      }
    });
  }

  protected undoLastPlan(): void {
    const previous = this.lastPlanned();
    const snapshot = this.data();
    if (!previous || !snapshot || this.pendingIds().has(previous.item.id)) return;
    this.pendingIds.update(ids => new Set(ids).add(previous.item.id));
    this.planning.unplan(previous.item.id, previous.sprintId, previous.version).pipe(
      finalize(() => this.pendingIds.update(ids => { const next = new Set(ids); next.delete(previous.item.id); return next; })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.data.update(data => data ? {
          ...data,
          backlog: data.backlog.some(item => item.id === previous.item.id) ? data.backlog : [...data.backlog, { ...previous.item, version: result.version }],
          tasks: data.tasks.map(item => item.id === previous.item.id ? { ...item, sprintId: null, version: result.version } : item)
        } : data);
        this.lastPlanned.set(null);
        this.notice.set('İş backlog alanına geri taşındı.');
      },
      error: () => {
        this.lastPlanned.set(null);
        this.notice.set('Geri alma tamamlanamadı; güncel plan yükleniyor.');
        this.load(false);
      }
    });
  }

  protected handleItemKey(event: KeyboardEvent, item: SprintBacklogItem): void {
    if (!event.altKey || event.key !== 'ArrowRight') return;
    event.preventDefault();
    this.planItem(item);
  }

  protected loadMore(): void {
    const cursor = this.data()?.backlogNextCursor;
    if (!cursor || this.loadingMore()) return;
    this.loadingMore.set(true);
    this.planning.loadMoreBacklog(this.project().id, cursor).pipe(
      finalize(() => this.loadingMore.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: page => this.data.update(data => data ? {
        ...data,
        backlog: [...data.backlog, ...page.items.filter(item => !data.backlog.some(existing => existing.id === item.id))],
        backlogNextCursor: page.nextCursor
      } : data),
      error: () => this.notice.set('Backlog devam kayıtları yüklenemedi.')
    });
  }

  protected sprintLabel(sprint: ProjectSprint): string { return `${sprint.name} · ${statusLabel(sprint.status)}`; }
  protected statusLabel(status: string): string { return statusLabel(status); }
  protected priorityLabel(priority: string): string { return ({ Critical: 'Kritik', High: 'Yüksek', Medium: 'Orta', Low: 'Düşük' } as Readonly<Record<string, string>>)[priority] ?? priority; }
  protected capacityState(): string { const value = this.capacityPercent(); return value == null ? 'unknown' : value > 115 ? 'over' : value > 90 ? 'near' : 'available'; }

  private hasPermission(permission: string): boolean {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role: ProjectWorkItemRole | undefined = this.data()?.roles.find(item => item.name === membership?.role && item.isActive);
    return !!role?.permissions.some(value => value === '*' || value === permission);
  }
}

function statusLabel(status: string): string {
  return ({ Planned: 'Planlandı', Active: 'Aktif', Completed: 'Tamamlandı' } as Readonly<Record<string, string>>)[status] ?? status;
}

function planningError(code: string | null | undefined): string {
  if (code === 'CONCURRENCY_CONFLICT') return 'İş başka bir kullanıcı tarafından değiştirildi; güncel plan yükleniyor.';
  if (code === 'SPRINT_PLANNING_CLOSED') return 'Sprint başladığı için kapsamı artık değiştirilemez.';
  return 'İş sprint kapsamına alınamadı; önceki plan geri yüklendi.';
}
