import { ProjectSummary } from '../../shell/desktop-shell.models';

export interface WorkItemRelation {
  readonly relationType?: string | null;
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
  readonly projectName: string;
}

export interface WorkItemSearchResult {
  readonly items: readonly Omit<PersonalWorkItem, 'projectName'>[];
  readonly totalCount?: number | null;
}

export interface HomeNotification {
  readonly id: string;
  readonly read: boolean;
  readonly type: string;
  readonly message: string;
  readonly createdAt: string;
  readonly sourceId?: string | null;
  readonly actionKind?: string | null;
}

export interface HomeData {
  readonly tasks: readonly PersonalWorkItem[];
  readonly notifications: readonly HomeNotification[];
  readonly partial: boolean;
}

export interface ProjectSearchResult {
  readonly project: ProjectSummary;
  readonly result: WorkItemSearchResult | null;
}
