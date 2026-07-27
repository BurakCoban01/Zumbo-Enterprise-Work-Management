/* global module */
(function(root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ZumboWorkAutomationCore = api;
})(typeof window !== 'undefined' ? window : globalThis, function() {
  'use strict';

  var limits = Object.freeze({
    templateName: 120,
    templateTitle: 200,
    templateDescription: 10000,
    dueAfterDays: 3650,
    labelCount: 50,
    labelLength: 50,
    recurrenceInterval: 365,
    recurrenceOccurrences: 1000,
    previewCount: 5
  });

  function roleOf(project, userId) {
    var membership = (project && project.members || []).find(function(member) {
      return member.userId === userId;
    });
    return membership ? membership.role : '';
  }

  function canEdit(role, currentUser) {
    var globalRoles = currentUser && currentUser.roles || [];
    return globalRoles.indexOf('SystemAdmin') >= 0
      || ['ProjectOwner', 'ProjectAdmin', 'Developer'].indexOf(role) >= 0;
  }

  function normalizeLabels(value) {
    var seen = Object.create(null);
    var values = String(value || '').split(/[\n,]+/).map(function(label) {
      return label.trim();
    }).filter(function(label) {
      var key = label.toLowerCase();
      if (!label || seen[key]) return false;
      seen[key] = true;
      return true;
    });
    return {
      values: values,
      tooMany: values.length > limits.labelCount,
      tooLong: values.some(function(label) { return label.length > limits.labelLength; })
    };
  }

  function toLocalInput(value) {
    if (!value) return '';
    var date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return [
      date.getFullYear(),
      pad(date.getMonth() + 1),
      pad(date.getDate())
    ].join('-') + 'T' + [pad(date.getHours()), pad(date.getMinutes())].join(':');
  }

  function toUtcIso(value) {
    if (!value) return null;
    var date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  function timeZone() {
    try {
      return Intl.DateTimeFormat().resolvedOptions().timeZone || 'Yerel saat';
    } catch (_) {
      return 'Yerel saat';
    }
  }

  function customFieldsForRequest(values) {
    return (values || []).map(function(value) {
      return {
        fieldKey: value.fieldKey,
        textValue: value.textValue == null ? null : value.textValue,
        numberValue: value.numberValue == null ? null : value.numberValue,
        booleanValue: value.booleanValue == null ? null : value.booleanValue,
        dateValue: value.dateValue == null ? null : value.dateValue,
        optionKey: value.optionKey == null ? null : value.optionKey
      };
    });
  }

  function templateDraft(template, fallbackBoardId) {
    template = template || {};
    return {
      id: template.id || null,
      boardId: template.boardId || fallbackBoardId || '',
      name: template.name || '',
      title: template.title || '',
      description: template.description || '',
      type: template.type || 'Task',
      priority: template.priority || 'Medium',
      assigneeUserId: template.assigneeUserId || '',
      teamId: template.teamId || '',
      dueAfterDays: template.dueAfterDays == null ? null : template.dueAfterDays,
      labelsText: (template.labels || []).join(', '),
      customFields: customFieldsForRequest(template.customFields)
    };
  }

  function recurrenceDraft(templateId, now) {
    var start = new Date(now || Date.now());
    start.setSeconds(0, 0);
    start.setMinutes(start.getMinutes() + 15);
    return {
      templateId: templateId || '',
      frequency: 'Weekly',
      interval: 1,
      startAtLocal: start,
      endAtLocal: null,
      maxOccurrences: 12
    };
  }

  function recurrenceRequest(projectId, draft) {
    return {
      projectId: projectId,
      templateId: draft.templateId,
      frequency: draft.frequency,
      interval: Number(draft.interval),
      startAtUtc: toUtcIso(draft.startAtLocal),
      endAtUtc: toUtcIso(draft.endAtLocal),
      maxOccurrences: Number(draft.maxOccurrences)
    };
  }

  function templateName(templates, id) {
    var template = (templates || []).find(function(candidate) { return candidate.id === id; });
    return template ? template.name : 'Arşivlenmiş şablon';
  }

  function frequencyLabel(value, interval) {
    var unit = { Daily: 'gün', Weekly: 'hafta', Monthly: 'ay' }[value] || value || 'dönem';
    return Number(interval) === 1 ? 'Her ' + unit : 'Her ' + interval + ' ' + unit;
  }

  function recurrenceState(recurrence, now) {
    if (!recurrence) return { id: 'unknown', label: 'Bilinmiyor', tone: 'neutral' };
    if (recurrence.archived) return { id: 'archived', label: 'Arşiv', tone: 'neutral' };
    if (!recurrence.nextRunAtUtc || recurrence.scheduledOccurrences >= recurrence.maxOccurrences) {
      return { id: 'completed', label: 'Tamamlandı', tone: 'success' };
    }
    if (!recurrence.active) return { id: 'paused', label: 'Duraklatıldı', tone: 'warning' };
    var current = new Date(now || Date.now()).getTime();
    var next = new Date(recurrence.nextRunAtUtc).getTime();
    if (Number.isFinite(next) && next < current - 5 * 60 * 1000) {
      return { id: 'delayed', label: 'Gecikmiş', tone: 'danger' };
    }
    return { id: 'active', label: 'Etkin', tone: 'success' };
  }

  function occurrenceState(occurrence) {
    if (occurrence && occurrence.status === 'Generated') {
      return { id: 'generated', label: 'Oluşturuldu', tone: 'success' };
    }
    if (occurrence && occurrence.status === 'Scheduled') {
      return { id: 'scheduled', label: 'İşleniyor', tone: 'warning' };
    }
    return { id: 'failed', label: 'Başarısız', tone: 'danger' };
  }

  function auditEntries(entries) {
    return (entries || []).slice().sort(function(left, right) {
      return new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime();
    });
  }

  function errorMessage(error, fallback) {
    if (error && error.message) return error.message;
    if (error && error.data && error.data.error && error.data.error.message) {
      return error.data.error.message;
    }
    return fallback || 'İşlem tamamlanamadı.';
  }

  function pad(value) {
    return String(value).padStart(2, '0');
  }

  return Object.freeze({
    limits: limits,
    roleOf: roleOf,
    canEdit: canEdit,
    normalizeLabels: normalizeLabels,
    toLocalInput: toLocalInput,
    toUtcIso: toUtcIso,
    timeZone: timeZone,
    customFieldsForRequest: customFieldsForRequest,
    templateDraft: templateDraft,
    recurrenceDraft: recurrenceDraft,
    recurrenceRequest: recurrenceRequest,
    templateName: templateName,
    frequencyLabel: frequencyLabel,
    recurrenceState: recurrenceState,
    occurrenceState: occurrenceState,
    auditEntries: auditEntries,
    errorMessage: errorMessage
  });
});
