import { HttpResponse } from '@angular/common/http';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ProjectWorkItem, ProjectWorkItemUser } from '../work-items/project-work-item.models';

export type ReportingViewMode = 'workload' | 'reports' | 'dashboards';

export interface ProjectSummaryReport { readonly total: number; readonly done: number; readonly inProgress: number; readonly overdue: number; }
export interface StatusDistributionReport { readonly status: string; readonly count: number; }
export interface UserWorkloadReport { readonly userId: string; readonly openItems: number; readonly overdueItems: number; readonly loggedHours: number; }
export interface DueDateRiskReport { readonly id: string; readonly title: string; readonly assigneeUserId?: string | null; readonly dueDate: string; readonly status: string; }
export interface FlowTimeReport { readonly completedItems: number; readonly cycleTimeSampleSize: number; readonly medianLeadTimeHours: number; readonly medianCycleTimeHours?: number | null; }
export interface CompletionRateReport { readonly createdItems: number; readonly completedItems: number; readonly completionRatePercent: number; }
export interface TeamPerformanceReport { readonly teamId: string; readonly teamName: string; readonly assignedItems: number; readonly completedItems: number; readonly completionRatePercent: number; readonly averageLeadTimeHours?: number | null; readonly loggedHours: number; }

export interface ReportSnapshot<T> { readonly data: T; readonly generatedAt: string | null; readonly sourceVersion: number; readonly stale: boolean; readonly ageSeconds: number; }
export interface ReportingFreshness { readonly generatedAt: string | null; readonly stale: boolean; readonly maxAgeSeconds: number; }
export interface WorkloadRow extends UserWorkloadReport { readonly label: string; readonly estimatedPoints: number; readonly unestimatedItems: number; readonly relativeWidth: number; readonly tasks: readonly ProjectWorkItem[]; }
export interface WorkloadModel { readonly rows: readonly WorkloadRow[]; readonly totals: { readonly openItems: number; readonly overdueItems: number; readonly loggedHours: number; readonly unestimatedItems: number; }; }
export interface StatusReportRow extends StatusDistributionReport { readonly percent: number; readonly relativeWidth: number; }
export interface ReportingModel { readonly summary: ProjectSummaryReport; readonly status: readonly StatusReportRow[]; readonly risks: readonly DueDateRiskReport[]; readonly flow: FlowTimeReport; readonly completion: CompletionRateReport; readonly teams: readonly TeamPerformanceReport[]; }

export interface ProjectReportingData {
  readonly tasks: readonly ProjectWorkItem[];
  readonly users: readonly ProjectWorkItemUser[];
  readonly snapshots: {
    readonly summary: ReportSnapshot<ProjectSummaryReport>;
    readonly status: ReportSnapshot<readonly StatusDistributionReport[]>;
    readonly workload: ReportSnapshot<readonly UserWorkloadReport[]>;
    readonly risks: ReportSnapshot<readonly DueDateRiskReport[]>;
    readonly flow: ReportSnapshot<FlowTimeReport>;
    readonly completion: ReportSnapshot<CompletionRateReport>;
    readonly teams: ReportSnapshot<readonly TeamPerformanceReport[]>;
  };
}

export interface DashboardFilter { readonly rangeDays: number; readonly dueRiskDays: number; readonly assigneeUserId?: string | null; readonly teamId?: string | null; readonly statuses: readonly string[]; }
export interface DashboardWidget { readonly id: string; readonly type: string; readonly title: string; readonly column: number; readonly row: number; readonly width: number; readonly height: number; readonly projectId?: string | null; readonly filter?: DashboardFilter | null; }
export interface Dashboard { readonly id?: string | null; readonly name: string; readonly description?: string | null; readonly scope: 'Personal' | 'Project' | 'Portfolio'; readonly projectIds: readonly string[]; readonly widgets: readonly DashboardWidget[]; readonly filter: DashboardFilter; readonly viewerUserIds: readonly string[]; readonly canEdit: boolean; readonly archived?: boolean; readonly version: number; }
export interface DashboardPage { readonly items: readonly Dashboard[]; readonly total: number; }
export interface DashboardColumn { readonly key: string; readonly label: string; }
export interface DashboardSource { readonly projectId: string; readonly columns: readonly DashboardColumn[]; readonly rows: readonly Readonly<Record<string, string | null>>[]; readonly stale: boolean; }
export interface DashboardRenderedWidget { readonly id: string; readonly type: string; readonly title: string; readonly status: 'Ready' | 'Stale' | 'Degraded'; readonly sources: readonly DashboardSource[]; }
export interface DashboardRender { readonly dashboard: Dashboard; readonly widgets: readonly DashboardRenderedWidget[]; readonly generatedAt?: string | null; readonly stale: boolean; readonly partial: boolean; }
export interface DashboardContext { readonly dashboards: readonly Dashboard[]; readonly users: readonly ProjectWorkItemUser[]; readonly projects: readonly ProjectSummary[]; }

export type RawReport<T> = HttpResponse<{ readonly data: T }>;
