import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ZumboApiError, ZumboRealtimeService } from '@zumbo/modern-shared';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { ProjectWorkItemRole } from '../work-items/project-work-item.models';
import {
  CreateSprintDraft,
  ProjectPlanningData,
  ProjectSprint,
  SprintBurndownPoint,
  SprintScopeItem
} from './project-planning.models';
import { ProjectPlanningService } from './project-planning.service';

const TARGET_KEY = 'zumbo.planningSprint';

interface SprintDraft {
  readonly name: string;
  readonly goal: string;
  readonly startDate: string;
  readonly endDate: string;
}

@Component({
  selector: 'zumbo-project-sprint-page',
  imports: [RouterLink, ZumboIconComponent],
  providers: [ProjectPlanningService],
  templateUrl: './project-sprint.page.html',
  styleUrls: ['./project-sprint.page.scss', './project-sprint-detail.scss', './project-sprint-responsive.scss']
})
export class ProjectSprintPage {
  readonly project = input.required<ProjectSummary>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(ZumboRealtimeService);
  private contextProjectId = '';
  protected readonly data = signal<ProjectPlanningData | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly selectedSprintId = signal('');
  protected readonly carryoverSprintId = signal('');
  protected readonly createOpen = signal(false);
  protected readonly draft = signal(initialDraft());
  protected readonly draftError = signal<string | null>(null);
  protected readonly burndown = signal<readonly SprintBurndownPoint[]>([]);
  protected readonly burndownLoading = signal(false);
  protected readonly burndownError = signal<string | null>(null);

  protected readonly selectedSprint = computed(() => this.data()?.sprints.find(item => item.id === this.selectedSprintId()) ?? null);
  protected readonly sprintItems = computed(() => {
    const id = this.selectedSprintId();
    return (this.data()?.tasks ?? []).filter(item => item.sprintId === id) as readonly SprintScopeItem[];
  });
  protected readonly planningPoints = computed(() => this.sprintItems().reduce((sum, item) => sum + Number(item.estimatePoints || 0), 0));
  protected readonly canPlan = computed(() => this.hasPermission('WorkItemMove') && this.hasPermission('WorkItemUpdate'));
  protected readonly carryoverTargets = computed(() => (this.data()?.sprints ?? []).filter(item => item.status === 'Planned' && item.id !== this.selectedSprintId()));
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
      if (change.projectId === this.project().id && !this.busy()) this.load(false);
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
        const selected = data.sprints.find(item => item.id === this.selectedSprintId())
          ?? data.sprints.find(item => item.id === stored)
          ?? data.sprints.find(item => item.status === 'Active')
          ?? data.sprints.find(item => item.status === 'Planned')
          ?? data.sprints[0];
        this.selectedSprintId.set(selected?.id ?? '');
        this.realtime.synchronize(data.tasks);
        this.loadBurndown();
      },
      error: () => this.error.set('Sprint planı yüklenemedi.')
    });
  }

  protected selectSprint(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    if (!this.data()?.sprints.some(item => item.id === id)) return;
    this.selectedSprintId.set(id);
    this.carryoverSprintId.set('');
    localStorage.setItem(`${TARGET_KEY}.${this.project().id}`, id);
    this.notice.set(null);
    this.loadBurndown();
  }

  protected retryBurndown(): void {
    this.loadBurndown();
  }

  protected updateDraft(field: keyof SprintDraft, event: Event): void {
    this.draft.update(value => ({ ...value, [field]: (event.target as HTMLInputElement).value }));
    this.draftError.set(null);
  }

  protected createSprint(): void {
    const draft = this.draft();
    if (!this.canPlan() || this.busy() || !draft.name.trim()) return;
    if (!draft.startDate || !draft.endDate || draft.endDate < draft.startDate) {
      this.draftError.set('Bitiş tarihi başlangıç tarihinden önce olamaz.');
      return;
    }
    const request: CreateSprintDraft = {
      projectId: this.project().id,
      name: draft.name.trim(),
      goal: draft.goal.trim() || null,
      startDate: draft.startDate,
      endDate: draft.endDate
    };
    this.busy.set(true);
    this.planning.createSprint(request).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: sprint => {
        this.data.update(data => data ? { ...data, sprints: [...data.sprints, sprint] } : data);
        this.selectedSprintId.set(sprint.id);
        localStorage.setItem(`${TARGET_KEY}.${this.project().id}`, sprint.id);
        this.draft.set(initialDraft());
        this.createOpen.set(false);
        this.notice.set('Sprint oluşturuldu.');
        this.loadBurndown();
      },
      error: error => this.notice.set(lifecycleError((error as ZumboApiError).code))
    });
  }

  protected startSprint(): void {
    const sprint = this.selectedSprint();
    if (!sprint || sprint.status !== 'Planned') return;
    this.runLifecycle(this.planning.startSprint(sprint), 'Sprint başlatıldı.');
  }

  protected completeSprint(): void {
    const sprint = this.selectedSprint();
    if (!sprint || sprint.status !== 'Active') return;
    this.runLifecycle(this.planning.completeSprint(sprint, this.carryoverSprintId() || null), 'Sprint tamamlandı.');
  }

  protected unplanItem(item: SprintScopeItem): void {
    const sprint = this.selectedSprint();
    const snapshot = this.data();
    if (!sprint || !snapshot || sprint.status !== 'Planned' || this.busy()) return;
    this.busy.set(true);
    this.planning.unplan(item.id, sprint.id, item.version).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.data.update(data => data ? {
          ...data,
          backlog: data.backlog.some(candidate => candidate.id === item.id) ? data.backlog : [...data.backlog, {
            id: item.id, title: item.title, type: item.type, priority: item.priority,
            estimatePoints: Number(item.estimatePoints || 0), rank: item.rank, version: result.version
          }],
          tasks: data.tasks.map(candidate => candidate.id === item.id ? { ...candidate, sprintId: null, version: result.version } : candidate)
        } : data);
        this.notice.set('İş backlog alanına taşındı.');
      },
      error: error => {
        this.notice.set(lifecycleError((error as ZumboApiError).code));
        this.load(false);
      }
    });
  }

  protected statusLabel(status: string): string { return statusLabel(status); }
  protected sprintLabel(sprint: ProjectSprint): string { return `${sprint.name} · ${statusLabel(sprint.status)}`; }
  protected priorityLabel(priority: string): string { return ({ Critical: 'Kritik', High: 'Yüksek', Medium: 'Orta', Low: 'Düşük' } as Readonly<Record<string, string>>)[priority] ?? priority; }
  protected burndownWidth(point: SprintBurndownPoint): number {
    const values = this.burndown().map(item => item.remainingPoints || item.remainingItems);
    const max = Math.max(...values, 0);
    return max ? Math.round((point.remainingPoints || point.remainingItems) / max * 100) : 0;
  }
  protected formatDate(value: string): string { return new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(`${value}T00:00:00`)); }

  private loadBurndown(): void {
    const sprintId = this.selectedSprintId();
    if (!sprintId) { this.burndown.set([]); return; }
    this.burndownLoading.set(true);
    this.burndownError.set(null);
    this.planning.loadBurndown(sprintId).pipe(finalize(() => this.burndownLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: points => { if (this.selectedSprintId() === sprintId) this.burndown.set(points); },
      error: () => { if (this.selectedSprintId() === sprintId) { this.burndown.set([]); this.burndownError.set('Burndown verisi yüklenemedi.'); } }
    });
  }

  private runLifecycle(request: ReturnType<ProjectPlanningService['startSprint']>, message: string): void {
    if (!this.canPlan() || this.busy()) return;
    this.busy.set(true);
    request.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: sprint => {
        this.data.update(data => data ? { ...data, sprints: data.sprints.map(item => item.id === sprint.id ? sprint : item) } : data);
        this.notice.set(message);
        this.load(false);
      },
      error: error => {
        this.notice.set(lifecycleError((error as ZumboApiError).code));
        if ((error as ZumboApiError).code === 'CONCURRENCY_CONFLICT') this.load(false);
      }
    });
  }

  private hasPermission(permission: string): boolean {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role: ProjectWorkItemRole | undefined = this.data()?.roles.find(item => item.name === membership?.role && item.isActive);
    return !!role?.permissions.some(value => value === '*' || value === permission);
  }
}

function initialDraft(): SprintDraft {
  const start = new Date();
  const end = new Date(start);
  end.setDate(end.getDate() + 13);
  return { name: '', goal: '', startDate: dateValue(start), endDate: dateValue(end) };
}

function dateValue(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function statusLabel(status: string): string {
  return ({ Planned: 'Planlandı', Active: 'Aktif', Completed: 'Tamamlandı' } as Readonly<Record<string, string>>)[status] ?? status;
}

function lifecycleError(code: string | null | undefined): string {
  if (code === 'CONCURRENCY_CONFLICT') return 'Sprint başka bir kullanıcı tarafından değiştirildi; güncel plan yükleniyor.';
  if (code === 'SPRINT_ACTIVE_EXISTS') return 'Bu projede zaten aktif bir sprint var.';
  if (code === 'SPRINT_PLANNING_CLOSED') return 'Sprint başladığı için kapsamı artık değiştirilemez.';
  return 'Sprint işlemi tamamlanamadı.';
}
