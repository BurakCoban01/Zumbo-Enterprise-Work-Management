export interface MobileProject {
  readonly id: string;
  readonly key: string;
  readonly name: string;
  readonly members?: readonly { readonly userId: string; readonly role: string }[];
}

export interface MobileWorkItemRecord {
  readonly id: string;
  readonly projectId: string;
  readonly boardId?: string | null;
  readonly columnId?: string | null;
  readonly sprintId?: string | null;
  readonly title: string;
  readonly description?: string | null;
  readonly type: string;
  readonly status: string;
  readonly priority: string;
  readonly assigneeUserId?: string | null;
  readonly dueDate?: string | null;
  readonly completedAt?: string | null;
  readonly estimatePoints?: number | null;
  readonly labels?: readonly string[];
  readonly relations?: readonly { readonly relationType?: string | null }[];
  readonly approvals?: readonly { readonly status: string }[];
  readonly version?: number;
}

export interface MobileWorkItem extends MobileWorkItemRecord {
  readonly projectName: string;
}

export interface MobileNotification {
  readonly id: string;
  readonly type: string;
  readonly message: string;
  readonly read: boolean;
  readonly createdAt: string;
  readonly category?: string | null;
  readonly actionKind?: string | null;
  readonly sourceId?: string | null;
  readonly projectId?: string | null;
}

export interface MobileRole {
  readonly name: string;
  readonly permissions: readonly string[];
  readonly isActive: boolean;
}

export interface MobileBoard { readonly id: string; readonly projectId?: string; readonly name: string; }
export interface MobileIssueType { readonly key: string; readonly name: string; readonly active: boolean; readonly position: number; }
export interface MobileSchema { readonly issueTypes: readonly MobileIssueType[]; }
export interface MobileSearchResult {
  readonly items: readonly MobileWorkItemRecord[];
  readonly totalCount?: number;
  readonly degraded?: boolean;
}

export interface MobileWorkflowStatus { readonly name: string; readonly position?: number; }
export interface MobileWorkflow { readonly statuses: readonly MobileWorkflowStatus[]; }

export interface MobileTaskDetail extends MobileWorkItemRecord {
  readonly checklist?: readonly { readonly id: string; readonly text: string; readonly completed: boolean }[];
  readonly comments?: readonly MobileTaskComment[];
  readonly attachments?: readonly MobileTaskAttachment[];
  readonly workLogs?: readonly MobileTaskWorkLog[];
}

export interface MobileTaskComment {
  readonly id: string;
  readonly body: string;
  readonly authorUserId: string;
  readonly createdAt: string;
}

export interface MobileTaskAttachment {
  readonly id: string;
  readonly fileName: string;
  readonly sizeBytes: number;
  readonly securityState: string;
  readonly createdAt?: string;
}

export interface MobileTaskWorkLog {
  readonly id: string;
  readonly userId: string;
  readonly hours: number;
  readonly note?: string | null;
  readonly createdAt: string;
}

export interface MobileTaskActivity {
  readonly id: string;
  readonly type: string;
  readonly detail: string;
  readonly actorUserId: string;
  readonly createdAt: string;
}

export interface MobileActivityPage<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export interface MobileTaskCollaboration {
  readonly watcherCount: number;
  readonly voteCount: number;
  readonly watching: boolean;
  readonly voted: boolean;
  readonly version: number;
}

export interface MobileWorkflowTransition {
  readonly fromStatus: string;
  readonly toStatus: string;
  readonly requiresApproval?: boolean;
}

export interface MobileTaskWorkflow extends MobileWorkflow {
  readonly transitions: readonly MobileWorkflowTransition[];
}

export interface MobileUser {
  readonly id: string;
  readonly username?: string;
  readonly email?: string;
}

export type MobileWorkMode = 'assigned' | 'due' | 'blocked' | 'recent';
export type MobileInboxMode = 'unread' | 'actions' | 'all';

export function isOpen(item: MobileWorkItem): boolean { return !item.completedAt; }
export function isBlocked(item: MobileWorkItem): boolean {
  return (item.relations ?? []).some(relation => ['blockedby', 'isblockedby', 'dependson'].includes(String(relation.relationType ?? '').toLowerCase()));
}
export function dueTime(item: MobileWorkItem): number { return item.dueDate ? new Date(item.dueDate).getTime() : Number.MAX_SAFE_INTEGER; }
export function notificationLabel(item: MobileNotification): string {
  return ({ Mention: 'Bahsetme', Assignment: 'Atama', ApprovalRequest: 'Onay isteği', Approval: 'Onay sonucu', DueDateReminder: 'Tarih hatırlatması', TeamInvitation: 'Ekip daveti' } as Record<string, string>)[item.type] ?? 'Bildirim';
}
