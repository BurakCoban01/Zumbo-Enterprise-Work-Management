export interface WorkItemRelation {
  readonly relationType?: string | null;
}

export interface WorkItemActivity {
  readonly changedAt?: string | null;
  readonly createdAt?: string | null;
  readonly editedAt?: string | null;
}

export interface WorkItemApproval {
  readonly status: string;
}

export interface PersonalWorkItem {
  readonly id: string;
  readonly projectId: string;
  readonly boardId?: string | null;
  readonly title: string;
  readonly status: string;
  readonly priority: string;
  readonly dueDate?: string | null;
  readonly completedAt?: string | null;
  readonly relations?: readonly WorkItemRelation[];
  readonly statusHistory?: readonly WorkItemActivity[];
  readonly comments?: readonly WorkItemActivity[];
  readonly workLogs?: readonly WorkItemActivity[];
  readonly approvals?: readonly WorkItemApproval[];
  readonly projectName: string;
  readonly personalActivityAt: string;
}

export interface WorkItemSearchResult {
  readonly items: readonly Omit<PersonalWorkItem, 'projectName' | 'personalActivityAt'>[];
  readonly totalCount?: number | null;
}

export interface PersonalWorkPage {
  readonly tasks: readonly PersonalWorkItem[];
  readonly partial: boolean;
  readonly hasMore: boolean;
  readonly page: number;
}

export type PersonalMode = 'assigned' | 'due' | 'blocked' | 'recent';
export type PersonalSort = 'urgency' | 'project' | 'recent';

export interface SavedPersonalView {
  readonly id: string;
  readonly name: string;
  readonly mode: PersonalMode;
}
