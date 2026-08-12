export interface ProjectSummaryMetrics {
  readonly total: number;
  readonly done: number;
  readonly inProgress: number;
  readonly overdue: number;
}

export interface ProjectRiskItem {
  readonly id: string;
  readonly title: string;
  readonly assigneeUserId?: string | null;
  readonly dueDate?: string | null;
  readonly status: string;
}

export interface ProjectSprint {
  readonly id: string;
  readonly name: string;
  readonly goal?: string | null;
  readonly startDate?: string | null;
  readonly endDate?: string | null;
  readonly status: string;
}

export interface ProjectSprintPage {
  readonly items: readonly ProjectSprint[];
}

export interface ProjectAuditEntry {
  readonly id: string;
  readonly actorUserId?: string | null;
  readonly action: string;
  readonly createdAt: string;
}

export interface OverviewUser {
  readonly id: string;
  readonly username?: string | null;
  readonly email?: string | null;
}

export interface OverviewRole {
  readonly name: string;
  readonly displayName: string;
  readonly isActive: boolean;
  readonly isProtected: boolean;
  readonly permissions: readonly string[];
}

export interface OverviewTeam {
  readonly id: string;
  readonly name: string;
}

export interface ProjectOverviewData {
  readonly summary: ProjectSummaryMetrics;
  readonly risks: readonly ProjectRiskItem[];
  readonly sprints: readonly ProjectSprint[];
  readonly activity: readonly ProjectAuditEntry[];
  readonly users: readonly OverviewUser[];
  readonly roles: readonly OverviewRole[];
  readonly teams: readonly OverviewTeam[];
  readonly partial: boolean;
}
