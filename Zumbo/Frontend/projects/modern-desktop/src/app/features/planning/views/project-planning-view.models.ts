import { ProjectSummary } from '../../../shell/desktop-shell.models';
import { ProjectSprint } from '../project-planning.models';
import { ProjectWorkItem, ProjectWorkItemCollection, ProjectWorkflow } from '../../work-items/project-work-item.models';

export type PlanningViewMode = 'calendar' | 'timeline' | 'roadmap';
export type PlanningCalendarMode = 'month' | 'week' | 'list';
export type PlanningZoom = 'week' | 'month' | 'quarter';

export interface PlanningFilters {
  readonly query: string;
  readonly assignee: string;
  readonly type: string;
}

export interface PlanningViewData extends ProjectWorkItemCollection {
  readonly sprints: readonly ProjectSprint[];
  readonly workflow: ProjectWorkflow;
}

export interface PlanningCalendarEvent {
  readonly id: string;
  readonly key: string;
  readonly kind: string;
  readonly title: string;
  readonly tone: string;
  readonly task?: ProjectWorkItem;
}

export interface PlanningCalendarDay {
  readonly key: string;
  readonly label: string;
  readonly inRange: boolean;
  readonly events: readonly PlanningCalendarEvent[];
}

export interface PlanningSegment {
  readonly status: string;
  readonly category: string;
  readonly tone: string;
  readonly count: number;
  readonly percentage: number;
  readonly order: number;
}

export interface PlanningTimelineRow {
  readonly id: string;
  readonly task: ProjectWorkItem;
  readonly title: string;
  readonly startKey: string;
  readonly endKey: string;
  readonly dueKey: string;
  readonly source: string;
  readonly milestone: boolean;
  readonly blockedBy: readonly string[];
  readonly blocks: readonly string[];
  readonly dependencyRisk: boolean;
  readonly inWindow: boolean;
  readonly column: number;
  readonly span: number;
}

export interface PlanningRoadmapRow {
  readonly id: string;
  readonly kind: string;
  readonly title: string;
  readonly status: string;
  readonly startKey: string;
  readonly endKey: string;
  readonly milestone: boolean;
  readonly segments: readonly PlanningSegment[];
  readonly progress: number;
  readonly progressSource: string;
  readonly inWindow: boolean;
  readonly column: number;
  readonly span: number;
}

export interface PlanningWindow {
  readonly startKey: string;
  readonly endKey: string;
  readonly bucketDays: number;
  readonly buckets: readonly { readonly index: number; readonly startKey: string; readonly endKey: string; readonly label: string }[];
}

export interface PlanningModel {
  readonly anchorKey: string;
  readonly calendarDays: readonly PlanningCalendarDay[];
  readonly calendarEvents: readonly PlanningCalendarEvent[];
  readonly timelineRows: readonly PlanningTimelineRow[];
  readonly unscheduledTasks: readonly ProjectWorkItem[];
  readonly dependencyRisks: readonly { readonly id: string; readonly from: string; readonly to: string; readonly risk: boolean }[];
  readonly roadmapRows: readonly PlanningRoadmapRow[];
  readonly window: PlanningWindow;
  readonly totals: {
    readonly tasks: number;
    readonly done: number;
    readonly overdue: number;
    readonly progress: number;
    readonly projectTasks: number;
    readonly projectDone: number;
    readonly projectProgress: number;
    readonly projectSegments: readonly PlanningSegment[];
  };
}

export interface BuildPlanningModelInput {
  readonly tasks: readonly ProjectWorkItem[];
  readonly sprints: readonly ProjectSprint[];
  readonly project: ProjectSummary;
  readonly workflow: ProjectWorkflow;
  readonly filters: PlanningFilters;
  readonly anchorDate: string | Date;
  readonly calendarMode: Exclude<PlanningCalendarMode, 'list'>;
  readonly zoom: PlanningZoom;
  readonly timeZone: string;
  readonly today?: string | Date;
}
