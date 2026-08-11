export interface ProjectWorkItem {
  readonly id: string;
  readonly projectId: string;
  readonly boardId: string;
  readonly columnId?: string | null;
  readonly sprintId?: string | null;
  readonly title: string;
  readonly description?: string | null;
  readonly type: string;
  readonly priority: string;
  readonly status: string;
  readonly assigneeUserId?: string | null;
  readonly dueDate?: string | null;
  readonly estimatePoints?: number | null;
  readonly labels: readonly string[];
  readonly rank: number;
  readonly version: number;
}

export interface ProjectWorkItemSearchResult {
  readonly items: readonly ProjectWorkItem[];
  readonly totalCount: number;
  readonly degraded?: boolean;
}

export interface ProjectWorkItemUser {
  readonly id: string;
  readonly username?: string | null;
  readonly email?: string | null;
}

export interface ProjectWorkItemRole {
  readonly name: string;
  readonly isActive: boolean;
  readonly permissions: readonly string[];
}

export interface ProjectWorkflowTransition {
  readonly fromStatus: string;
  readonly toStatus: string;
}

export interface ProjectWorkflow {
  readonly transitions: readonly ProjectWorkflowTransition[];
}

export interface ProjectWorkItemCollection {
  readonly tasks: readonly ProjectWorkItem[];
  readonly totalCount: number;
  readonly degraded: boolean;
  readonly users: readonly ProjectWorkItemUser[];
  readonly roles: readonly ProjectWorkItemRole[];
}

export interface ProjectWorkItemUpdate {
  readonly title: string;
  readonly description: string;
  readonly priority: string;
  readonly dueDate: string | null;
}

export interface BulkWorkItemResult {
  readonly workItemId: string;
  readonly success: boolean;
  readonly errorCode?: string | null;
  readonly errorMessage?: string | null;
}

export interface BulkWorkItemResponse {
  readonly results: readonly BulkWorkItemResult[];
  readonly succeeded: number;
  readonly failed: number;
}
