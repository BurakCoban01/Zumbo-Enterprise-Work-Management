import { MobileBulkJob, MobileImportRow, MobileJobRole, MobileJobState, MobileParsedImport } from './mobile-jobs.models';

export const mobileJobLimits = Object.freeze({ maxInputItems: 5000, maxInputBytes: 5 * 1024 * 1024, maxTitleLength: 200 });
const terminalStates = new Set(['Completed', 'CompletedWithErrors', 'Cancelled', 'Failed']);

export function hasMobileJobPermission(roleName: string | null, roles: readonly MobileJobRole[], permission: string): boolean {
  const role = roles.find(item => item.name === roleName && item.isActive);
  return !!role?.permissions.some(item => item === '*' || item === permission);
}

export function parseMobileImport(text: string, byteLength: number): MobileParsedImport {
  if (byteLength > mobileJobLimits.maxInputBytes) return invalid('Dosya 5 MB sınırını aşıyor.');
  let source: unknown;
  try { source = JSON.parse(text); } catch { return invalid('Dosya geçerli JSON içermiyor.'); }
  const values = Array.isArray(source)
    ? source
    : source && typeof source === 'object' && Array.isArray((source as { items?: unknown }).items)
      ? (source as { items: unknown[] }).items
      : null;
  if (!values) return invalid('JSON kökünde bir satır dizisi veya items dizisi bulunmalı.');
  if (!values.length || values.length > mobileJobLimits.maxInputItems) return invalid('Dosya 1 ile 5.000 satır arasında olmalı.');

  const keys = new Set<string>();
  const rows: MobileImportRow[] = [];
  const errors: string[] = [];
  values.forEach((value, index) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
      errors.push(`${index + 1}. satır bir nesne olmalı.`);
      return;
    }
    const item = value as Record<string, unknown>;
    const row: MobileImportRow = {
      sourceKey: stringValue(item['sourceKey']), boardId: stringValue(item['boardId']), title: stringValue(item['title']),
      type: stringValue(item['type']) || 'Task', priority: stringValue(item['priority']) || 'Medium',
      assigneeUserId: optional(item['assigneeUserId']), dueDate: optional(item['dueDate']),
      parentId: optional(item['parentId']), teamId: optional(item['teamId']),
      customFields: Array.isArray(item['customFields']) ? item['customFields'] : []
    };
    if (!row.sourceKey || !row.boardId || !row.title) errors.push(`${index + 1}. satırda sourceKey, boardId ve title zorunlu.`);
    else if (row.title.length > mobileJobLimits.maxTitleLength) errors.push(`${index + 1}. satır başlığı 200 karakteri aşıyor.`);
    else if (keys.has(row.sourceKey)) errors.push(`${index + 1}. satırda yinelenen sourceKey: ${row.sourceKey}`);
    else { keys.add(row.sourceKey); rows.push(row); }
  });
  return { valid: errors.length === 0, rows, errors: errors.slice(0, 20), totalErrors: errors.length };
}

export function isMobileJobTerminal(job: MobileBulkJob): boolean { return terminalStates.has(job.state); }
export function mobileJobIsActive(job: MobileBulkJob): boolean { return !isMobileJobTerminal(job); }
export function canCancelMobileJob(job: MobileBulkJob): boolean { return mobileJobIsActive(job) && !job.cancelRequested; }
export function canRetryMobileJob(job: MobileBulkJob): boolean { return ['Failed', 'CompletedWithErrors'].includes(job.state); }
export function mobileJobArtifactsExpired(job: MobileBulkJob, now = Date.now()): boolean {
  const expires = Date.parse(job.artifactsExpireAt ?? '');
  return Number.isFinite(expires) && expires <= now;
}
export function mobileJobProgress(job: MobileBulkJob): number {
  const total = Math.max(0, job.totalItems || 0);
  const processed = Math.min(total, Math.max(0, job.processedItems || 0));
  return total ? Math.round(processed * 100 / total) : isMobileJobTerminal(job) ? 100 : 0;
}
export function mobileJobState(job: MobileBulkJob): MobileJobState {
  if (mobileJobArtifactsExpired(job)) return { label: 'Dosyaların süresi doldu', tone: 'muted' };
  const states: Readonly<Record<string, MobileJobState>> = {
    Pending: { label: 'Sırada', tone: 'neutral' },
    Running: { label: job.cancelRequested ? 'İptal bekleniyor' : 'Çalışıyor', tone: 'info' },
    Completed: { label: job.dryRun ? 'Önizleme tamamlandı' : 'Tamamlandı', tone: 'success' },
    CompletedWithErrors: { label: 'Kısmen tamamlandı', tone: 'warning' },
    Cancelled: { label: 'İptal edildi', tone: 'muted' },
    Failed: { label: 'Başarısız', tone: 'danger' }
  };
  return states[job.state] ?? { label: job.state || 'Bilinmiyor', tone: 'neutral' };
}
export function mobileJobType(job: MobileBulkJob): string {
  if (job.type === 'Import') return job.dryRun ? 'İçe aktarım önizlemesi' : 'İçe aktarım';
  if (job.type === 'Export') return job.dryRun ? 'Dışa aktarım önizlemesi' : 'Dışa aktarım';
  return ({ Move: 'Durum değişikliği', Assign: 'Atama', Archive: 'Arşivleme' } as Readonly<Record<string, string>>)[job.operation ?? ''] ?? 'Toplu işlem';
}

function stringValue(value: unknown): string { return value == null ? '' : String(value).trim(); }
function optional(value: unknown): string | null { return stringValue(value) || null; }
function invalid(message: string): MobileParsedImport { return { valid: false, rows: [], errors: [message], totalErrors: 1 }; }
