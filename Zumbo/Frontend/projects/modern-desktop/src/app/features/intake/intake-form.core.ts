import { BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { IntakeField, IntakeFieldDraft, IntakeForm, IntakeFormDraft, IntakeRole } from './intake.models';

export const intakeLimits = Object.freeze({ formName: 120, description: 1000, confirmation: 500, fields: 40, fieldLabel: 120, helpText: 500, options: 50 });
export const intakeFieldTypes = Object.freeze([
  { id: 'Text', label: 'Kısa metin' }, { id: 'LongText', label: 'Uzun metin' },
  { id: 'Email', label: 'E-posta' }, { id: 'Number', label: 'Sayı' },
  { id: 'Date', label: 'Tarih' }, { id: 'Choice', label: 'Seçim' },
  { id: 'Checkbox', label: 'Onay kutusu' }, { id: 'Attachment', label: 'Dosya' }
]);

export function hasIntakePermission(roleName: string | null, roles: readonly IntakeRole[], permission: string): boolean {
  const role = roles.find(candidate => candidate.name === roleName && candidate.isActive);
  return !!role?.permissions.some(candidate => candidate === '*' || candidate === permission);
}

export function newIntakeDraft(project: ProjectSummary, boards: readonly BoardSummary[], workItemType: string): IntakeFormDraft {
  return {
    projectId: project.id, name: '', description: '', state: 'Draft',
    definition: {
      accessPolicy: 'Internal', boardId: boards[0]?.id ?? '', workItemType, defaultPriority: 'Medium',
      confirmationMessage: 'Talebiniz alındı.',
      fields: [newField(0, 'Talep başlığı', 'baslik', true), newField(1, 'Açıklama', 'aciklama')],
      mapping: { titleFieldKey: 'baslik', descriptionFieldKey: 'aciklama', priorityFieldKey: '', dueDateFieldKey: '', customFields: [] }
    }
  };
}

export function editIntakeDraft(form: IntakeForm): IntakeFormDraft {
  return {
    id: form.id, projectId: form.projectId, name: form.name, description: form.description ?? '', state: form.state,
    definition: {
      ...form.draft,
      fields: form.draft.fields.map(field => ({ ...field, helpText: field.helpText ?? '', optionsText: field.options.join('\n') })),
      mapping: {
        titleFieldKey: form.draft.mapping.titleFieldKey,
        descriptionFieldKey: form.draft.mapping.descriptionFieldKey ?? '',
        priorityFieldKey: form.draft.mapping.priorityFieldKey ?? '',
        dueDateFieldKey: form.draft.mapping.dueDateFieldKey ?? '',
        customFields: form.draft.mapping.customFields.map(value => ({ ...value }))
      }
    }
  };
}

export function newField(index: number, label = '', key = `alan_${index + 1}`, required = false): IntakeFieldDraft {
  return { key, label, type: index === 1 ? 'LongText' : 'Text', required, helpText: '', optionsText: '' };
}

export function intakeDraftRequest(draft: IntakeFormDraft): unknown {
  const definition = draft.definition;
  const request = {
    name: draft.name.trim(), description: draft.description.trim() || null,
    definition: {
      accessPolicy: definition.accessPolicy, boardId: definition.boardId, workItemType: definition.workItemType,
      defaultPriority: definition.defaultPriority, confirmationMessage: definition.confirmationMessage.trim(),
      fields: definition.fields.map((field, index) => ({
        key: field.key.trim() || keyFromLabel(field.label, index), label: field.label.trim(), type: field.type,
        required: field.required, helpText: field.helpText.trim() || null,
        options: field.type === 'Choice' ? optionsOf(field.optionsText) : []
      })),
      mapping: {
        titleFieldKey: definition.mapping.titleFieldKey,
        descriptionFieldKey: definition.mapping.descriptionFieldKey || null,
        priorityFieldKey: definition.mapping.priorityFieldKey || null,
        dueDateFieldKey: definition.mapping.dueDateFieldKey || null,
        customFields: definition.mapping.customFields.filter(value => value.intakeFieldKey && value.workItemFieldKey)
      }
    }
  };
  return draft.id ? request : { projectId: draft.projectId, ...request };
}

export function validateIntakeDraft(draft: IntakeFormDraft | null): string | null {
  if (!draft?.name.trim() || !draft.definition.boardId) return 'Form adı ve hedef pano gereklidir.';
  if (!draft.definition.fields.length) return 'En az bir alan ekleyin.';
  if (draft.definition.fields.length > intakeLimits.fields) return 'Bir form en fazla 40 alan içerebilir.';
  const fields = (intakeDraftRequest(draft) as { definition: { fields: readonly IntakeField[] } }).definition.fields;
  if (fields.some(field => !/^[a-z][a-z0-9_-]*$/.test(field.key))) return 'Alan anahtarları küçük harfle başlamalı ve benzersiz olmalıdır.';
  if (new Set(fields.map(field => field.key)).size !== fields.length) return 'Alan anahtarları benzersiz olmalıdır.';
  if (fields.some(field => !field.label)) return 'Her alanın görünen adı gereklidir.';
  const title = fields.find(field => field.key === draft.definition.mapping.titleFieldKey);
  if (!title?.required || !['Text', 'LongText'].includes(title.type)) return 'Başlık eşlemesi zorunlu bir metin alanına bağlanmalıdır.';
  if (fields.some(field => field.type === 'Choice' && (!field.options.length || field.options.length > intakeLimits.options))) return 'Seçim alanları 1-50 benzersiz seçenek içermelidir.';
  const mappings = draft.definition.mapping.customFields.filter(value => value.intakeFieldKey && value.workItemFieldKey);
  if (new Set(mappings.map(value => value.intakeFieldKey)).size !== mappings.length || new Set(mappings.map(value => value.workItemFieldKey)).size !== mappings.length) return 'Özel alan eşlemeleri bire bir olmalıdır.';
  return null;
}

export function compatibleIntakeFields(fields: readonly IntakeFieldDraft[], target: 'title' | 'description' | 'priority' | 'dueDate' | 'custom'): readonly IntakeFieldDraft[] {
  const allowed = { title: ['Text', 'LongText'], description: ['Text', 'LongText'], priority: ['Text', 'Choice'], dueDate: ['Date'], custom: ['Text', 'LongText', 'Email', 'Number', 'Date', 'Choice', 'Checkbox'] }[target];
  return fields.filter(field => allowed.includes(field.type));
}

export function intakeStateLabel(value: string): string { return ({ Draft: 'Taslak', Published: 'Yayında', Archived: 'Arşiv', Processing: 'İşleniyor', New: 'Yeni', InReview: 'İncelemede', Resolved: 'Çözüldü', Rejected: 'Reddedildi' } as Record<string, string>)[value] ?? 'Bilinmiyor'; }
export function intakeAccessLabel(value: string): string { return value === 'Public' ? 'Dış paylaşıma açık' : 'Yalnızca ekip'; }
export function intakeErrorMessage(error: { readonly code?: string; readonly message?: string } | null | undefined, fallback: string): string { const labels: Record<string, string> = { INTAKE_FORM_ARCHIVED: 'Arşivlenmiş form değiştirilemez.', INTAKE_FORM_NOT_FOUND: 'Form bulunamadı.', INTAKE_FORM_PUBLIC_ONLY: 'Dış form yalnız paylaşım bağlantısından gönderilebilir.', INTAKE_SUBMISSION_PROCESSING: 'İş kaydı tamamlanmadan talep sınıflandırılamaz.', CONCURRENCY_CONFLICT: 'Form başka bir kullanıcı tarafından değiştirildi; kayıt yenilendi.', FORBIDDEN: 'Bu işlem için proje yetkiniz yok.', VALIDATION_ERROR: 'Form alanlarını ve sınırları kontrol edin.' }; return labels[error?.code ?? ''] ?? error?.message ?? fallback; }

function optionsOf(value: string): readonly string[] { const seen = new Set<string>(); return value.split(/[\n,]/).map(option => option.trim()).filter(option => { const key = option.toLocaleLowerCase('tr-TR'); if (!option || seen.has(key)) return false; seen.add(key); return true; }); }
function keyFromLabel(label: string, index: number): string { const key = label.toLocaleLowerCase('tr-TR').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/ı/g, 'i').replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 48); return /^[a-z]/.test(key) ? key : `alan_${index + 1}`; }
