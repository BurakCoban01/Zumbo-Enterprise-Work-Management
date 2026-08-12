import { ProjectWorkItem } from '../work-items/project-work-item.models';
import { Dashboard, DashboardWidget, ProjectReportingData, RawReport, ReportSnapshot, ReportingFreshness, ReportingModel, WorkloadModel } from './project-reporting.models';

export const DASHBOARD_CATALOG = [
  ['ProjectSummary', 'Proje özeti'], ['StatusDistribution', 'Durum dağılımı'], ['UserWorkload', 'İş yükü'],
  ['DueDateRisks', 'Teslimat riskleri'], ['FlowTime', 'Akış süresi'], ['CompletionRate', 'Tamamlama oranı'], ['TeamPerformance', 'Ekip teslimatı']
] as const;

export function reportSnapshot<T>(response: RawReport<T>): ReportSnapshot<T> {
  return {
    data: response.body?.data as T,
    generatedAt: response.headers.get('X-Zumbo-Report-Generated-At'),
    sourceVersion: number(response.headers.get('X-Zumbo-Report-Source-Version')),
    stale: response.headers.get('X-Zumbo-Report-Stale')?.toLowerCase() === 'true',
    ageSeconds: number(response.headers.get('X-Zumbo-Report-Age-Seconds'))
  };
}

export function buildReportingModels(data: ProjectReportingData, userName: (id?: string | null) => string): { workload: WorkloadModel; reports: ReportingModel; freshness: ReportingFreshness } {
  const rows = data.snapshots.workload.data.map(item => {
    const tasks = data.tasks.filter(task => isOpen(task) && task.assigneeUserId === item.userId);
    return { ...item, label: userName(item.userId), estimatedPoints: sum(tasks, task => number(task.estimatePoints)), unestimatedItems: tasks.filter(task => number(task.estimatePoints) <= 0).length, relativeWidth: 0, tasks };
  });
  const unassigned = data.tasks.filter(task => isOpen(task) && !task.assigneeUserId);
  if (unassigned.length) rows.push({
    userId: '', label: 'Atanmamış', openItems: unassigned.length,
    overdueItems: unassigned.filter(task => !!task.dueDate && Date.parse(task.dueDate) < Date.now()).length,
    loggedHours: 0, estimatedPoints: sum(unassigned, task => number(task.estimatePoints)),
    unestimatedItems: unassigned.filter(task => number(task.estimatePoints) <= 0).length, relativeWidth: 0, tasks: unassigned
  });
  const maxOpen = Math.max(1, ...rows.map(row => row.openItems));
  const workloadRows = rows.map(row => ({ ...row, relativeWidth: Math.round(row.openItems / maxOpen * 12) }));
  const statusTotal = sum(data.snapshots.status.data, row => row.count);
  const maxStatus = Math.max(1, ...data.snapshots.status.data.map(row => row.count));
  const snapshots = Object.values(data.snapshots);
  const generated = snapshots.map(value => Date.parse(value.generatedAt ?? '')).filter(Number.isFinite);
  return {
    workload: { rows: workloadRows, totals: { openItems: sum(workloadRows, row => row.openItems), overdueItems: sum(workloadRows, row => row.overdueItems), loggedHours: sum(workloadRows, row => row.loggedHours), unestimatedItems: sum(workloadRows, row => row.unestimatedItems) } },
    reports: {
      summary: data.snapshots.summary.data,
      status: data.snapshots.status.data.map(row => ({ ...row, percent: statusTotal ? Math.round(row.count / statusTotal * 100) : 0, relativeWidth: Math.max(1, Math.round(row.count / maxStatus * 12)) })),
      risks: data.snapshots.risks.data,
      flow: data.snapshots.flow.data,
      completion: data.snapshots.completion.data,
      teams: [...data.snapshots.teams.data].sort((left, right) => left.teamName.localeCompare(right.teamName, 'tr-TR'))
    },
    freshness: { generatedAt: generated.length ? new Date(Math.min(...generated)).toISOString() : null, stale: snapshots.some(value => value.stale), maxAgeSeconds: Math.max(0, ...snapshots.map(value => value.ageSeconds)) }
  };
}

export function createDashboard(projectId: string): Dashboard {
  return { id: null, name: 'Teslimat görünümü', description: '', scope: 'Project', projectIds: [projectId], widgets: [createWidget('ProjectSummary', 0)], filter: { rangeDays: 30, dueRiskDays: 30, statuses: [] }, viewerUserIds: [], version: 0, canEdit: true };
}

export function createWidget(type: string, index: number): DashboardWidget {
  const title = DASHBOARD_CATALOG.find(item => item[0] === type)?.[1] ?? DASHBOARD_CATALOG[0][1];
  return { id: `widget-${Date.now().toString(36)}-${index}`, type, title, column: 1, row: index * 2 + 1, width: 12, height: 2, projectId: null, filter: null };
}

export function normalizeWidgets(widgets: readonly DashboardWidget[]): readonly DashboardWidget[] { return widgets.map((widget, index) => ({ ...widget, column: 1, row: index * 2 + 1, width: 12, height: 2 })); }
export function validateDashboard(value: Dashboard): string | null {
  if (!value.name.trim()) return 'Dashboard adı zorunludur.';
  if (!value.projectIds.length) return 'En az bir proje seçin.';
  if (value.scope === 'Project' && value.projectIds.length !== 1) return 'Proje dashboardu için bir proje seçin.';
  if (value.scope === 'Portfolio' && value.projectIds.length < 2) return 'Portföy dashboardu için en az iki proje seçin.';
  if (!value.widgets.length) return 'En az bir widget ekleyin.';
  if (value.widgets.some(widget => !widget.title.trim())) return 'Her widget için bir başlık girin.';
  if (value.filter.rangeDays < 1 || value.filter.rangeDays > 366) return 'Dönem 1 ile 366 gün arasında olmalıdır.';
  if (value.filter.dueRiskDays < 1 || value.filter.dueRiskDays > 90) return 'Risk günü 1 ile 90 gün arasında olmalıdır.';
  return null;
}

function isOpen(task: ProjectWorkItem): boolean { return !task.completedAt && task.status !== 'Done'; }
function number(value: unknown): number { const parsed = Number(value); return Number.isFinite(parsed) ? parsed : 0; }
function sum<T>(values: readonly T[], selector: (value: T) => number): number { return values.reduce((total, value) => total + selector(value), 0); }
