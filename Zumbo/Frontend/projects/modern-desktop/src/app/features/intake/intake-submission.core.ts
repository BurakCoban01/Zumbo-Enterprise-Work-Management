import { IntakeField, IntakeSubmission, IntakeSubmissionModel, PublishedIntakeForm } from './intake.models';

export const intakeTriageStates = Object.freeze([
  { id: 'New', label: 'Yeni' }, { id: 'InReview', label: 'İncelemede' },
  { id: 'Resolved', label: 'Çözüldü' }, { id: 'Rejected', label: 'Reddedildi' }
]);

export function newSubmissionModel(form: PublishedIntakeForm): IntakeSubmissionModel {
  return {
    values: Object.fromEntries(form.fields.map(field => [field.key, field.type === 'Checkbox' ? false : ''])),
    files: {}, website: ''
  };
}

export function validateSubmission(form: PublishedIntakeForm | null, model: IntakeSubmissionModel | null): string | null {
  if (!form || !model) return 'Form yüklenmeden gönderim yapılamaz.';
  const missing = form.fields.find(field => field.required && isMissing(field, model));
  if (missing) return `${missing.label} alanı zorunludur.`;
  const files = Object.values(model.files).flat();
  if (files.length > 5) return 'Bir talebe en fazla 5 dosya eklenebilir.';
  if (files.some(file => file.size <= 0 || file.size > 10 * 1024 * 1024)) return 'Her dosya 10 MB veya daha küçük olmalıdır.';
  if (files.reduce((sum, file) => sum + file.size, 0) > 25 * 1024 * 1024) return 'Dosyaların toplam boyutu 25 MB sınırını aşıyor.';
  return null;
}

export function submissionFormData(form: PublishedIntakeForm, model: IntakeSubmissionModel): FormData {
  const data = new FormData();
  data.append('submission', JSON.stringify({
    values: form.fields.filter(field => field.type !== 'Attachment').map(field => ({ fieldKey: field.key, value: String(model.values[field.key] ?? '') })),
    website: model.website || null
  }));
  for (const field of form.fields.filter(field => field.type === 'Attachment')) {
    for (const file of model.files[field.key] ?? []) data.append(`attachments.${field.key}`, file, file.name);
  }
  return data;
}

export function submissionValue(submission: IntakeSubmission, fieldKey: string): string {
  return submission.values.find(value => value.fieldKey === fieldKey)?.value ?? '';
}

export function securityLabel(value: string): string { return ({ Clean: 'Taramadan geçti', Quarantined: 'Karantinada', Rejected: 'Reddedildi', Pending: 'Taranıyor' } as Record<string, string>)[value] ?? 'Bilinmiyor'; }

function isMissing(field: IntakeField, model: IntakeSubmissionModel): boolean {
  if (field.type === 'Attachment') return !(model.files[field.key] ?? []).length;
  const value = model.values[field.key];
  return value === null || value === undefined || String(value).trim() === '';
}
