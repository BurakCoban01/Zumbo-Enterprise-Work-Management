/* global module */
(function(root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ZumboAuditPrivacyCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  var privacyStates = Object.freeze({
    Pending: 'Sırada',
    Running: 'İşleniyor',
    Failed: 'Tamamlanamadı',
    Completed: 'Tamamlandı',
    Expired: 'Süresi doldu'
  });

  function text(value, maximum) {
    var normalized = String(value || '').trim();
    return normalized.slice(0, maximum || 200);
  }

  function dateBoundary(value, endOfDay) {
    if (!value) return null;
    if (Object.prototype.toString.call(value) === '[object Date]') {
      var modelDate = new Date(value.getTime());
      if (!Number.isFinite(modelDate.getTime())) {
        throw validation('AUDIT_DATE_INVALID', 'Tarih filtresi geçersiz.');
      }
      modelDate.setHours(
        endOfDay ? 23 : 0,
        endOfDay ? 59 : 0,
        endOfDay ? 59 : 0,
        endOfDay ? 999 : 0
      );
      return modelDate.toISOString();
    }
    var date = new Date(value + (String(value).length === 10
      ? (endOfDay ? 'T23:59:59.999' : 'T00:00:00.000')
      : ''));
    if (!Number.isFinite(date.getTime())) throw validation('AUDIT_DATE_INVALID', 'Tarih filtresi geçersiz.');
    return date.toISOString();
  }

  function normalizedAuditFilters(filters) {
    filters = filters || {};
    var entityType = text(filters.entityType, 80);
    var entityId = text(filters.entityId, 200);
    if (!!entityType !== !!entityId) {
      throw validation('AUDIT_ENTITY_PAIR_REQUIRED', 'Kaynak türü ve kaynak kimliği birlikte girilmelidir.');
    }
    var from = dateBoundary(filters.from, false);
    var to = dateBoundary(filters.to, true);
    if (from && to) {
      var range = Date.parse(to) - Date.parse(from);
      if (range < 0) throw validation('AUDIT_DATE_ORDER_INVALID', 'Bitiş tarihi başlangıçtan önce olamaz.');
      if (range > 366 * 86400000) throw validation('AUDIT_DATE_RANGE_INVALID', 'Tarih aralığı 366 günü geçemez.');
    }
    return {
      actorUserId: text(filters.actorUserId, 128),
      action: text(filters.action, 120),
      entityType: entityType,
      entityId: entityId,
      from: from,
      to: to
    };
  }

  function auditUrl(path, filters, context) {
    var normalized = normalizedAuditFilters(filters);
    context = context || {};
    var values = {
      actorUserId: normalized.actorUserId,
      action: normalized.action,
      entityType: normalized.entityType,
      entityId: normalized.entityId,
      from: normalized.from,
      to: normalized.to,
      organizationId: text(context.organizationId, 200)
    };
    if (context.pageSize) values.pageSize = Math.max(1, Math.min(100, Number(context.pageSize) || 50));
    if (context.cursor) values.cursor = text(context.cursor, 2000);
    var query = Object.keys(values).filter(function(key) {
      return values[key] !== null && values[key] !== undefined && values[key] !== '';
    }).map(function(key) {
      return encodeURIComponent(key) + '=' + encodeURIComponent(values[key]);
    }).join('&');
    return path + (query ? '?' + query : '');
  }

  function safeAuditChanges(entry) {
    return ((entry && entry.changes) || []).slice(0, 50).map(function(change) {
      var redacted = !!change.redacted;
      return {
        field: text(change.field, 120) || 'Değişiklik',
        oldValue: redacted && change.oldValue != null ? '[REDACTED]' : boundedValue(change.oldValue),
        newValue: redacted && change.newValue != null ? '[REDACTED]' : boundedValue(change.newValue),
        redacted: redacted
      };
    });
  }

  function boundedValue(value) {
    if (value === null || value === undefined || value === '') return null;
    return String(value).slice(0, 500);
  }

  function hasPermission(user, roles, permission) {
    var names = (user && user.roles) || [];
    return (roles || []).some(function(role) {
      return role.isActive !== false && names.indexOf(role.name) >= 0
        && ((role.permissions || []).indexOf('*') >= 0 || (role.permissions || []).indexOf(permission) >= 0);
    });
  }

  function integrityState(result) {
    if (!result) return 'unknown';
    if (!result.verified) return 'empty';
    if (!result.valid) return 'invalid';
    return result.completeHistory ? 'valid' : 'partial';
  }

  function privacyStorageKey(user) {
    if (!user || !user.id || !user.organizationId) return null;
    return 'zumbo.privacy.workflow.' + encodeURIComponent(user.organizationId) + '.' + encodeURIComponent(user.id);
  }

  function savePrivacyReceipt(storage, user, receipt) {
    var key = privacyStorageKey(user);
    var token = receipt && receipt.statusToken;
    var job = receipt && receipt.job;
    if (!key || !storage || !job || !validStatusToken(token)) return false;
    storage.setItem(key, JSON.stringify({ id: job.id, statusToken: token }));
    return true;
  }

  function loadPrivacyReceipt(storage, user) {
    var key = privacyStorageKey(user);
    if (!key || !storage) return null;
    try {
      var value = JSON.parse(storage.getItem(key) || 'null');
      return value && text(value.id, 200) && validStatusToken(value.statusToken)
        ? { id: text(value.id, 200), statusToken: value.statusToken }
        : null;
    } catch (_) {
      return null;
    }
  }

  function clearPrivacyReceipt(storage, user) {
    var key = privacyStorageKey(user);
    if (key && storage) storage.removeItem(key);
  }

  function validStatusToken(value) {
    return /^[A-Za-z0-9_-]{20,128}$/.test(String(value || ''));
  }

  function validateAnonymization(draft) {
    if (!draft || !text(draft.password, 500)) {
      throw validation('PRIVACY_PASSWORD_REQUIRED', 'Parola gereklidir.');
    }
    if (draft.confirmation !== 'ANONYMIZE') {
      throw validation('PRIVACY_CONFIRMATION_REQUIRED', 'Devam etmek için ANONYMIZE yazın.');
    }
    return { password: draft.password, confirmation: 'ANONYMIZE' };
  }

  function mergePrivacyStatus(job, status) {
    if (!status) return job || null;
    return Object.assign({}, job || {}, status);
  }

  function privacyStateLabel(state) {
    return privacyStates[state] || 'Bilinmiyor';
  }

  function privacyProgress(job) {
    return Math.max(0, Math.min(100, Number(job && job.progressPercent) || 0));
  }

  function isPrivacyTerminal(job) {
    return !!job && ['Completed', 'Expired'].indexOf(job.state) >= 0;
  }

  function canRetryPrivacy(job) {
    return !!job && job.state === 'Failed';
  }

  function canReconcilePrivacy(job, now, staleAfterMs) {
    if (!job) return false;
    if (job.state === 'Failed') return true;
    if (['Pending', 'Running'].indexOf(job.state) < 0) return false;
    var updated = Date.parse(job.updatedAt);
    return Number.isFinite(updated)
      && (now == null ? Date.now() : now) - updated >= (staleAfterMs || 120000);
  }

  function validation(code, message) {
    var error = new Error(message);
    error.code = code;
    return error;
  }

  return Object.freeze({
    normalizedAuditFilters: normalizedAuditFilters,
    auditSearchUrl: function(filters, context) { return auditUrl('/api/audit', filters, context); },
    auditExportUrl: function(filters, context) { return auditUrl('/api/audit/export', filters, context); },
    safeAuditChanges: safeAuditChanges,
    hasPermission: hasPermission,
    integrityState: integrityState,
    privacyStorageKey: privacyStorageKey,
    savePrivacyReceipt: savePrivacyReceipt,
    loadPrivacyReceipt: loadPrivacyReceipt,
    clearPrivacyReceipt: clearPrivacyReceipt,
    validStatusToken: validStatusToken,
    validateAnonymization: validateAnonymization,
    mergePrivacyStatus: mergePrivacyStatus,
    privacyStateLabel: privacyStateLabel,
    privacyProgress: privacyProgress,
    isPrivacyTerminal: isPrivacyTerminal,
    canRetryPrivacy: canRetryPrivacy,
    canReconcilePrivacy: canReconcilePrivacy
  });
});
