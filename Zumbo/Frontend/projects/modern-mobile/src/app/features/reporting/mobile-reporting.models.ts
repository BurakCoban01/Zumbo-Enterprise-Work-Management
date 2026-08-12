import { HttpResponse } from '@angular/common/http';

export type MobileReportingTab = 'workload' | 'reports' | 'dashboards';
export interface MobileReportingProject { readonly id: string; readonly key: string; readonly name: string; readonly members?: readonly { readonly userId: string; readonly role: string }[]; }
export interface MobileReportingRole { readonly name: string; readonly permissions: readonly string[]; readonly isActive: boolean; }
export interface MobileReportingUser { readonly id: string; readonly username?: string | null; readonly email: string; }
export interface MobileReportingWorkflow { readonly statuses: readonly { readonly key: string; readonly name: string; readonly position?: number }[]; }
export interface MobileProjectSummaryReport { readonly total: number; readonly done: number; readonly inProgress: number; readonly overdue: number; }
export interface MobileStatusDistributionReport { readonly status: string; readonly count: number; }
export interface MobileUserWorkloadReport { readonly userId: string; readonly openItems: number; readonly overdueItems: number; readonly loggedHours: number; }
export interface MobileDueDateRiskReport { readonly id: string; readonly title: string; readonly assigneeUserId?: string | null; readonly dueDate: string; readonly status: string; }
export interface MobileFlowTimeReport { readonly completedItems: number; readonly cycleTimeSampleSize: number; readonly medianLeadTimeHours: number; readonly medianCycleTimeHours?: number | null; }
export interface MobileCompletionRateReport { readonly createdItems: number; readonly completedItems: number; readonly completionRatePercent: number; }
export interface MobileTeamPerformanceReport { readonly teamId: string; readonly teamName: string; readonly assignedItems: number; readonly completedItems: number; readonly completionRatePercent: number; readonly averageLeadTimeHours?: number | null; readonly loggedHours: number; }
export interface MobileReportSnapshot<T> { readonly data: T; readonly generatedAt: string | null; readonly sourceVersion: number; readonly stale: boolean; readonly ageSeconds: number; }
export interface MobileReportingData { readonly project: MobileReportingProject; readonly roles: readonly MobileReportingRole[]; readonly users: readonly MobileReportingUser[]; readonly workflow: MobileReportingWorkflow; readonly snapshots: { readonly summary: MobileReportSnapshot<MobileProjectSummaryReport>; readonly status: MobileReportSnapshot<readonly MobileStatusDistributionReport[]>; readonly workload: MobileReportSnapshot<readonly MobileUserWorkloadReport[]>; readonly risks: MobileReportSnapshot<readonly MobileDueDateRiskReport[]>; readonly flow: MobileReportSnapshot<MobileFlowTimeReport>; readonly completion: MobileReportSnapshot<MobileCompletionRateReport>; readonly teams: MobileReportSnapshot<readonly MobileTeamPerformanceReport[]>; }; }
export interface MobileDashboardFilter { readonly rangeDays: number; readonly dueRiskDays: number; readonly assigneeUserId?: string | null; readonly teamId?: string | null; readonly statuses: readonly string[]; }
export interface MobileDashboardWidget { readonly id: string; readonly type: string; readonly title: string; }
export interface MobileDashboard { readonly id?: string | null; readonly name: string; readonly description?: string | null; readonly scope: 'Personal' | 'Project' | 'Portfolio'; readonly projectIds: readonly string[]; readonly widgets: readonly MobileDashboardWidget[]; readonly filter: MobileDashboardFilter; readonly viewerUserIds: readonly string[]; readonly canEdit: boolean; readonly archived?: boolean; readonly version: number; }
export interface MobileDashboardPage { readonly items: readonly MobileDashboard[]; readonly total: number; }
export interface MobileDashboardColumn { readonly key: string; readonly label: string; }
export interface MobileDashboardSource { readonly projectId: string; readonly columns: readonly MobileDashboardColumn[]; readonly rows: readonly Readonly<Record<string, string | null>>[]; readonly stale: boolean; }
export interface MobileDashboardRenderedWidget { readonly id: string; readonly type: string; readonly title: string; readonly status: 'Ready' | 'Stale' | 'Degraded'; readonly sources: readonly MobileDashboardSource[]; }
export interface MobileDashboardRender { readonly dashboard: MobileDashboard; readonly widgets: readonly MobileDashboardRenderedWidget[]; readonly generatedAt?: string | null; readonly stale: boolean; readonly partial: boolean; }
export type MobileRawReport<T> = HttpResponse<{ readonly data: T }>;
