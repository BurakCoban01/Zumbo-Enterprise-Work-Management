import type {
  MobileActivityPage,
  MobileRole,
  MobileTaskActivity,
  MobileTaskAttachment,
  MobileTaskCollaboration,
  MobileTaskComment,
  MobileTaskDetail,
  MobileTaskWorkflow,
  MobileTaskWorkLog,
  MobileUser
} from '../../shell/mobile-workspace.models';

export type MobileTaskDetailTab = 'summary' | 'work' | 'activity';
export type MobileTaskStream = 'activity' | 'attachments' | 'comments' | 'worklogs';

export interface MobileTaskDetailContext {
  readonly detail: MobileTaskDetail;
  readonly collaboration: MobileTaskCollaboration | null;
  readonly workflow: MobileTaskWorkflow | null;
  readonly roles: readonly MobileRole[];
  readonly users: readonly MobileUser[];
  readonly comments: MobileActivityPage<MobileTaskComment>;
  readonly attachments: MobileActivityPage<MobileTaskAttachment>;
  readonly worklogs: MobileActivityPage<MobileTaskWorkLog>;
  readonly activity: MobileActivityPage<MobileTaskActivity>;
  readonly partial: boolean;
}

export interface MobileTaskDraft {
  readonly title: string;
  readonly description: string;
  readonly priority: string;
  readonly dueDate: string;
}

export function emptyPage<T>(items: readonly T[] = []): MobileActivityPage<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length };
}
