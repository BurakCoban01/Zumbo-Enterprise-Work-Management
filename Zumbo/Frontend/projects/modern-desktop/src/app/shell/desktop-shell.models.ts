export interface ProjectSummary {
  readonly id: string;
  readonly organizationId?: string;
  readonly key: string;
  readonly name: string;
  readonly visibility?: string;
  readonly members?: readonly ProjectMemberSummary[];
  readonly teamIds?: readonly string[];
  readonly archived?: boolean;
  readonly version?: number;
  readonly milestones?: readonly ProjectMilestoneSummary[];
  readonly releases?: readonly ProjectReleaseSummary[];
}

export interface ProjectMemberSummary {
  readonly userId: string;
  readonly role: string;
}

export interface ProjectMilestoneSummary {
  readonly id: string;
  readonly name: string;
  readonly dueAt?: string | null;
  readonly status: string;
}

export interface ProjectReleaseSummary {
  readonly id: string;
  readonly name: string;
  readonly scheduledAt?: string | null;
  readonly status: string;
}

export interface BoardSummary {
  readonly id: string;
  readonly projectId?: string;
  readonly name: string;
  readonly type?: string;
  readonly swimlaneMode?: string;
  readonly columns?: readonly BoardColumnSummary[];
  readonly version?: number;
}

export interface BoardColumnSummary {
  readonly id: string;
  readonly name: string;
  readonly category?: string;
  readonly position: number;
  readonly wipLimit?: number | null;
  readonly statusNames?: readonly string[];
}

export interface OrganizationSummary {
  readonly id: string;
  readonly name: string;
}

export interface WorkspaceRole {
  readonly name: string;
  readonly permissions: readonly string[];
  readonly isActive: boolean;
}

export type WorkspaceSection =
  | 'home'
  | 'mywork'
  | 'inbox'
  | 'projects'
  | 'portfolios'
  | 'goals'
  | 'capacity'
  | 'knowledge'
  | 'teams'
  | 'audit'
  | 'archive'
  | 'settings';

export type ProjectViewId =
  | 'overview'
  | 'board'
  | 'list'
  | 'backlog'
  | 'sprint'
  | 'calendar'
  | 'timeline'
  | 'roadmap'
  | 'catalog'
  | 'intake'
  | 'automation'
  | 'jobs'
  | 'workload'
  | 'reports'
  | 'dashboards';

export interface ProjectViewDefinition {
  readonly id: ProjectViewId;
  readonly label: string;
  readonly icon: IconName;
  readonly group: 'primary' | 'plan' | 'operate' | 'insights';
  readonly requiresBoard: boolean;
}

export type IconName =
  | 'arrow-down'
  | 'arrow-left'
  | 'arrow-right'
  | 'arrow-up'
  | 'archive'
  | 'arrow-up-right'
  | 'bell'
  | 'book'
  | 'bookmark'
  | 'briefcase'
  | 'chart'
  | 'check'
  | 'check-check'
  | 'columns'
  | 'copy'
  | 'chevron-down'
  | 'chevron-left'
  | 'chevron-right'
  | 'chevrons-left'
  | 'folder'
  | 'edit'
  | 'eye'
  | 'gauge'
  | 'home'
  | 'inbox'
  | 'kanban'
  | 'list'
  | 'link'
  | 'logout'
  | 'menu'
  | 'message-square'
  | 'milestone'
  | 'moon'
  | 'paperclip'
  | 'plus'
  | 'refresh'
  | 'rows'
  | 'search'
  | 'save'
  | 'settings'
  | 'star'
  | 'sun'
  | 'target'
  | 'trash'
  | 'unlink'
  | 'users'
  | 'x';

export const PROJECT_VIEWS: readonly ProjectViewDefinition[] = [
  { id: 'overview', label: 'Genel bakış', icon: 'chart', group: 'primary', requiresBoard: false },
  { id: 'board', label: 'Pano', icon: 'kanban', group: 'primary', requiresBoard: true },
  { id: 'list', label: 'Liste', icon: 'list', group: 'primary', requiresBoard: true },
  { id: 'backlog', label: 'Backlog', icon: 'inbox', group: 'primary', requiresBoard: true },
  { id: 'sprint', label: 'Sprint', icon: 'milestone', group: 'primary', requiresBoard: true },
  { id: 'calendar', label: 'Takvim', icon: 'milestone', group: 'plan', requiresBoard: true },
  { id: 'timeline', label: 'Zaman çizelgesi', icon: 'chart', group: 'plan', requiresBoard: true },
  { id: 'roadmap', label: 'Yol haritası', icon: 'milestone', group: 'plan', requiresBoard: true },
  { id: 'catalog', label: 'Teslimat', icon: 'briefcase', group: 'plan', requiresBoard: false },
  { id: 'intake', label: 'Intake', icon: 'inbox', group: 'operate', requiresBoard: false },
  { id: 'automation', label: 'Otomasyon', icon: 'settings', group: 'operate', requiresBoard: false },
  { id: 'jobs', label: 'İş merkezi', icon: 'briefcase', group: 'operate', requiresBoard: false },
  { id: 'workload', label: 'İş yükü', icon: 'users', group: 'insights', requiresBoard: false },
  { id: 'reports', label: 'Raporlar', icon: 'chart', group: 'insights', requiresBoard: false },
  { id: 'dashboards', label: 'Dashboardlar', icon: 'chart', group: 'insights', requiresBoard: false }
];

export function isWorkspaceSection(value: string | null): value is WorkspaceSection {
  return !!value && ['home', 'mywork', 'inbox', 'projects', 'portfolios', 'goals', 'capacity', 'knowledge', 'teams', 'audit', 'archive', 'settings'].includes(value);
}

export function isProjectView(value: string | null): value is ProjectViewId {
  return !!value && PROJECT_VIEWS.some(candidate => candidate.id === value);
}

export function hasWorkspacePermission(
  roleNames: readonly string[],
  roles: readonly WorkspaceRole[],
  permission: string
): boolean {
  return roles.some(role => role.isActive
    && roleNames.includes(role.name)
    && role.permissions.some(value => value === '*' || value === permission));
}
