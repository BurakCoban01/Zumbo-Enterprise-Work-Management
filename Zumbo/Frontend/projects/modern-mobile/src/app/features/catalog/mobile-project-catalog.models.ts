import { ProjectCatalogLike } from '@zumbo/modern-shared';

export type MobileProjectCatalogTab = 'releases' | 'milestones' | 'components' | 'templates' | 'activity';

export interface MobileProjectTemplate { readonly id: string; readonly name: string; readonly isDefault: boolean; readonly archived: boolean; readonly defaultComponentNames: readonly string[]; }
export interface MobileProjectComponent { readonly id: string; readonly name: string; readonly description?: string | null; readonly archived: boolean; }
export interface MobileProjectVersion { readonly id: string; readonly name: string; readonly status: string; readonly releasedAt?: string | null; }
export interface MobileProjectRelease { readonly id: string; readonly versionId: string; readonly name: string; readonly status: string; readonly scheduledAt?: string | null; readonly approvedAt?: string | null; readonly publishedAt?: string | null; }
export interface MobileProjectMilestone { readonly id: string; readonly name: string; readonly dueAt: string; readonly status: string; readonly completedAt?: string | null; }

export interface MobileCatalogProject extends ProjectCatalogLike {
  readonly id: string;
  readonly key: string;
  readonly name: string;
  readonly templates: readonly MobileProjectTemplate[];
  readonly components: readonly MobileProjectComponent[];
  readonly versions: readonly MobileProjectVersion[];
  readonly releases: readonly MobileProjectRelease[];
  readonly milestones: readonly MobileProjectMilestone[];
}

export interface MobileCatalogRole { readonly name: string; readonly displayName: string; readonly permissions: readonly string[]; readonly isActive: boolean; readonly isProtected: boolean; }
export interface MobileCatalogUser { readonly id: string; readonly username?: string | null; readonly email?: string | null; }
export interface MobileCatalogAudit { readonly id: string; readonly action: string; readonly actorUserId?: string | null; readonly createdAt: string; }
export interface MobileProjectCatalogData { readonly project: MobileCatalogProject; readonly roles: readonly MobileCatalogRole[]; readonly users: readonly MobileCatalogUser[]; readonly audit: readonly MobileCatalogAudit[]; }

export interface MobileTemplateDraft { readonly id?: string | null; readonly name: string; readonly isDefault: boolean; readonly defaultComponentNamesText: string; }
export interface MobileComponentDraft { readonly id?: string | null; readonly name: string; readonly description: string; }
export interface MobileMilestoneDraft { readonly id?: string | null; readonly name: string; readonly dueAt: string; }
export interface MobileReleaseDraft { readonly versionId: string; readonly name: string; readonly scheduledAt: string; }
