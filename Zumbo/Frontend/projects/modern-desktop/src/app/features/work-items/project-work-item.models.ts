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
  readonly teamId?: string | null;
  readonly dueDate?: string | null;
  readonly completedAt?: string | null;
  readonly estimatePoints?: number | null;
  readonly labels: readonly string[];
  readonly relations?: readonly WorkItemRelation[];
  readonly rank: number;
  readonly version: number;
}

export interface WorkItemChecklistEntry { readonly id: string; readonly text: string; readonly completed: boolean; }
export interface WorkItemCommentRevision { readonly body: string; readonly editedByUserId: string; readonly editedAt: string; }
export interface WorkItemComment { readonly id: string; readonly body: string; readonly authorUserId: string; readonly mentions?: readonly string[]; readonly createdAt: string; readonly editedAt?: string | null; readonly history?: readonly WorkItemCommentRevision[]; }
export interface WorkItemWorkLog { readonly id: string; readonly userId: string; readonly hours: number; readonly note?: string | null; readonly createdAt: string; }
export interface WorkItemStatusEntry { readonly fromStatus?: string | null; readonly toStatus: string; readonly changedByUserId: string; readonly changedAt: string; }
export interface WorkItemRelation { readonly relatedWorkItemId: string; readonly relationType: string; }
export interface WorkItemCustomFieldValue {
  readonly fieldKey: string;
  readonly type?: string;
  readonly textValue?: string | null;
  readonly numberValue?: number | null;
  readonly booleanValue?: boolean | null;
  readonly dateValue?: string | null;
  readonly optionKey?: string | null;
}
export interface WorkItemCollaboration { readonly workItemId: string; readonly watcherCount: number; readonly voteCount: number; readonly watching: boolean; readonly voted: boolean; readonly version: number; }
export interface WorkItemActivityEvent { readonly id: string; readonly type: string; readonly actorUserId: string; readonly detail: string; readonly createdAt: string; }
export interface WorkItemActivityPage<T> { readonly items: readonly T[]; readonly page: number; readonly pageSize: number; readonly totalCount: number; }
export interface WorkItemAttachment { readonly id: string; readonly fileName: string; readonly contentType: string; readonly sizeBytes: number; readonly createdAt: string; readonly securityState: string; readonly scanProvider: string; readonly scannedAt?: string | null; }
export interface WorkItemApproval { readonly id: string; readonly fromStatus: string; readonly toStatus: string; readonly requestedByUserId: string; readonly requestedAt: string; readonly expiresAt: string; readonly status: string; readonly decidedByUserId?: string | null; readonly decidedAt?: string | null; readonly note?: string | null; readonly consumedAt?: string | null; }
export interface WorkItemDevelopmentLink { readonly id: string; readonly mappingId: string; readonly repositoryFullName: string; readonly kind: string; readonly externalId: string; readonly title: string; readonly url: string; readonly branch?: string | null; readonly commitSha?: string | null; readonly status: string; readonly source: string; readonly connectionActive: boolean; readonly createdAtUtc: string; readonly updatedAtUtc: string; readonly version: number; }
export interface WorkItemDevelopmentMapping { readonly id: string; readonly repositoryFullName: string; readonly repositoryUrl: string; readonly isActive: boolean; }

export interface ProjectWorkItemDetail extends ProjectWorkItem {
  readonly parentId?: string | null;
  readonly teamId?: string | null;
  readonly completedAt?: string | null;
  readonly archived?: boolean;
  readonly checklist: readonly WorkItemChecklistEntry[];
  readonly comments: readonly WorkItemComment[];
  readonly attachments?: readonly WorkItemAttachment[];
  readonly approvals?: readonly WorkItemApproval[];
  readonly workLogs: readonly WorkItemWorkLog[];
  readonly statusHistory: readonly WorkItemStatusEntry[];
  readonly relations: readonly WorkItemRelation[];
  readonly customFields?: readonly WorkItemCustomFieldValue[];
}

export interface WorkItemIssueType {
  readonly key: string;
  readonly name: string;
  readonly hierarchyLevel: string;
  readonly active: boolean;
  readonly position: number;
}

export interface WorkItemCustomFieldDefinition {
  readonly key: string;
  readonly name: string;
  readonly type: 'Text' | 'Number' | 'Boolean' | 'Date' | 'Select' | string;
  readonly required: boolean;
  readonly indexed: boolean;
  readonly maxLength?: number | null;
  readonly minimum?: number | null;
  readonly maximum?: number | null;
  readonly options?: readonly string[] | null;
  readonly appliesToIssueTypes?: readonly string[] | null;
  readonly position: number;
}

export interface WorkItemIssueTypeLayout { readonly issueTypeKey: string; readonly fieldKeys: readonly string[]; }

export interface WorkItemSchema {
  readonly projectId: string;
  readonly schemaVersion?: number;
  readonly issueTypes: readonly WorkItemIssueType[];
  readonly customFields?: readonly WorkItemCustomFieldDefinition[];
  readonly layouts?: readonly WorkItemIssueTypeLayout[];
  readonly version?: number;
}

export interface WorkItemSprintOption { readonly id: string; readonly name: string; readonly status: string; readonly startDate: string; readonly endDate: string; }
export interface WorkItemSprintPage { readonly items: readonly WorkItemSprintOption[]; readonly nextCursor?: string | null; }

export interface CreateProjectWorkItem {
  readonly projectId: string;
  readonly boardId: string;
  readonly title: string;
  readonly type: string;
  readonly priority: string;
  readonly assigneeUserId: string | null;
  readonly dueDate: string | null;
  readonly parentId: string | null;
  readonly teamId: string | null;
  readonly customFields: readonly WorkItemCustomFieldValue[];
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
  readonly requiresApproval?: boolean;
}

export interface ProjectWorkflowStatus {
  readonly name: string;
  readonly category?: string | null;
  readonly position?: number;
}

export interface ProjectWorkflow {
  readonly transitions: readonly ProjectWorkflowTransition[];
  readonly statuses?: readonly ProjectWorkflowStatus[];
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
