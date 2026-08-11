import { projectCatalogAuditEntries, projectCatalogSnapshot, projectVersionName } from '@zumbo/modern-shared';
import { MobileCatalogAudit, MobileCatalogProject, MobileProjectComponent, MobileProjectMilestone, MobileProjectRelease, MobileProjectTemplate, MobileProjectVersion } from './mobile-project-catalog.models';

export interface MobileCatalogSnapshot {
  readonly templates: readonly MobileProjectTemplate[];
  readonly activeTemplates: readonly MobileProjectTemplate[];
  readonly components: readonly MobileProjectComponent[];
  readonly activeComponents: readonly MobileProjectComponent[];
  readonly versions: readonly MobileProjectVersion[];
  readonly plannedVersions: readonly MobileProjectVersion[];
  readonly releases: readonly MobileProjectRelease[];
  readonly milestones: readonly MobileProjectMilestone[];
  readonly openMilestones: readonly MobileProjectMilestone[];
}

export function mobileCatalogSnapshot(project: MobileCatalogProject | null | undefined): MobileCatalogSnapshot {
  return projectCatalogSnapshot(project) as unknown as MobileCatalogSnapshot;
}

export function mobileCatalogAudit(entries: readonly MobileCatalogAudit[]): readonly MobileCatalogAudit[] {
  return [...projectCatalogAuditEntries(entries)].sort((left: MobileCatalogAudit, right: MobileCatalogAudit) => right.createdAt.localeCompare(left.createdAt));
}

export function mobileCatalogVersionLabel(project: MobileCatalogProject | null | undefined, versionId: string): string {
  return projectVersionName(project, versionId);
}

export function mobileCatalogDateInput(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
}

export function mobileCatalogAuditLabel(action: string): string {
  const labels: Readonly<Record<string, string>> = {
    ProjectTemplateCreated: 'Şablon oluşturuldu', ProjectTemplateUpdated: 'Şablon güncellendi', ProjectTemplateArchived: 'Şablon arşivlendi',
    ProjectComponentCreated: 'Bileşen oluşturuldu', ProjectComponentUpdated: 'Bileşen güncellendi', ProjectComponentArchived: 'Bileşen arşivlendi',
    ProjectVersionCreated: 'Sürüm oluşturuldu', ProjectVersionArchived: 'Sürüm arşivlendi', ProjectReleaseCreated: 'Yayın taslağı oluşturuldu',
    ProjectReleaseApproved: 'Yayın onaylandı', ProjectReleasePublished: 'Yayınlandı', ProjectMilestoneCreated: 'Kilometre taşı oluşturuldu',
    ProjectMilestoneUpdated: 'Kilometre taşı güncellendi', ProjectMilestoneCompleted: 'Kilometre taşı tamamlandı'
  };
  return labels[action] ?? 'Teslimat kaydı güncellendi';
}
