export const projectCatalogLimits = Object.freeze({
  templateName: 120,
  defaultComponentCount: 50,
  componentName: 80,
  componentDescription: 500,
  versionName: 80,
  releaseName: 100,
  milestoneName: 100
});

export interface ProjectMemberLike {
  readonly userId: string;
  readonly role: string;
}

export interface ProjectRoleDefinitionLike {
  readonly name: string;
  readonly permissions?: readonly string[];
  readonly isActive?: boolean;
  readonly isProtected?: boolean;
}

export interface CatalogEntityLike {
  readonly id?: string;
  readonly archived?: boolean;
  readonly status?: string;
  readonly dueAt?: string | Date;
  readonly name?: string;
}

export interface ProjectCatalogLike {
  readonly members?: readonly ProjectMemberLike[];
  readonly templates?: readonly CatalogEntityLike[];
  readonly components?: readonly CatalogEntityLike[];
  readonly versions?: readonly CatalogEntityLike[];
  readonly releases?: readonly CatalogEntityLike[];
  readonly milestones?: readonly CatalogEntityLike[];
}

export interface AuditEntryLike {
  readonly action?: string;
}

export function projectRoleOf(project: ProjectCatalogLike | null | undefined, userId: string): string | null {
  return project?.members?.find(candidate => candidate.userId === userId)?.role ?? null;
}

export function projectRoleDefinition(
  role: string | null | undefined,
  definitions: readonly ProjectRoleDefinitionLike[] | null | undefined
): ProjectRoleDefinitionLike | null {
  return definitions?.find(item => item.name === role && item.isActive !== false) ?? null;
}

export function canManageProjectCatalog(
  role: string | null | undefined,
  definitions: readonly ProjectRoleDefinitionLike[] | null | undefined
): boolean {
  const definition = projectRoleDefinition(role, definitions);
  return !!definition?.permissions?.some(permission => permission === '*' || permission === 'BoardManage');
}

export function canReleaseProjectCatalog(
  role: string | null | undefined,
  definitions: readonly ProjectRoleDefinitionLike[] | null | undefined
): boolean {
  return !!projectRoleDefinition(role, definitions)?.isProtected;
}

export function normalizeProjectComponentNames(value: unknown): {
  readonly values: readonly string[];
  readonly tooMany: boolean;
  readonly tooLong: boolean;
} {
  const seen = new Set<string>();
  const values = String(value ?? '')
    .split(/[\n,]/)
    .map(item => item.trim())
    .filter(item => {
      const key = item.toLowerCase();
      if (!item || seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  return {
    values,
    tooMany: values.length > projectCatalogLimits.defaultComponentCount,
    tooLong: values.some(item => item.length > projectCatalogLimits.componentName)
  };
}

export function toProjectCatalogDate(value: string | Date | null | undefined): Date | null {
  if (!value) return null;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

export function projectVersionName(project: ProjectCatalogLike | null | undefined, versionId: string): string {
  return project?.versions?.find(candidate => candidate.id === versionId)?.name ?? 'Bilinmeyen sürüm';
}

export function projectCatalogSnapshot(project: ProjectCatalogLike | null | undefined) {
  const current = project ?? {};
  return {
    templates: [...(current.templates ?? [])],
    activeTemplates: (current.templates ?? []).filter(item => !item.archived),
    components: [...(current.components ?? [])],
    activeComponents: (current.components ?? []).filter(item => !item.archived),
    versions: [...(current.versions ?? [])],
    plannedVersions: (current.versions ?? []).filter(item => item.status === 'Planned'),
    releases: [...(current.releases ?? [])],
    milestones: [...(current.milestones ?? [])].sort((left, right) =>
      new Date(left.dueAt ?? 0).getTime() - new Date(right.dueAt ?? 0).getTime()),
    openMilestones: (current.milestones ?? []).filter(item => item.status === 'Open')
  };
}

export function projectCatalogAuditEntries<T extends AuditEntryLike>(entries: readonly T[] | null | undefined): readonly T[] {
  return (entries ?? []).filter(entry => /^Project(?:Template|Component|Version|Release|Milestone)/.test(entry.action ?? ''));
}

const projectCatalogErrorMessages: Readonly<Record<string, string>> = Object.freeze({
  PROJECT_TEMPLATE_EXISTS: 'Bu adla etkin bir proje şablonu zaten var.',
  PROJECT_DEFAULT_TEMPLATE_REQUIRED: 'Önce başka bir şablonu varsayılan yapın.',
  PROJECT_TEMPLATE_ARCHIVED: 'Arşivlenmiş şablon değiştirilemez.',
  PROJECT_COMPONENT_EXISTS: 'Bu adla etkin bir bileşen zaten var.',
  PROJECT_VERSION_EXISTS: 'Bu adla etkin bir sürüm zaten var.',
  PROJECT_VERSION_RELEASED: 'Yayınlanmış sürüm arşivlenemez.',
  PROJECT_VERSION_HAS_RELEASE: 'Etkin yayını olan sürüm arşivlenemez.',
  PROJECT_RELEASE_EXISTS: 'Bu sürüm için zaten bir yayın var.',
  PROJECT_RELEASE_NOT_DRAFT: 'Yalnızca taslak yayın onaylanabilir.',
  PROJECT_RELEASE_NOT_APPROVED: 'Yayınlamadan önce onay gerekir.',
  PROJECT_MILESTONE_EXISTS: 'Bu adla açık bir kilometre taşı zaten var.',
  PROJECT_MILESTONE_COMPLETED: 'Tamamlanmış kilometre taşı değiştirilemez.',
  CONCURRENCY_CONFLICT: 'Proje başka bir kullanıcı tarafından değiştirildi. Güncel kayıt yeniden yüklendi.',
  FORBIDDEN: 'Bu işlem için proje yetkiniz yok.',
  VALIDATION_ERROR: 'Alanları ve belirtilen sınırları kontrol edin.'
});

export function projectCatalogErrorMessage(
  error: { readonly code?: string; readonly message?: string; readonly data?: { readonly error?: { readonly code?: string } } } | null | undefined,
  fallback: string
): string {
  const code = error?.code ?? error?.data?.error?.code ?? '';
  return projectCatalogErrorMessages[code] ?? error?.message ?? fallback;
}
