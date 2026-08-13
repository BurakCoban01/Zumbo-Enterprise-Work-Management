import { AuditChange, AuditEntry, AuditFilters, AuditIntegrity, AuditIntegrityState, AuditProject, AuditRole, AuditUser, AuditUserContext } from './audit.models';

const ACTION_LABELS: Readonly<Record<string, string>> = {
  AccountAnonymized: 'Hesap anonimleştirildi', AutomationDraftSaved: 'Otomasyon taslağı kaydedildi', AutomationPublished: 'Otomasyon yayınlandı', AutomationStateChanged: 'Otomasyon durumu değiştirildi',
  BoardCreated: 'Pano oluşturuldu', BoardUpdated: 'Pano güncellendi', DashboardCreated: 'Pano raporu oluşturuldu', DashboardSharingChanged: 'Pano raporu paylaşımı değiştirildi', DashboardUpdated: 'Pano raporu güncellendi',
  IntakeFormCreated: 'Talep formu oluşturuldu', IntakeFormPublished: 'Talep formu yayınlandı', IntakeSubmissionReceived: 'Talep alındı', IntakeSubmissionRouted: 'Talep işe dönüştürüldü', IntakeSubmissionTriaged: 'Talep değerlendirildi',
  OrganizationCreated: 'Organizasyon oluşturuldu', OrganizationUpdated: 'Organizasyon güncellendi', ProjectCreated: 'Proje oluşturuldu', ProjectMemberAdded: 'Proje üyesi eklendi', ProjectMemberRemoved: 'Proje üyesi kaldırıldı', ProjectUpdated: 'Proje güncellendi', ProjectVersionArchived: 'Proje sürümü arşivlendi', ProjectVersionCreated: 'Proje sürümü oluşturuldu',
  SprintWorkItemUnplanned: 'İş sprintten çıkarıldı', TeamCreated: 'Ekip oluşturuldu', TeamUpdated: 'Ekip güncellendi', UserRegistered: 'Kullanıcı kaydı oluşturuldu', UserRolesChanged: 'Kullanıcı rolleri değiştirildi',
  WorkItemArchived: 'İş arşivlendi', WorkItemAttachmentDeleted: 'İş eki silindi', WorkItemAttachmentUploaded: 'İş eki yüklendi', WorkItemBulkJobCompleted: 'Toplu iş tamamlandı', WorkItemBulkJobCreated: 'Toplu iş başlatıldı', WorkItemCommentAdded: 'İşe yorum eklendi', WorkItemCommentDeleted: 'İş yorumu silindi', WorkItemCommentEdited: 'İş yorumu düzenlendi', WorkItemCreated: 'İş oluşturuldu', WorkItemLinked: 'İş bağlantısı eklendi', WorkItemMoved: 'İş taşındı', WorkItemUnlinked: 'İş bağlantısı kaldırıldı', WorkItemUnwatched: 'İş takibi bırakıldı', WorkItemUpdated: 'İş güncellendi', WorkItemVoteRemoved: 'İş oyu kaldırıldı', WorkItemVoted: 'İşe oy verildi', WorkItemWatched: 'İş takibe alındı'
};
const ENTITY_LABELS: Readonly<Record<string, string>> = { AutomationRule: 'Otomasyon', Board: 'Pano', Dashboard: 'Pano raporu', Identity: 'Kullanıcı', IntakeForm: 'Talep formu', IntakeSubmission: 'Talep', Organization: 'Organizasyon', Project: 'Proje', Team: 'Ekip', WorkItem: 'İş', WorkItemBulkJob: 'Toplu iş' };
const FIELD_LABELS: Readonly<Record<string, string>> = { assigneeUserId: 'Sorumlu', name: 'Ad', priority: 'Öncelik', role: 'Rol', status: 'Durum', title: 'Başlık', value: 'Değer' };
const VALUE_LABELS: Readonly<Record<string, string>> = {
  Archived: 'Arşivlendi', Completed: 'Tamamlandı', Draft: 'Taslak', InProgress: 'Devam ediyor', InReview: 'İncelemede',
  New: 'Yeni', Open: 'Açık', Planned: 'Planlandı', Processing: 'İşleniyor', Published: 'Yayında', Rejected: 'Reddedildi', Resolved: 'Çözüldü'
};
const INTERNAL_ID_PATTERN = /\b[0-9a-f]{32}\b/gi;

export function hasAuditPermission(roles: readonly AuditRole[], context: AuditUserContext, permission: 'AuditRead' | 'AuditReadAll'): boolean {
  return roles.some(role => role.isActive && context.roleNames.includes(role.name) && role.permissions.some(value => value === '*' || value === permission));
}

export function defaultAuditFilters(now = new Date()): AuditFilters {
  const from = new Date(now);
  from.setDate(from.getDate() - 30);
  return { actorUserId: '', action: '', entityType: '', entityId: '', from: dateInput(from), to: dateInput(now) };
}

export function auditQuery(filters: AuditFilters, organizationId: string, cursor?: string | null, includePageSize = true): string {
  const entityType = bounded(filters.entityType, 80);
  const entityId = bounded(filters.entityId, 200);
  if (!!entityType !== !!entityId) throw new Error('Kaynak türü ve kaynak kimliği birlikte girilmelidir.');
  const from = boundary(filters.from, false);
  const to = boundary(filters.to, true);
  if (from && to) {
    const range = Date.parse(to) - Date.parse(from);
    if (range < 0) throw new Error('Bitiş tarihi başlangıçtan önce olamaz.');
    if (range > 366 * 86_400_000) throw new Error('Tarih aralığı 366 günü geçemez.');
  }
  const values: Record<string, string> = { actorUserId: bounded(filters.actorUserId, 128), action: bounded(filters.action, 120), entityType, entityId, from, to, organizationId: bounded(organizationId, 200), pageSize: includePageSize ? '50' : '', cursor: bounded(cursor ?? '', 2000) };
  const query = Object.entries(values).filter(([, value]) => value).map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`).join('&');
  return query ? `?${query}` : '';
}

export const auditActionLabel = (action: string): string => ACTION_LABELS[action] ?? 'Denetim olayı';
export const auditFieldLabel = (field: string): string => FIELD_LABELS[field] ?? 'Değişiklik';
export const shortId = (value: string): string => value.length > 18 ? `${value.slice(0, 8)}…${value.slice(-6)}` : value;

export function userName(id: string, users: readonly AuditUser[]): string {
  const user = users.find(item => item.id === id);
  return user?.username || user?.email || (id ? `Kullanıcı · ${shortId(id)}` : 'Sistem işlemi');
}

export function auditEntityLabel(entry: AuditEntry, projects: readonly AuditProject[], users: readonly AuditUser[]): string {
  if (entry.entityType === 'Project') return projects.find(project => project.id === entry.entityId)?.name ?? `Proje · ${shortId(entry.entityId)}`;
  if (entry.entityType === 'Identity') return userName(entry.entityId, users);
  return `${ENTITY_LABELS[entry.entityType] ?? 'Kaynak'} · ${shortId(entry.entityId)}`;
}

export function safeAuditChanges(entry: AuditEntry | null): readonly AuditChange[] {
  return (entry?.changes ?? []).slice(0, 50).map(change => ({ field: bounded(change.field, 120) || 'Değişiklik', oldValue: safeValue(change.oldValue, change.redacted), newValue: safeValue(change.newValue, change.redacted), redacted: change.redacted }));
}

export function integrityState(result: AuditIntegrity | null): AuditIntegrityState {
  if (!result) return 'unknown';
  if (!result.verified) return 'empty';
  if (!result.valid) return 'invalid';
  return result.completeHistory ? 'valid' : 'partial';
}

function safeValue(value: string | null | undefined, redacted: boolean): string | null {
  if (value === null || value === undefined || value === '') return null;
  if (redacted) return '[GİZLENDİ]';
  const text = String(value).slice(0, 500);
  const linkedState = /^([A-Za-z]+):[0-9a-f]{32}$/i.exec(text);
  if (linkedState && VALUE_LABELS[linkedState[1]]) return `${VALUE_LABELS[linkedState[1]]} · iş kaydı oluşturuldu`;
  return (VALUE_LABELS[text] ?? text).replace(INTERNAL_ID_PATTERN, 'iç kimlik gizlendi');
}
function bounded(value: string, maximum: number): string { return String(value ?? '').trim().slice(0, maximum); }
function boundary(value: string, endOfDay: boolean): string {
  if (!value) return '';
  const date = new Date(`${value}T${endOfDay ? '23:59:59.999' : '00:00:00.000'}`);
  if (!Number.isFinite(date.getTime())) throw new Error('Tarih filtresi geçersiz.');
  return date.toISOString();
}
function dateInput(value: Date): string { return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`; }
