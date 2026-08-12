import type {
  ProjectWorkItem,
  ProjectWorkItemRole,
  ProjectWorkItemUser,
  ProjectWorkflow,
  ProjectWorkflowTransition
} from '../work-items/project-work-item.models';

export type BoardWorkItem = ProjectWorkItem;

export type BoardWorkflowTransition = ProjectWorkflowTransition;
export type BoardWorkflow = ProjectWorkflow;

export type BoardUser = ProjectWorkItemUser;
export type BoardRole = ProjectWorkItemRole;

export interface ProjectBoardData {
  readonly tasks: readonly BoardWorkItem[];
  readonly totalCount: number;
  readonly degraded: boolean;
  readonly workflow: BoardWorkflow;
  readonly users: readonly BoardUser[];
  readonly roles: readonly BoardRole[];
}

export type BoardDropPlacement = 'before' | 'after' | 'end';
