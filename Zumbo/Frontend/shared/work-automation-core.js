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
    previewCount: 5,
    ruleName: 120,
    ruleDescription: 1000,
    ruleConditions: 20,
    ruleActions: 10,
    ruleHourlyLimit: 1000,
    ruleChainDepth: 10
  });

  function roleOf(project, userId) {
    var membership = (project && project.members || []).find(function(member) {
      return member.userId === userId;
    });
    return membership ? membership.role : '';
  }

  function canEdit(role, currentUser, projectRoles, systemRoles) {
    var projectDefinition = (projectRoles || []).find(function(item) {
      return item.name === role && item.isActive !== false;
    });
    var globalRoles = currentUser && currentUser.roles || [];
    var hasGlobalAccess = (systemRoles || []).some(function(item) {
      return item.isActive !== false && globalRoles.indexOf(item.name) >= 0
        && (item.permissions || []).indexOf('*') >= 0;
    });
    return hasGlobalAccess || !!projectDefinition && (projectDefinition.permissions || []).some(function(permission) {
      return permission === '*' || permission === 'BoardManage';
    });
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

  function ruleDraft(rule) {
    var definition = rule && rule.definition || {};
    var trigger = definition.trigger || {};
    var condition = definition.condition || null;
    var conditions = condition
      ? (condition.kind === 'Field' ? [condition] : condition.children || [])
      : [];
    return {
      id: rule && rule.id || null,
      version: rule && rule.version || 0,
      publishedVersion: rule && rule.publishedVersion || 0,
      active: !!(rule && rule.active),
      archived: !!(rule && rule.archived),
      name: definition.name || '',
      description: definition.description || '',
      triggerType: trigger.type || 'Event',
      eventType: trigger.eventType || 'WorkItemCreated',
      intervalMinutes: trigger.intervalMinutes || 60,
      startAtLocal: trigger.startAtUtc ? new Date(trigger.startAtUtc) : null,
      conditionMode: condition && condition.kind !== 'Field' ? condition.kind : 'All',
      conditions: conditions.map(function(item) {
        return {
          field: item.field || 'Status',
          operator: item.operator || 'Equals',
          value: item.value == null ? '' : item.value
        };
      }),
      actions: (definition.actions || []).map(function(action) {
        return { type: action.type, value: action.value == null ? '' : action.value };
      }),
      maximumExecutionsPerHour: definition.maximumExecutionsPerHour || 100,
      maximumChainDepth: definition.maximumChainDepth || 3
    };
  }

  function newRuleDraft() {
    return {
      id: null,
      version: 0,
      publishedVersion: 0,
      active: false,
      archived: false,
      name: '',
      description: '',
      triggerType: 'Event',
      eventType: 'WorkItemCreated',
      intervalMinutes: 60,
      startAtLocal: null,
      conditionMode: 'All',
      conditions: [],
      actions: [{ type: 'AddLabel', value: '' }],
      maximumExecutionsPerHour: 100,
      maximumChainDepth: 3
    };
  }

  function actionNeedsValue(type) {
    return ['AssignUser', 'AddLabel', 'RemoveLabel', 'SetPriority', 'AddComment']
      .indexOf(type) >= 0;
  }

  function conditionNeedsValue(operator) {
    return ['IsEmpty', 'IsNotEmpty'].indexOf(operator) < 0;
  }

  function conditionFieldLabel(field) {
    return {
      Status: 'Durum',
      PreviousStatus: 'Önceki durum',
      Priority: 'Öncelik',
      Type: 'İş türü',
      AssigneeUserId: 'Atanan kullanıcı',
      Labels: 'Etiketler'
    }[field] || field;
  }

  function conditionOperatorLabel(operator) {
    return {
      Equals: 'Eşittir',
      NotEquals: 'Eşit değildir',
      Contains: 'İçerir',
      NotContains: 'İçermez',
      IsEmpty: 'Boş',
      IsNotEmpty: 'Boş değil'
    }[operator] || operator;
  }

  function actionTypeLabel(type) {
    return {
      AssignToActor: 'Tetikleyen kullanıcıya ata',
      AssignUser: 'Kullanıcıya ata',
      ClearAssignee: 'Atamayı kaldır',
      AddLabel: 'Etiket ekle',
      RemoveLabel: 'Etiketi kaldır',
      SetPriority: 'Öncelik ayarla',
      AddComment: 'Yorum ekle'
    }[type] || type;
  }

  function ruleRequest(projectId, draft) {
    var conditions = (draft.conditions || []).map(function(condition) {
      return {
        kind: 'Field',
        field: condition.field,
        operator: condition.operator,
        value: conditionNeedsValue(condition.operator) ? String(condition.value || '').trim() : null,
        children: []
      };
    });
    var condition = conditions.length === 0
      ? null
      : conditions.length === 1
        ? conditions[0]
        : {
            kind: draft.conditionMode,
            field: null,
            operator: null,
            value: null,
            children: conditions
          };
    return {
      projectId: projectId,
      name: String(draft.name || '').trim(),
      description: String(draft.description || '').trim() || null,
      trigger: draft.triggerType === 'Schedule'
        ? {
            type: 'Schedule',
            eventType: null,
            intervalMinutes: Number(draft.intervalMinutes),
            startAtUtc: toUtcIso(draft.startAtLocal)
          }
        : {
            type: 'Event',
            eventType: draft.eventType,
            intervalMinutes: null,
            startAtUtc: null
          },
      condition: condition,
      actions: (draft.actions || []).map(function(action) {
        return {
          type: action.type,
          value: actionNeedsValue(action.type) ? String(action.value || '').trim() : null
        };
      }),
      maximumExecutionsPerHour: Number(draft.maximumExecutionsPerHour),
      maximumChainDepth: Number(draft.maximumChainDepth)
    };
  }

  function validRule(draft) {
    if (!draft || !String(draft.name || '').trim() || !draft.actions || !draft.actions.length) return false;
    if (draft.actions.length > limits.ruleActions || draft.conditions.length > limits.ruleConditions) return false;
    if (draft.triggerType === 'Schedule'
        && (Number(draft.intervalMinutes) < 5 || Number(draft.intervalMinutes) > 525600)) return false;
    if (Number(draft.maximumExecutionsPerHour) < 1
        || Number(draft.maximumExecutionsPerHour) > limits.ruleHourlyLimit) return false;
    if (Number(draft.maximumChainDepth) < 1
        || Number(draft.maximumChainDepth) > limits.ruleChainDepth) return false;
    return draft.conditions.every(function(condition) {
      return !conditionNeedsValue(condition.operator) || !!String(condition.value || '').trim();
    }) && draft.actions.every(function(action) {
      return !actionNeedsValue(action.type) || !!String(action.value || '').trim();
    });
  }

  function ruleState(rule) {
    if (rule.archived) return { id: 'archived', label: 'Arşiv', tone: 'neutral' };
    if (!rule.publishedVersion) return { id: 'draft', label: 'Taslak', tone: 'warning' };
    if (rule.active) return { id: 'active', label: 'Etkin', tone: 'success' };
    return { id: 'paused', label: 'Duraklatıldı', tone: 'warning' };
  }

  function runState(run) {
    var states = {
      Succeeded: { label: 'Başarılı', tone: 'success' },
      Skipped: { label: 'Atlandı', tone: 'neutral' },
      Running: { label: 'Çalışıyor', tone: 'warning' },
      Pending: { label: 'Sırada', tone: 'warning' },
      RetryScheduled: { label: 'Yeniden denenecek', tone: 'warning' },
      DeadLetter: { label: 'Müdahale gerekli', tone: 'danger' }
    };
    var state = states[run && run.status] || { label: 'Bilinmiyor', tone: 'neutral' };
    return { id: run && run.status || 'Unknown', label: state.label, tone: state.tone };
  }

  function triggerLabel(rule) {
    if (rule.triggerType === 'Schedule') {
      return 'Her ' + rule.intervalMinutes + ' dakika';
    }
    return {
      WorkItemCreated: 'İş oluşturulduğunda',
      WorkItemUpdated: 'İş güncellendiğinde',
      WorkItemTransitioned: 'İş durum değiştirdiğinde'
    }[rule.eventType] || rule.eventType;
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
    ruleDraft: ruleDraft,
    newRuleDraft: newRuleDraft,
    ruleRequest: ruleRequest,
    validRule: validRule,
    actionNeedsValue: actionNeedsValue,
    conditionNeedsValue: conditionNeedsValue,
    conditionFieldLabel: conditionFieldLabel,
    conditionOperatorLabel: conditionOperatorLabel,
    actionTypeLabel: actionTypeLabel,
    ruleState: ruleState,
    runState: runState,
    triggerLabel: triggerLabel,
    errorMessage: errorMessage
  });
});
