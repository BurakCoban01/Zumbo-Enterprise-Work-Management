import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ZumboApiError, ZumboRealtimeService } from '@zumbo/modern-shared';
import { finalize } from 'rxjs';
import { ProjectSummary, ProjectViewId } from '../../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../../shell/zumbo-icon.component';
import { ProjectWorkItem, ProjectWorkItemRole } from '../../work-items/project-work-item.models';
import { addDays, buildPlanningModel, dateKey, formatPlanningDate } from './project-planning-view.core';
import { PlanningCalendarMode, PlanningFilters, PlanningViewData, PlanningViewMode, PlanningZoom } from './project-planning-view.models';
import { ProjectPlanningViewService } from './project-planning-view.service';

@Component({
  selector: 'zumbo-project-planning-view-page',
  imports: [RouterLink, ZumboIconComponent],
  providers: [ProjectPlanningViewService],
  templateUrl: './project-planning-view.page.html',
  styleUrls: ['./project-planning-view.page.scss', './project-planning-view-calendar.scss', './project-planning-view-charts.scss', './project-planning-view-layout.scss', './project-planning-view-responsive.scss']
})
export class ProjectPlanningViewPage {
  readonly project = input.required<ProjectSummary>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();
  readonly view = input.required<ProjectViewId>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly browser = inject(DOCUMENT).defaultView;
  private readonly realtime = inject(ZumboRealtimeService);
  private contextProjectId = '';
  private dragTaskId = '';
  protected readonly data = signal<PlanningViewData | null>(null);
  protected readonly loading = signal(true);
  protected readonly savingId = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<{ readonly kind: 'success' | 'error'; readonly text: string } | null>(null);
  protected readonly calendarMode = signal<PlanningCalendarMode>('month');
  protected readonly zoom = signal<PlanningZoom>('month');
  protected readonly anchor = signal(dateKey(new Date(), Intl.DateTimeFormat().resolvedOptions().timeZone, true));
  protected readonly filters = signal<PlanningFilters>({ query: '', assignee: '', type: '' });
  protected readonly tableOpen = signal(false);
  protected readonly unscheduledOpen = signal(false);
  protected readonly loadedAt = signal<Date | null>(null);
  protected readonly timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  protected readonly mode = computed<PlanningViewMode>(() => {
    const view = this.view();
    return isPlanningMode(view) ? view : 'calendar';
  });
  protected readonly model = computed(() => buildPlanningModel({
    tasks: this.data()?.tasks ?? [], sprints: this.data()?.sprints ?? [], project: this.project(), workflow: this.data()?.workflow ?? { transitions: [] },
    filters: this.filters(), anchorDate: this.anchor(), calendarMode: this.calendarMode() === 'week' ? 'week' : 'month', zoom: this.zoom(), timeZone: this.timeZone
  }));
  protected readonly visibleCalendarEvents = computed(() => {
    const days = this.model().calendarDays;
    if (!days.length) return [];
    return this.model().calendarEvents.filter(event => event.key >= days[0].key && event.key <= days[days.length - 1].key);
  });
  protected readonly visibleTimelineRows = computed(() => this.model().timelineRows.filter(row => row.inWindow));
  protected readonly visibleRoadmapRows = computed(() => this.model().roadmapRows.filter(row => row.inWindow));
  protected readonly canEdit = computed(() => this.hasPermission('WorkItemUpdate'));
  protected readonly typeOptions = computed(() => [...new Set((this.data()?.tasks ?? []).map(task => task.type))].sort((a, b) => a.localeCompare(b, 'tr-TR')));
  protected readonly windowLabel = computed(() => {
    if (this.mode() !== 'calendar') return `${formatPlanningDate(this.model().window.startKey)} – ${formatPlanningDate(this.model().window.endKey)}`;
    const days = this.model().calendarDays;
    return `${formatPlanningDate(days[0]?.key ?? '')} – ${formatPlanningDate(days[days.length - 1]?.key ?? '')}`;
  });

  constructor(private readonly planning: ProjectPlanningViewService) {
    effect(() => {
      const projectId = this.project().id;
      if (!this.contextReady() || projectId === this.contextProjectId) return;
      this.contextProjectId = projectId;
      this.restorePreferences();
      this.load();
    });
    this.realtime.changes$.pipe(takeUntilDestroyed()).subscribe(change => {
      if (change.projectId === this.project().id && change.workItemId !== this.savingId()) this.load(false);
    });
    this.realtime.resync$.pipe(takeUntilDestroyed()).subscribe(() => this.load(false));
  }

  protected load(showLoading = true): void {
    if (showLoading) this.loading.set(true);
    this.error.set(null);
    this.planning.load(this.project().id).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => { this.data.set(data); this.loadedAt.set(new Date()); this.realtime.synchronize(data.tasks); },
      error: () => this.error.set('Proje planının tüm kapsamı yüklenemedi.')
    });
  }

  protected setCalendarMode(mode: PlanningCalendarMode): void { this.calendarMode.set(mode); this.persistPreferences(); }
  protected setZoom(zoom: PlanningZoom): void { this.zoom.set(zoom); this.persistPreferences(); }
  protected shift(direction: number): void {
    const amount = this.mode() === 'calendar' ? this.calendarMode() === 'week' ? 7 : 28 : this.zoom() === 'quarter' ? 90 : this.zoom() === 'month' ? 28 : 14;
    this.anchor.set(addDays(this.anchor(), amount * direction));
  }
  protected today(): void { this.anchor.set(dateKey(new Date(), this.timeZone, true)); }
  protected updateFilter(field: keyof PlanningFilters, event: Event): void { this.filters.update(value => ({ ...value, [field]: (event.target as HTMLInputElement | HTMLSelectElement).value })); }
  protected clearFilters(): void { this.filters.set({ query: '', assignee: '', type: '' }); }
  protected userName(id?: string | null): string { const user = this.data()?.users.find(item => item.id === id); return user?.username || user?.email || 'Atanmamış'; }
  protected taskTitle(id: string): string { return this.data()?.tasks.find(task => task.id === id)?.title ?? 'Erişilemeyen iş'; }
  protected formatDate(key: string): string { return formatPlanningDate(key); }
  protected isToday(key: string): boolean { return key === dateKey(new Date(), this.timeZone, true); }
  protected segmentSpan(percentage: number): number { return Math.max(1, Math.min(12, Math.round(percentage * 12 / 100))); }

  protected startDrag(event: DragEvent, task: ProjectWorkItem): void {
    if (!this.canEdit()) { event.preventDefault(); return; }
    this.dragTaskId = task.id;
    event.dataTransfer?.setData('text/zumbo-work-item', task.id);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }
  protected allowDrop(event: DragEvent): void { if (this.canEdit()) event.preventDefault(); }
  protected drop(event: DragEvent, key: string): void { event.preventDefault(); const id = event.dataTransfer?.getData('text/zumbo-work-item') || this.dragTaskId; this.dragTaskId = ''; const task = this.data()?.tasks.find(item => item.id === id); if (task) this.reschedule(task, key); }
  protected changeDueDate(task: ProjectWorkItem, event: Event): void { this.reschedule(task, (event.target as HTMLInputElement).value); }

  private reschedule(task: ProjectWorkItem, key: string): void {
    const snapshot = this.data();
    if (!snapshot || !this.canEdit() || this.savingId() || !/^\d{4}-\d{2}-\d{2}$/.test(key)) return;
    this.savingId.set(task.id);
    this.notice.set(null);
    this.data.set({ ...snapshot, tasks: snapshot.tasks.map(item => item.id === task.id ? { ...item, dueDate: `${key}T00:00:00.000Z` } : item) });
    this.planning.updateDueDate(task, key).pipe(finalize(() => this.savingId.set('')), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => { this.data.update(data => data ? { ...data, tasks: data.tasks.map(item => item.id === updated.id ? updated : item) } : data); this.loadedAt.set(new Date()); this.notice.set({ kind: 'success', text: `Bitiş tarihi ${formatPlanningDate(key)} olarak güncellendi.` }); },
      error: error => { this.data.set(snapshot); const code = (error as ZumboApiError).code; this.notice.set({ kind: 'error', text: code === 'CONCURRENCY_CONFLICT' ? 'Tarih başka bir kullanıcı tarafından değiştirildi; güncel plan yükleniyor.' : 'Tarih kaydedilemedi; önceki değer geri yüklendi.' }); if (code === 'CONCURRENCY_CONFLICT') this.load(false); }
    });
  }

  private hasPermission(permission: string): boolean {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role: ProjectWorkItemRole | undefined = this.data()?.roles.find(item => item.name === membership?.role && item.isActive);
    return !!role?.permissions.some(value => value === '*' || value === permission);
  }
  private preferenceKey(): string { return `zumbo.modern.planningViews.${this.project().id}`; }
  private restorePreferences(): void { try { const value = JSON.parse(localStorage.getItem(this.preferenceKey()) ?? '{}') as { calendarMode?: PlanningCalendarMode; zoom?: PlanningZoom }; if (value.calendarMode && ['month', 'week', 'list'].includes(value.calendarMode)) this.calendarMode.set(value.calendarMode); if (value.zoom && ['week', 'month', 'quarter'].includes(value.zoom)) this.zoom.set(value.zoom); if ((this.browser?.innerWidth ?? 1024) <= 760) this.calendarMode.set('list'); } catch { localStorage.removeItem(this.preferenceKey()); if ((this.browser?.innerWidth ?? 1024) <= 760) this.calendarMode.set('list'); } }
  private persistPreferences(): void { localStorage.setItem(this.preferenceKey(), JSON.stringify({ calendarMode: this.calendarMode(), zoom: this.zoom() })); }
}

function isPlanningMode(value: ProjectViewId): value is PlanningViewMode { return value === 'calendar' || value === 'timeline' || value === 'roadmap'; }
