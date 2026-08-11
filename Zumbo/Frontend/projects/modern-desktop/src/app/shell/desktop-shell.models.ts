export interface ProjectSummary {
  readonly id: string;
  readonly key: string;
  readonly name: string;
}

export interface BoardSummary {
  readonly id: string;
  readonly name: string;
}

export interface OrganizationSummary {
  readonly id: string;
  readonly name: string;
}

export interface NotificationSummary {
  readonly id: string;
  readonly read: boolean;
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
  | 'archive'
  | 'bell'
  | 'book'
  | 'briefcase'
  | 'chart'
  | 'chevron-down'
  | 'chevrons-left'
  | 'folder'
  | 'gauge'
  | 'home'
  | 'inbox'
  | 'kanban'
  | 'list'
  | 'logout'
  | 'menu'
  | 'milestone'
  | 'moon'
  | 'search'
  | 'settings'
  | 'star'
  | 'sun'
  | 'target'
  | 'users';

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
