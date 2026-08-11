import { MobileProjectIntakeDraft, MobileProjectIntakeField, MobileProjectIntakeFieldDraft, MobileProjectIntakeFieldType, MobileProjectIntakeForm, MobileProjectIntakeMappingItem, MobileProjectIntakeProject, MobileProjectIntakeBoard, MobileProjectIntakePublishedForm, MobileProjectIntakeRole, MobileProjectIntakeSubmission, MobileProjectIntakeSubmissionModel } from './mobile-project-intake.models';

export const mobileProjectIntakeLimits = Object.freeze({ formName: 120, description: 1000, confirmation: 500, fields: 40, fieldLabel: 120, helpText: 500, options: 50, value: 4000 });
export const mobileProjectIntakeFieldTypes: readonly { readonly id: MobileProjectIntakeFieldType; readonly label: string }[] = Object.freeze([
  { id: 'Text', label: 'Kısa metin' }, { id: 'LongText', label: 'Uzun metin' }, { id: 'Email', label: 'E-posta' }, { id: 'Number', label: 'Sayı' },
  { id: 'Date', label: 'Tarih' }, { id: 'Choice', label: 'Seçim' }, { id: 'Checkbox', label: 'Onay kutusu' }, { id: 'Attachment', label: 'Dosya' }
]);
export const mobileProjectIntakeTriageStates = Object.freeze([{ id: 'New', label: 'Yeni' }, { id: 'InReview', label: 'İncelemede' }, { id: 'Resolved', label: 'Çözüldü' }, { id: 'Rejected', label: 'Reddedildi' }]);

export function hasMobileProjectIntakePermission(roleName: string | null, roles: readonly MobileProjectIntakeRole[], permission: string): boolean {
  return !!roles.find(role => role.name === roleName && role.isActive)?.permissions.some(value => value === '*' || value === permission);
}
export function newMobileProjectIntakeDraft(project: MobileProjectIntakeProject, boards: readonly MobileProjectIntakeBoard[], workItemType: string): MobileProjectIntakeDraft {
  return { projectId: project.id, name: '', description: '', state: 'Draft', definition: { accessPolicy: 'Internal', boardId: boards[0]?.id ?? '', workItemType, defaultPriority: 'Medium', confirmationMessage: 'Talebiniz alındı.', fields: [newMobileProjectIntakeField(0, 'Talep başlığı', 'baslik', true), newMobileProjectIntakeField(1, 'Açıklama', 'aciklama')], mapping: { titleFieldKey: 'baslik', descriptionFieldKey: 'aciklama', priorityFieldKey: '', dueDateFieldKey: '', customFields: [] } } };
}
export function editMobileProjectIntakeDraft(form: MobileProjectIntakeForm): MobileProjectIntakeDraft {
  return { id: form.id, projectId: form.projectId, name: form.name, description: form.description ?? '', state: form.state, definition: { ...form.draft, fields: form.draft.fields.map(field => ({ ...field, helpText: field.helpText ?? '', optionsText: field.options.join('\n') })), mapping: { titleFieldKey: form.draft.mapping.titleFieldKey, descriptionFieldKey: form.draft.mapping.descriptionFieldKey ?? '', priorityFieldKey: form.draft.mapping.priorityFieldKey ?? '', dueDateFieldKey: form.draft.mapping.dueDateFieldKey ?? '', customFields: form.draft.mapping.customFields.map(item => ({ ...item })) } } };
}
export function newMobileProjectIntakeField(index: number, label = '', key = `alan_${index + 1}`, required = false): MobileProjectIntakeFieldDraft { return { key, label, type: index === 1 ? 'LongText' : 'Text', required, helpText: '', optionsText: '' }; }
export function mobileProjectIntakeRequest(draft: MobileProjectIntakeDraft): unknown {
  const definition = draft.definition;
  const request = { name: draft.name.trim(), description: draft.description.trim() || null, definition: { accessPolicy: definition.accessPolicy, boardId: definition.boardId, workItemType: definition.workItemType, defaultPriority: definition.defaultPriority, confirmationMessage: definition.confirmationMessage.trim(), fields: definition.fields.map((field, index) => ({ key: field.key.trim() || keyFromLabel(field.label, index), label: field.label.trim(), type: field.type, required: field.required, helpText: field.helpText.trim() || null, options: field.type === 'Choice' ? optionsOf(field.optionsText) : [] })), mapping: { titleFieldKey: definition.mapping.titleFieldKey, descriptionFieldKey: definition.mapping.descriptionFieldKey || null, priorityFieldKey: definition.mapping.priorityFieldKey || null, dueDateFieldKey: definition.mapping.dueDateFieldKey || null, customFields: definition.mapping.customFields.filter(item => item.intakeFieldKey && item.workItemFieldKey) } } };
  return draft.id ? request : { projectId: draft.projectId, ...request };
}
export function validateMobileProjectIntakeDraft(draft: MobileProjectIntakeDraft | null): string | null {
  if (!draft?.name.trim() || !draft.definition.boardId) return 'Form adı ve hedef pano gereklidir.';
  if (!draft.definition.fields.length) return 'En az bir alan ekleyin.';
  if (draft.definition.fields.length > mobileProjectIntakeLimits.fields) return 'Bir form en fazla 40 alan içerebilir.';
  const fields = (mobileProjectIntakeRequest(draft) as { definition: { fields: readonly MobileProjectIntakeField[] } }).definition.fields;
  if (fields.some(field => !/^[a-z][a-z0-9_-]*$/.test(field.key)) || new Set(fields.map(field => field.key)).size !== fields.length) return 'Alan anahtarları küçük harfle başlamalı ve benzersiz olmalıdır.';
  if (fields.some(field => !field.label)) return 'Her alanın görünen adı gereklidir.';
  const title = fields.find(field => field.key === draft.definition.mapping.titleFieldKey);
  if (!title?.required || !['Text', 'LongText'].includes(title.type)) return 'Başlık eşlemesi zorunlu bir metin alanına bağlanmalıdır.';
  if (fields.some(field => field.type === 'Choice' && (!field.options.length || field.options.length > mobileProjectIntakeLimits.options))) return 'Seçim alanları 1-50 benzersiz seçenek içermelidir.';
  const mappings = draft.definition.mapping.customFields.filter(item => item.intakeFieldKey && item.workItemFieldKey);
  if (new Set(mappings.map(item => item.intakeFieldKey)).size !== mappings.length || new Set(mappings.map(item => item.workItemFieldKey)).size !== mappings.length) return 'Özel alan eşlemeleri bire bir olmalıdır.';
  return null;
}
export function compatibleMobileProjectIntakeFields(fields: readonly MobileProjectIntakeFieldDraft[], target: 'title' | 'description' | 'priority' | 'dueDate' | 'custom'): readonly MobileProjectIntakeFieldDraft[] {
  const allowed = { title: ['Text', 'LongText'], description: ['Text', 'LongText'], priority: ['Text', 'Choice'], dueDate: ['Date'], custom: ['Text', 'LongText', 'Email', 'Number', 'Date', 'Choice', 'Checkbox'] }[target];
  return fields.filter(field => allowed.includes(field.type));
}
export function newMobileProjectIntakeSubmission(form: MobileProjectIntakePublishedForm): MobileProjectIntakeSubmissionModel { return { values: Object.fromEntries(form.fields.map(field => [field.key, field.type === 'Checkbox' ? false : ''])), files: {}, website: '' }; }
export function validateMobileProjectIntakeSubmission(form: MobileProjectIntakePublishedForm | null, model: MobileProjectIntakeSubmissionModel | null): string | null {
  if (!form || !model) return 'Form yüklenmeden gönderim yapılamaz.';
  const missing = form.fields.find(field => field.required && (field.type === 'Attachment' ? !(model.files[field.key] ?? []).length : field.type === 'Checkbox' ? model.values[field.key] !== true : String(model.values[field.key] ?? '').trim() === ''));
  if (missing) return `${missing.label} alanı zorunludur.`;
  const files = Object.values(model.files).flat();
  if (files.length > 5) return 'Bir talebe en fazla 5 dosya eklenebilir.';
  if (files.some(file => file.size <= 0 || file.size > 10 * 1024 * 1024)) return 'Her dosya 10 MB veya daha küçük olmalıdır.';
  if (files.reduce((total, file) => total + file.size, 0) > 25 * 1024 * 1024) return 'Dosyaların toplam boyutu 25 MB sınırını aşıyor.';
  return null;
}
export function mobileProjectIntakeSubmissionData(form: MobileProjectIntakePublishedForm, model: MobileProjectIntakeSubmissionModel): FormData { const data = new FormData(); data.append('submission', JSON.stringify({ values: form.fields.filter(field => field.type !== 'Attachment').map(field => ({ fieldKey: field.key, value: String(model.values[field.key] ?? '') })), website: model.website || null })); for (const field of form.fields.filter(field => field.type === 'Attachment')) for (const file of model.files[field.key] ?? []) data.append(`attachments.${field.key}`, file, file.name); return data; }
export function mobileProjectIntakeStateLabel(value: string): string { return ({ Draft: 'Taslak', Published: 'Yayında', Archived: 'Arşiv', Processing: 'İşleniyor', New: 'Yeni', InReview: 'İncelemede', Resolved: 'Çözüldü', Rejected: 'Reddedildi' } as Record<string, string>)[value] ?? 'Bilinmiyor'; }
export function mobileProjectIntakeAccessLabel(value: string): string { return value === 'Public' ? 'Dış paylaşıma açık' : 'Yalnızca ekip'; }
export function mobileProjectIntakeSecurityLabel(value: string): string { return ({ Clean: 'Taramadan geçti', Quarantined: 'Karantinada', Rejected: 'Reddedildi', Pending: 'Taranıyor' } as Record<string, string>)[value] ?? 'Bilinmiyor'; }
export function mobileProjectIntakeSubmissionValue(submission: MobileProjectIntakeSubmission, fieldKey: string): string { return submission.values.find(value => value.fieldKey === fieldKey)?.value ?? ''; }
export function mobileProjectIntakeErrorMessage(code: string | undefined, fallback: string): string { return ({ INTAKE_FORM_ARCHIVED: 'Arşivlenmiş form değiştirilemez.', INTAKE_FORM_NOT_FOUND: 'Form bulunamadı.', INTAKE_FORM_PUBLIC_ONLY: 'Dış form yalnız paylaşım bağlantısından gönderilebilir.', INTAKE_SUBMISSION_PROCESSING: 'İş kaydı tamamlanmadan talep sınıflandırılamaz.', CONCURRENCY_CONFLICT: 'Form başka bir kullanıcı tarafından değiştirildi; kayıt yenilendi.', FORBIDDEN: 'Bu işlem için proje yetkiniz yok.', VALIDATION_ERROR: 'Form alanlarını ve sınırları kontrol edin.' } as Record<string, string>)[code ?? ''] ?? fallback; }
function optionsOf(value: string): readonly string[] { const seen = new Set<string>(); return value.split(/[\n,]/).map(item => item.trim()).filter(item => { const key = item.toLocaleLowerCase('tr-TR'); if (!item || seen.has(key)) return false; seen.add(key); return true; }); }
function keyFromLabel(label: string, index: number): string { const key = label.toLocaleLowerCase('tr-TR').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/ı/g, 'i').replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 48); return /^[a-z]/.test(key) ? key : `alan_${index + 1}`; }
