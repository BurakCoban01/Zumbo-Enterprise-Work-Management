import { MobileRole, MobileWorkItemRecord, MobileWorkflow } from '../../shell/mobile-workspace.models';

export type MobileProjectHubTab = 'overview' | 'board' | 'plan';
export interface MobileProjectSummary { readonly total: number; readonly done: number; readonly inProgress: number; readonly overdue: number; }
export interface MobileProjectRisk { readonly id: string; readonly title: string; readonly dueDate?: string | null; readonly status: string; }
export interface MobileProjectSprint { readonly id: string; readonly name: string; readonly goal?: string | null; readonly startDate?: string | null; readonly endDate?: string | null; readonly status: string; readonly committedItems?: number; readonly completedItems?: number; }
export interface MobileSprintPage<T> { readonly items: readonly T[]; readonly nextCursor?: string | null; }
export interface MobileBacklogItem { readonly id: string; readonly title: string; readonly type: string; readonly priority: string; readonly estimatePoints?: number; }
export interface MobileProjectHubData {
  readonly summary: MobileProjectSummary;
  readonly risks: readonly MobileProjectRisk[];
  readonly tasks: readonly MobileWorkItemRecord[];
  readonly workflow: MobileWorkflow;
  readonly sprints: readonly MobileProjectSprint[];
  readonly backlog: readonly MobileBacklogItem[];
  readonly roles: readonly MobileRole[];
  readonly failures: readonly string[];
}
