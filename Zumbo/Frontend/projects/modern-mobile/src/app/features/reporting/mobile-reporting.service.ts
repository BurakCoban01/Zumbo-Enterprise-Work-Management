import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map } from 'rxjs';
import { mobileReportSnapshot } from './mobile-reporting.core';
import { MobileCompletionRateReport, MobileDashboardPage, MobileDashboardRender, MobileDueDateRiskReport, MobileFlowTimeReport, MobileProjectSummaryReport, MobileRawReport, MobileReportingData, MobileReportingProject, MobileReportingRole, MobileReportingUser, MobileReportingWorkflow, MobileStatusDistributionReport, MobileTeamPerformanceReport, MobileUserWorkloadReport } from './mobile-reporting.models';

@Injectable()
export class MobileReportingService {
  private readonly api = inject(ZumboApiClient);
  load(projectId: string, rangeDays: number): Observable<MobileReportingData> { const id = encodeURIComponent(projectId), to = dateKey(new Date()), from = dateKey(new Date(Date.now() - (rangeDays - 1) * 86400000)), range = `?from=${from}&to=${to}`; return forkJoin({ project: this.api.get<MobileReportingProject>(`/api/projects/${id}`), roles: this.api.get<readonly MobileReportingRole[]>('/api/auth/roles?scope=Project'), users: this.api.get<readonly MobileReportingUser[]>('/api/auth/users'), workflow: this.api.get<MobileReportingWorkflow>(`/api/workflows/${id}`), summary: this.raw<MobileProjectSummaryReport>(`/api/work-items/reports/project-summary/${id}`), status: this.raw<readonly MobileStatusDistributionReport[]>(`/api/work-items/reports/status-distribution/${id}`), workload: this.raw<readonly MobileUserWorkloadReport[]>(`/api/work-items/reports/user-workload/${id}`), risks: this.raw<readonly MobileDueDateRiskReport[]>(`/api/work-items/reports/due-date-risks/${id}?days=30`), flow: this.raw<MobileFlowTimeReport>(`/api/work-items/reports/flow-time/${id}${range}`), completion: this.raw<MobileCompletionRateReport>(`/api/work-items/reports/completion-rate/${id}${range}`), teams: this.raw<readonly MobileTeamPerformanceReport[]>(`/api/work-items/reports/team-performance/${id}${range}`) }).pipe(map(({ project, roles, users, workflow, ...snapshots }) => ({ project, roles, users, workflow, snapshots }))); }
  dashboards(projectId: string): Observable<readonly import('./mobile-reporting.models').MobileDashboard[]> { return this.api.get<MobileDashboardPage>('/api/dashboards?page=1&pageSize=100').pipe(map(page => page.items.filter(item => item.projectIds.includes(projectId) && !item.archived))); }
  render(id: string): Observable<MobileDashboardRender> { return this.api.get(`/api/dashboards/${encodeURIComponent(id)}/render`); }
  export(id: string): Observable<Blob> { return this.api.download(`/api/dashboards/${encodeURIComponent(id)}/export`); }
  private raw<T>(path: string): Observable<ReturnType<typeof mobileReportSnapshot<T>>> { return this.api.get<MobileRawReport<T>>(path, { rawResponse: true }).pipe(map(mobileReportSnapshot)); }
}
function dateKey(value: Date): string { return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`; }
