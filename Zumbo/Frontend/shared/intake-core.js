/* global module */
(function(root, factory) {
  'use strict';

  var core = factory();
  if (typeof module === 'object' && module.exports) module.exports = core;
  if (root) root.ZumboIntakeCore = core;
})(typeof window !== 'undefined' ? window : globalThis, function() {
  'use strict';

  var limits = Object.freeze({
    formName: 120,
    description: 1000,
    confirmation: 500,
    fields: 40,
    fieldLabel: 120,
    helpText: 500,
    options: 50,
    value: 4000,
    attachments: 5,
    attachmentBytes: 10 * 1024 * 1024,
    totalAttachmentBytes: 25 * 1024 * 1024
  });
  var fieldTypes = [
    type('Text', 'Kısa metin'),
    type('LongText', 'Uzun metin'),
    type('Email', 'E-posta'),
    type('Number', 'Sayı'),
    type('Date', 'Tarih'),
    type('Choice', 'Seçim'),
    type('Checkbox', 'Onay kutusu'),
    type('Attachment', 'Dosya')
  ];
  var triageStates = [
    { id: 'New', label: 'Yeni' },
    { id: 'InReview', label: 'İncelemede' },
    { id: 'Resolved', label: 'Çözüldü' },
    { id: 'Rejected', label: 'Reddedildi' }
  ];

  function type(id, label) {
    return { id: id, label: label };
  }

  function roleOf(project, userId) {
    var member = (project && project.members || []).find(function(candidate) {
      return candidate.userId === userId;
    });
    return member ? member.role : null;
  }

  function canManage(role, definitions) {
    var definition = (definitions || []).find(function(item) { return item.name === role && item.isActive !== false; });
    return !!definition && (definition.permissions || []).some(function(permission) {
      return permission === '*' || permission === 'BoardManage';
    });
  }

  function keyFromLabel(label, index) {
    var key = String(label || '')
      .toLocaleLowerCase('tr-TR')
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/ı/g, 'i')
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .slice(0, 48);
    if (!/^[a-z]/.test(key)) key = 'alan_' + (index + 1);
    return key || 'alan_' + (index + 1);
  }

  function newField(index, fieldType) {
    return {
      key: 'alan_' + (index + 1),
      label: '',
      type: fieldType || 'Text',
      required: false,
      helpText: '',
      optionsText: ''
    };
  }

  function newDraft(project, boards) {
    var firstBoard = (boards || [])[0];
    return {
      id: null,
      name: '',
      description: '',
      state: 'Draft',
      publicId: null,
      publishedVersion: 0,
      definition: {
        accessPolicy: 'Internal',
        boardId: firstBoard ? firstBoard.id : '',
        workItemType: 'Task',
        defaultPriority: 'Medium',
        confirmationMessage: 'Talebiniz alındı.',
        fields: [
          {
            key: 'baslik',
            label: 'Talep başlığı',
            type: 'Text',
            required: true,
            helpText: '',
            optionsText: ''
          },
          {
            key: 'aciklama',
            label: 'Açıklama',
            type: 'LongText',
            required: false,
            helpText: '',
            optionsText: ''
          }
        ],
        mapping: {
          titleFieldKey: 'baslik',
          descriptionFieldKey: 'aciklama',
          priorityFieldKey: '',
          dueDateFieldKey: '',
          customFields: []
        }
      },
      projectId: project && project.id || ''
    };
  }

  function editDraft(form) {
    if (!form) return null;
    var definition = form.draft || form.definition || {};
    return {
      id: form.id,
      name: form.name,
      description: form.description || '',
      state: form.state,
      publicId: form.publicId,
      publishedVersion: form.publishedVersion || 0,
      projectId: form.projectId,
      definition: {
        accessPolicy: definition.accessPolicy || 'Internal',
        boardId: definition.boardId || '',
        workItemType: definition.workItemType || 'Task',
        defaultPriority: definition.defaultPriority || 'Medium',
        confirmationMessage: definition.confirmationMessage || 'Talebiniz alındı.',
        fields: (definition.fields || []).map(function(field) {
          return {
            key: field.key,
            label: field.label,
            type: field.type,
            required: !!field.required,
            helpText: field.helpText || '',
            optionsText: (field.options || []).join('\n')
          };
        }),
        mapping: {
          titleFieldKey: definition.mapping && definition.mapping.titleFieldKey || '',
          descriptionFieldKey: definition.mapping && definition.mapping.descriptionFieldKey || '',
          priorityFieldKey: definition.mapping && definition.mapping.priorityFieldKey || '',
          dueDateFieldKey: definition.mapping && definition.mapping.dueDateFieldKey || '',
          customFields: (definition.mapping && definition.mapping.customFields || []).map(function(mapping) {
            return {
              intakeFieldKey: mapping.intakeFieldKey,
              workItemFieldKey: mapping.workItemFieldKey
            };
          })
        }
      }
    };
  }

  function optionsOf(field) {
    var seen = Object.create(null);
    return String(field && field.optionsText || '')
      .split(/[\n,]/)
      .map(function(value) { return value.trim(); })
      .filter(function(value) {
        var key = value.toLocaleLowerCase('tr-TR');
        if (!value || seen[key]) return false;
        seen[key] = true;
        return true;
      });
  }

  function normalizeDraft(draft) {
    var definition = draft.definition;
    var fields = definition.fields.map(function(field, index) {
      var key = String(field.key || '').trim() || keyFromLabel(field.label, index);
      return {
        key: key,
        label: String(field.label || '').trim(),
        type: field.type,
        required: !!field.required,
        helpText: String(field.helpText || '').trim() || null,
        options: field.type === 'Choice' ? optionsOf(field) : []
      };
    });
    var mapping = definition.mapping;
    return {
      name: String(draft.name || '').trim(),
      description: String(draft.description || '').trim() || null,
      definition: {
        accessPolicy: definition.accessPolicy,
        boardId: definition.boardId,
        workItemType: definition.workItemType,
        defaultPriority: definition.defaultPriority,
        confirmationMessage: String(definition.confirmationMessage || '').trim(),
        fields: fields,
        mapping: {
          titleFieldKey: mapping.titleFieldKey,
          descriptionFieldKey: mapping.descriptionFieldKey || null,
          priorityFieldKey: mapping.priorityFieldKey || null,
          dueDateFieldKey: mapping.dueDateFieldKey || null,
          customFields: (mapping.customFields || []).filter(function(item) {
            return item.intakeFieldKey && item.workItemFieldKey;
          }).map(function(item) {
            return {
              intakeFieldKey: item.intakeFieldKey,
              workItemFieldKey: item.workItemFieldKey
            };
          })
        }
      }
    };
  }

  function validateDraft(draft) {
    if (!draft || !draft.name || !draft.definition.boardId) return 'Form adı ve hedef pano gereklidir.';
    if (!draft.definition.fields.length) return 'En az bir alan ekleyin.';
    if (draft.definition.fields.length > limits.fields) return 'Bir form en fazla 40 alan içerebilir.';
    var normalized = normalizeDraft(draft);
    var keys = normalized.definition.fields.map(function(field) { return field.key; });
    if (keys.some(function(key) { return !/^[a-z][a-z0-9_-]*$/.test(key); })) {
      return 'Alan anahtarları küçük harfle başlamalı; yalnızca harf, sayı, _ ve - içermelidir.';
    }
    if (new Set(keys).size !== keys.length) return 'Alan anahtarları benzersiz olmalıdır.';
    if (normalized.definition.fields.some(function(field) { return !field.label; })) return 'Her alanın görünen adı gereklidir.';
    var title = normalized.definition.fields.find(function(field) {
      return field.key === normalized.definition.mapping.titleFieldKey;
    });
    if (!title || !title.required || ['Text', 'LongText'].indexOf(title.type) < 0) {
      return 'Başlık eşlemesi zorunlu bir metin alanına bağlanmalıdır.';
    }
    var choice = normalized.definition.fields.find(function(field) {
      return field.type === 'Choice' && (!field.options.length || field.options.length > limits.options);
    });
    if (choice) return 'Seçim alanları 1-50 benzersiz seçenek içermelidir.';
    var customMappings = normalized.definition.mapping.customFields;
    if (new Set(customMappings.map(function(item) { return item.intakeFieldKey; })).size !== customMappings.length
      || new Set(customMappings.map(function(item) { return item.workItemFieldKey; })).size !== customMappings.length) {
      return 'Özel alan eşlemeleri bire bir olmalıdır.';
    }
    return null;
  }

  function requestFor(draft) {
    var normalized = normalizeDraft(draft);
    if (!draft.id) normalized.projectId = draft.projectId;
    return normalized;
  }

  function compatibleFields(fields, target) {
    var allowed = {
      title: ['Text', 'LongText'],
      description: ['Text', 'LongText'],
      priority: ['Text', 'Choice'],
      dueDate: ['Date'],
      custom: ['Text', 'LongText', 'Email', 'Number', 'Date', 'Choice', 'Checkbox']
    }[target] || [];
    return (fields || []).filter(function(field) { return allowed.indexOf(field.type) >= 0; });
  }

  function submissionModel(form) {
    var values = {};
    (form && form.fields || []).forEach(function(field) {
      values[field.key] = field.type === 'Checkbox' ? false : '';
    });
    return { values: values, files: {}, website: '' };
  }

  function submissionPayload(form, model) {
    return {
      values: (form.fields || []).filter(function(field) {
        return field.type !== 'Attachment';
      }).map(function(field) {
        var value = model.values[field.key];
        if (field.type === 'Checkbox') value = value ? 'true' : 'false';
        return { fieldKey: field.key, value: value == null ? '' : String(value) };
      }),
      website: model.website || null
    };
  }

  function validateSubmission(form, model) {
    if (!form || !model) return 'Form yüklenmeden gönderim yapılamaz.';
    var fields = form.fields || [];
    var missing = fields.find(function(field) {
      if (!field.required) return false;
      if (field.type === 'Attachment') return !(model.files[field.key] || []).length;
      var value = model.values[field.key];
      return value === null || value === undefined || String(value).trim() === '';
    });
    if (missing) return missing.label + ' alanı zorunludur.';
    var files = [];
    fields.forEach(function(field) {
      files = files.concat(model.files[field.key] || []);
    });
    if (files.length > limits.attachments) return 'Bir talebe en fazla 5 dosya eklenebilir.';
    if (files.some(function(file) { return file.size <= 0 || file.size > limits.attachmentBytes; })) {
      return 'Her dosya 10 MB veya daha küçük olmalıdır.';
    }
    var total = files.reduce(function(sum, file) { return sum + file.size; }, 0);
    if (total > limits.totalAttachmentBytes) return 'Dosyaların toplam boyutu 25 MB sınırını aşıyor.';
    return null;
  }

  function submissionValue(submission, fieldKey) {
    var entry = (submission && submission.values || []).find(function(value) {
      return value.fieldKey === fieldKey;
    });
    return entry ? entry.value : '';
  }

  function stateLabel(value) {
    return {
      Draft: 'Taslak',
      Published: 'Yayında',
      Archived: 'Arşiv',
      Processing: 'İşleniyor',
      New: 'Yeni',
      InReview: 'İncelemede',
      Resolved: 'Çözüldü',
      Rejected: 'Reddedildi'
    }[value] || value || 'Bilinmiyor';
  }

  function accessLabel(value) {
    return value === 'Public' ? 'Dış paylaşıma açık' : 'Yalnızca ekip';
  }

  function typeLabel(value) {
    var match = fieldTypes.find(function(item) { return item.id === value; });
    return match ? match.label : value;
  }

  function securityLabel(value) {
    return {
      Clean: 'Taramadan geçti',
      Quarantined: 'Karantinada',
      Rejected: 'Reddedildi',
      Pending: 'Taranıyor'
    }[value] || value || 'Bilinmiyor';
  }

  function errorMessage(error, fallback) {
    var code = error && (error.code || error.data && error.data.error && error.data.error.code);
    var messages = {
      INTAKE_FORM_ARCHIVED: 'Arşivlenmiş form değiştirilemez.',
      INTAKE_FORM_NOT_FOUND: 'Form bulunamadı veya bu projede erişilemiyor.',
      INTAKE_FORM_PUBLIC_ONLY: 'Dış paylaşıma açık form yalnızca paylaşım bağlantısından gönderilebilir.',
      INTAKE_FORM_VERSION_MISSING: 'Yayındaki form sürümü kullanılamıyor.',
      INTAKE_SUBMISSION_PROCESSING: 'İş kaydı tamamlanmadan bu talep sınıflandırılamaz.',
      IDEMPOTENCY_KEY_REUSED: 'Bu gönderim anahtarı farklı bir içerikle daha önce kullanıldı.',
      RATE_LIMITED: 'Çok sayıda gönderim yapıldı. Bir süre sonra yeniden deneyin.',
      FORBIDDEN: 'Bu işlem için proje yetkiniz yok.',
      VALIDATION_ERROR: 'Form alanlarını ve belirtilen sınırları kontrol edin.',
      NETWORK_UNAVAILABLE: 'Servise ulaşılamıyor. Bağlantınızı denetleyin.',
      SERVER_UNAVAILABLE: 'Intake servisi geçici olarak kullanılamıyor.',
      CONCURRENCY_CONFLICT: 'Form başka bir kullanıcı tarafından değiştirildi. Güncel kayıt yeniden yüklendi.'
    };
    return messages[code] || error && error.message || fallback;
  }

  return Object.freeze({
    limits: limits,
    fieldTypes: fieldTypes,
    triageStates: triageStates,
    roleOf: roleOf,
    canManage: canManage,
    keyFromLabel: keyFromLabel,
    newField: newField,
    newDraft: newDraft,
    editDraft: editDraft,
    optionsOf: optionsOf,
    normalizeDraft: normalizeDraft,
    validateDraft: validateDraft,
    requestFor: requestFor,
    compatibleFields: compatibleFields,
    submissionModel: submissionModel,
    submissionPayload: submissionPayload,
    validateSubmission: validateSubmission,
    submissionValue: submissionValue,
    stateLabel: stateLabel,
    accessLabel: accessLabel,
    typeLabel: typeLabel,
    securityLabel: securityLabel,
    errorMessage: errorMessage
  });
});
