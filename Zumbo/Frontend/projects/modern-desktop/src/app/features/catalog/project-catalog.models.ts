import { ProjectSummary } from '../../shell/desktop-shell.models';

export type ProjectCatalogTab = 'releases' | 'milestones' | 'components' | 'templates' | 'activity';
export interface ProjectTemplate { readonly id: string; readonly name: string; readonly isDefault: boolean; readonly archived: boolean; readonly defaultComponentNames: readonly string[]; }
export interface ProjectComponent { readonly id: string; readonly name: string; readonly description?: string | null; readonly archived: boolean; }
export interface ProjectVersion { readonly id: string; readonly name: string; readonly status: string; readonly releasedAt?: string | null; }
export interface ProjectRelease { readonly id: string; readonly versionId: string; readonly name: string; readonly status: string; readonly scheduledAt?: string | null; readonly approvedAt?: string | null; readonly publishedAt?: string | null; }
export interface ProjectMilestone { readonly id: string; readonly name: string; readonly dueAt: string; readonly status: string; readonly completedAt?: string | null; }
export interface ProjectCatalogProject extends ProjectSummary { readonly templates: readonly ProjectTemplate[]; readonly components: readonly ProjectComponent[]; readonly versions: readonly ProjectVersion[]; readonly releases: readonly ProjectRelease[]; readonly milestones: readonly ProjectMilestone[]; }
export interface ProjectCatalogRole { readonly name: string; readonly displayName: string; readonly permissions: readonly string[]; readonly isActive: boolean; readonly isProtected: boolean; }
export interface ProjectCatalogUser { readonly id: string; readonly username?: string | null; readonly email?: string | null; }
export interface ProjectCatalogAudit { readonly id: string; readonly action: string; readonly actorUserId?: string | null; readonly createdAt: string; }
export interface ProjectCatalogData { readonly project: ProjectCatalogProject; readonly roles: readonly ProjectCatalogRole[]; readonly users: readonly ProjectCatalogUser[]; readonly audit: readonly ProjectCatalogAudit[]; }
export interface TemplateDraft { readonly id?: string | null; readonly name: string; readonly isDefault: boolean; readonly defaultComponentNamesText: string; }
export interface ComponentDraft { readonly id?: string | null; readonly name: string; readonly description: string; }
export interface MilestoneDraft { readonly id?: string | null; readonly name: string; readonly dueAt: string; }
export interface ReleaseDraft { readonly versionId: string; readonly name: string; readonly scheduledAt: string; }
