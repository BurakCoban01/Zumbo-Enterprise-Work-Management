import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ProjectWorkItemService } from '../work-items/project-work-item.service';
import { reportSnapshot } from './project-reporting.core';
import { CompletionRateReport, Dashboard, DashboardContext, DashboardPage, DashboardRender, DueDateRiskReport, FlowTimeReport, ProjectReportingData, ProjectSummaryReport, RawReport, StatusDistributionReport, TeamPerformanceReport, UserWorkloadReport } from './project-reporting.models';

@Injectable()
export class ProjectReportingService {
  private readonly api = inject(ZumboApiClient);
  private readonly workItems = inject(ProjectWorkItemService);

  loadReports(projectId: string, rangeDays: number): Observable<ProjectReportingData> {
    const id = encodeURIComponent(projectId);
    const to = dateKey(new Date());
    const from = dateKey(new Date(Date.now() - (rangeDays - 1) * 86400000));
    const range = `?from=${from}&to=${to}`;
    return forkJoin({
      collection: this.workItems.loadAll(projectId),
      summary: this.raw<ProjectSummaryReport>(`/api/work-items/reports/project-summary/${id}`),
      status: this.raw<readonly StatusDistributionReport[]>(`/api/work-items/reports/status-distribution/${id}`),
      workload: this.raw<readonly UserWorkloadReport[]>(`/api/work-items/reports/user-workload/${id}`),
      risks: this.raw<readonly DueDateRiskReport[]>(`/api/work-items/reports/due-date-risks/${id}?days=30`),
      flow: this.raw<FlowTimeReport>(`/api/work-items/reports/flow-time/${id}${range}`),
      completion: this.raw<CompletionRateReport>(`/api/work-items/reports/completion-rate/${id}${range}`),
      teams: this.raw<readonly TeamPerformanceReport[]>(`/api/work-items/reports/team-performance/${id}${range}`)
    }).pipe(map(({ collection, ...snapshots }) => ({ tasks: collection.tasks, users: collection.users, snapshots })));
  }

  loadDashboards(projects: readonly ProjectSummary[]): Observable<DashboardContext> {
    return forkJoin({
      page: this.api.get<DashboardPage>('/api/dashboards?page=1&pageSize=100'),
      users: this.api.get<DashboardContext['users']>('/api/auth/users')
    }).pipe(map(({ page, users }) => ({ dashboards: page.items, users, projects })));
  }

  getDashboard(id: string): Observable<Dashboard> { return this.api.get(`/api/dashboards/${encodeURIComponent(id)}`); }
  saveDashboard(value: Dashboard): Observable<Dashboard> {
    const payload = { name: value.name.trim(), description: value.description?.trim() || null, scope: value.scope, projectIds: value.projectIds, widgets: value.widgets, filter: value.filter };
    return value.id ? this.api.put(`/api/dashboards/${encodeURIComponent(value.id)}`, payload) : this.api.post('/api/dashboards', payload);
  }
  shareDashboard(value: Dashboard): Observable<Dashboard> { return this.api.put(`/api/dashboards/${encodeURIComponent(value.id!)}/sharing`, { viewerUserIds: value.viewerUserIds }); }
  renderDashboard(id: string): Observable<DashboardRender> { return this.api.get(`/api/dashboards/${encodeURIComponent(id)}/render`); }
  archiveDashboard(id: string): Observable<unknown> { return this.api.delete(`/api/dashboards/${encodeURIComponent(id)}`); }
  exportDashboard(id: string): Observable<Blob> { return this.api.download(`/api/dashboards/${encodeURIComponent(id)}/export`); }

  private raw<T>(path: string): Observable<ReturnType<typeof reportSnapshot<T>>> { return this.api.get<RawReport<T>>(path, { rawResponse: true }).pipe(map(reportSnapshot)); }
}

function dateKey(value: Date): string { return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`; }
