import { CommonModule, DOCUMENT } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ProjectSummary, ProjectViewId } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { ProjectWorkItem, ProjectWorkItemUser } from '../work-items/project-work-item.models';
import { DASHBOARD_CATALOG, buildReportingModels, createDashboard, createWidget, normalizeWidgets, validateDashboard } from './project-reporting.core';
import { Dashboard, DashboardRender, ProjectReportingData, ReportingViewMode, WorkloadRow } from './project-reporting.models';
import { ProjectReportingService } from './project-reporting.service';

@Component({
  selector: 'zumbo-project-reporting-page',
  imports: [CommonModule, RouterLink, ZumboIconComponent],
  providers: [ProjectReportingService],
  templateUrl: './project-reporting.page.html',
  styleUrls: ['./project-reporting.page.scss', './project-reporting-data.scss', './project-reporting-dashboard.scss', './project-reporting-accessibility.scss', './project-reporting-responsive.scss']
})
export class ProjectReportingPage {
  readonly project = input.required<ProjectSummary>();
  readonly projects = input.required<readonly ProjectSummary[]>();
  readonly contextReady = input(false);
  readonly view = input.required<ProjectViewId>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private contextKey = '';
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly rangeDays = signal<30 | 90 | 180>(30);
  protected readonly data = signal<ProjectReportingData | null>(null);
  protected readonly dashboards = signal<readonly Dashboard[]>([]);
  protected readonly dashboardUsers = signal<readonly ProjectWorkItemUser[]>([]);
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly render = signal<DashboardRender | null>(null);
  protected readonly drilldown = signal<WorkloadRow | null>(null);
  protected readonly drilldownLimit = signal(50);
  protected readonly tableOpen = signal(false);
  protected readonly widgetType = signal(DASHBOARD_CATALOG[0][0] as string);
  protected readonly dashboardCatalog = DASHBOARD_CATALOG;
  protected readonly mode = computed<ReportingViewMode>(() => isReportingMode(this.view()) ? this.view() as ReportingViewMode : 'reports');
  protected readonly userName = (id?: string | null): string => this.data()?.users.find(user => user.id === id)?.username || this.data()?.users.find(user => user.id === id)?.email || 'Atanmamış';
  protected readonly models = computed(() => this.data() ? buildReportingModels(this.data()!, this.userName) : null);

  constructor(private readonly reporting: ProjectReportingService) {
    effect(() => {
      const key = `${this.project().id}:${this.mode()}`;
      if (!this.contextReady() || key === this.contextKey) return;
      this.contextKey = key;
      this.mode() === 'dashboards' ? this.loadDashboards() : this.loadReports();
    });
  }

  protected loadReports(): void {
    this.loading.set(true); this.error.set(null); this.drilldown.set(null);
    this.reporting.loadReports(this.project().id, this.rangeDays()).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => this.data.set(data), error: () => this.error.set('Proje raporları yüklenemedi. Tekrar deneyin.')
    });
  }
  protected changeRange(event: Event): void { this.rangeDays.set(Number((event.target as HTMLSelectElement).value) as 30 | 90 | 180); this.loadReports(); }
  protected openDrilldown(row: WorkloadRow): void { this.drilldown.set(row); this.drilldownLimit.set(50); }
  protected taskRoute(task: ProjectWorkItem): readonly string[] { return ['/workspace', this.project().id, this.mode(), 'task', task.id]; }
  protected reportWidthClass(value: number): string { return `report-width-${Math.max(1, Math.min(12, value))}`; }
  protected formatDays(hours?: number | null): string { return hours == null ? 'Veri yok' : `${(hours / 24).toLocaleString('tr-TR', { maximumFractionDigits: 1 })} gün`; }

  protected loadDashboards(selectId?: string): void {
    this.loading.set(true); this.error.set(null);
    this.reporting.loadDashboards(this.projects()).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: context => {
        this.dashboardUsers.set(context.users);
        const visible = context.dashboards.filter(item => item.projectIds.includes(this.project().id));
        this.dashboards.set(visible);
        const selected = visible.find(item => item.id === selectId) ?? visible[0];
        selected?.id ? this.selectDashboard(selected) : this.newDashboard();
      },
      error: () => this.error.set('Dashboardlar yüklenemedi. Tekrar deneyin.')
    });
  }
  protected newDashboard(): void { this.dashboard.set(createDashboard(this.project().id)); this.render.set(null); this.notice.set(null); }
  protected selectDashboard(value: Dashboard): void {
    if (!value.id) return;
    this.busy.set(true); this.error.set(null);
    this.reporting.getDashboard(value.id).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: dashboard => { this.dashboard.set(dashboard); this.renderDashboard(); }, error: () => this.error.set('Dashboard açılamadı.')
    });
  }
  protected updateDashboard(field: 'name' | 'description' | 'scope', event: Event): void {
    const value = this.dashboard(); if (!value) return;
    const inputValue = (event.target as HTMLInputElement | HTMLSelectElement).value;
    const scope = field === 'scope' ? inputValue as Dashboard['scope'] : value.scope;
    const projectIds = field === 'scope' && scope === 'Project' ? [this.project().id] : value.projectIds;
    this.dashboard.set({ ...value, [field]: inputValue, scope, projectIds });
  }
  protected updateNumber(field: 'rangeDays' | 'dueRiskDays', event: Event): void { const value = this.dashboard(); if (value) this.dashboard.set({ ...value, filter: { ...value.filter, [field]: Number((event.target as HTMLInputElement).value) } }); }
  protected updateProjects(event: Event): void { const value = this.dashboard(); if (!value) return; const ids = [...(event.target as HTMLSelectElement).selectedOptions].map(option => option.value); this.dashboard.set({ ...value, projectIds: ids }); }
  protected updateViewers(event: Event): void { const value = this.dashboard(); if (!value) return; const viewerUserIds = [...(event.target as HTMLSelectElement).selectedOptions].map(option => option.value); this.dashboard.set({ ...value, viewerUserIds }); }
  protected updateWidgetTitle(index: number, event: Event): void { const value = this.dashboard(); if (!value) return; this.dashboard.set({ ...value, widgets: value.widgets.map((widget, i) => i === index ? { ...widget, title: (event.target as HTMLInputElement).value } : widget) }); }
  protected addWidget(): void { const value = this.dashboard(); if (!value || value.widgets.length >= 12) return; this.dashboard.set({ ...value, widgets: normalizeWidgets([...value.widgets, createWidget(this.widgetType(), value.widgets.length)]) }); }
  protected removeWidget(index: number): void { const value = this.dashboard(); if (!value || value.widgets.length <= 1) return; this.dashboard.set({ ...value, widgets: normalizeWidgets(value.widgets.filter((_, i) => i !== index)) }); }
  protected moveWidget(index: number, direction: number): void { const value = this.dashboard(); if (!value) return; const target = index + direction; if (target < 0 || target >= value.widgets.length) return; const widgets = [...value.widgets]; const [widget] = widgets.splice(index, 1); widgets.splice(target, 0, widget); this.dashboard.set({ ...value, widgets: normalizeWidgets(widgets) }); }
  protected saveDashboard(): void {
    const value = this.dashboard(); if (!value || this.busy()) return;
    const validation = validateDashboard(value); if (validation) { this.error.set(validation); return; }
    this.busy.set(true); this.error.set(null);
    this.reporting.saveDashboard(value).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: saved => { this.dashboard.set(saved); this.dashboards.update(items => [saved, ...items.filter(item => item.id !== saved.id)]); this.notice.set('Dashboard kaydedildi.'); this.renderDashboard(); },
      error: () => this.error.set('Dashboard kaydedilemedi.')
    });
  }
  protected renderDashboard(): void { const value = this.dashboard(); if (!value?.id) return; this.reporting.renderDashboard(value.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: result => this.render.set(result), error: () => this.error.set('Dashboard verileri yenilenemedi.') }); }
  protected shareDashboard(): void { const value = this.dashboard(); if (!value?.id || !value.canEdit || this.busy()) return; this.busy.set(true); this.reporting.shareDashboard(value).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: saved => { this.dashboard.set(saved); this.notice.set('Paylaşım güncellendi.'); }, error: () => this.error.set('Paylaşım güncellenemedi.') }); }
  protected exportDashboard(): void { const value = this.dashboard(); if (!value?.id) return; this.reporting.exportDashboard(value.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: blob => { const url = URL.createObjectURL(blob); const link = this.document.createElement('a'); link.href = url; link.download = `zumbo-dashboard-${value.id}.json`; link.click(); URL.revokeObjectURL(url); }, error: () => this.error.set('Dashboard dışa aktarılamadı.') }); }
  protected archiveDashboard(): void { const value = this.dashboard(); if (!value?.id || !value.canEdit || !confirm('Bu dashboard arşivlensin mi?')) return; this.busy.set(true); this.reporting.archiveDashboard(value.id).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: () => { this.notice.set('Dashboard arşivlendi.'); this.loadDashboards(); }, error: () => this.error.set('Dashboard arşivlenemedi.') }); }
  protected projectName(id: string): string { return this.projects().find(project => project.id === id)?.name ?? 'Erişilebilir proje'; }
  protected widgetLabel(type: string): string { return DASHBOARD_CATALOG.find(item => item[0] === type)?.[1] ?? type; }
  protected cellValue(row: Readonly<Record<string, string | null>>, key: string): string { const value = row[key]; return value == null || value === '' ? '—' : /userId$/i.test(key) ? this.userName(value) : value; }
}

function isReportingMode(value: ProjectViewId): boolean { return value === 'workload' || value === 'reports' || value === 'dashboards'; }
