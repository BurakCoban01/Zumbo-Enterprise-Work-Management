import {
  ProjectWorkItemCollection,
  ProjectWorkflow
} from '../work-items/project-work-item.models';

export type ListDensity = 'comfortable' | 'compact';
export type ListSortField = 'rank' | 'title' | 'status' | 'priority' | 'assignee' | 'dueDate';
export type ListSortDirection = 'asc' | 'desc';
export type ListColumn = 'status' | 'priority' | 'assignee' | 'dueDate' | 'estimate';

export interface ProjectListPreferences {
  readonly density: ListDensity;
  readonly sort: ListSortField;
  readonly direction: ListSortDirection;
  readonly columns: Readonly<Record<ListColumn, boolean>>;
}

export interface ProjectListData extends ProjectWorkItemCollection {
  readonly workflow: ProjectWorkflow;
}

export const DEFAULT_LIST_PREFERENCES: ProjectListPreferences = {
  density: 'comfortable',
  sort: 'rank',
  direction: 'asc',
  columns: { status: true, priority: true, assignee: true, dueDate: true, estimate: false }
};
