import {
  ProjectWorkItem,
  ProjectWorkItemCollection
} from '../work-items/project-work-item.models';

export interface ProjectSprint {
  readonly id: string;
  readonly projectId: string;
  readonly name: string;
  readonly goal: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly status: 'Planned' | 'Active' | 'Completed' | string;
  readonly committedItems: number;
  readonly committedPoints: number;
  readonly completedItems: number;
  readonly completedPoints: number;
  readonly carryoverItems: number;
  readonly carryoverPoints: number;
  readonly startedAt?: string | null;
  readonly completedAt?: string | null;
  readonly version: number;
}

export interface SprintBacklogItem {
  readonly id: string;
  readonly title: string;
  readonly type: string;
  readonly priority: string;
  readonly estimatePoints: number;
  readonly rank: number;
  readonly version: number;
}

export interface SprintPage<T> {
  readonly items: readonly T[];
  readonly nextCursor?: string | null;
}

export interface SprintVelocity {
  readonly sprintId: string;
  readonly completedItems: number;
  readonly completedPoints: number;
}

export interface SprintBurndownPoint {
  readonly date: string;
  readonly remainingPoints: number;
  readonly remainingItems: number;
}

export interface CreateSprintDraft {
  readonly projectId: string;
  readonly name: string;
  readonly goal: string | null;
  readonly startDate: string;
  readonly endDate: string;
}

export interface PlannedSprintItem {
  readonly workItemId: string;
  readonly sprintId?: string | null;
  readonly estimatePoints: number;
  readonly version: number;
}

export interface ProjectPlanningData extends ProjectWorkItemCollection {
  readonly sprints: readonly ProjectSprint[];
  readonly backlog: readonly SprintBacklogItem[];
  readonly backlogNextCursor?: string | null;
  readonly sprintNextCursor?: string | null;
  readonly velocity: readonly SprintVelocity[];
}

export interface SprintScopeItem extends ProjectWorkItem {
  readonly sprintId?: string | null;
}
