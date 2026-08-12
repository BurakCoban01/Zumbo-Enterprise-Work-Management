/* global module */
(function(root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ZumboOperationsCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  var dependencyLabels = Object.freeze({
    mongodb: 'Belge verisi',
    postgresql: 'İlişkisel veri',
    redis: 'Oturum ve gerçek zaman',
    minio: 'Dosya depolama',
    opensearch: 'Arama',
    smtp: 'E-posta teslimatı',
    webhook: 'Webhook teslimatı'
  });

  var eventLabels = Object.freeze({
    'work-item.created.v1': 'İş oluşturma olayı',
    'work-item.updated.v1': 'İş güncelleme olayı',
    'work-item.moved.v1': 'İş taşıma olayı',
    'work-item.reordered.v1': 'İş sıralama olayı',
    'work-item.archived.v1': 'İş arşivleme olayı',
    'work-item.restored.v1': 'İş geri yükleme olayı',
    'privacy.workflow.requested.v1': 'Gizlilik iş akışı olayı'
  });

  function hasPermission(user, roles) {
    var assigned = user && user.roles || [];
    return (roles || []).some(function(role) {
      return role.isActive !== false && assigned.indexOf(role.name) >= 0
        && (role.permissions || []).some(function(permission) {
          return permission === '*' || permission === 'OperationsManage';
        });
    });
  }

  function dependencyLabel(value) {
    return dependencyLabels[String(value || '').toLowerCase()] || 'Harici hizmet';
  }

  function dependencyState(snapshot) {
    if (!snapshot) return Object.freeze({ key: 'unknown', label: 'Bilinmiyor', tone: 'muted' });
    if (snapshot.circuitOpen) return Object.freeze({ key: 'unavailable', label: 'Kullanılamıyor', tone: 'danger' });
    if (Number(snapshot.timedOut || 0) > 0
        || Number(snapshot.rejected || 0) > 0
        || Number(snapshot.queued || 0) > 0
        || Number(snapshot.failed || 0) > Number(snapshot.succeeded || 0)) {
      return Object.freeze({ key: 'degraded', label: 'Kısıtlı', tone: 'warning' });
    }
    return Object.freeze({ key: 'available', label: 'Kullanılabilir', tone: 'success' });
  }

  function overallState(dependencies, messaging, notifications, storage) {
    var states = (dependencies || []).map(function(item) { return dependencyState(item).key; });
    if (Number((messaging || {}).deadLetter || 0)
        || Number((notifications || {}).deadLetter || 0)
        || Number((storage || {}).quarantined || 0)
        || states.indexOf('unavailable') >= 0) {
      return Object.freeze({ key: 'attention', label: 'Müdahale gerekiyor', tone: 'danger' });
    }
    if (states.indexOf('degraded') >= 0 || states.indexOf('unknown') >= 0) {
      return Object.freeze({ key: 'degraded', label: 'Kısıtlı çalışıyor', tone: 'warning' });
    }
    return Object.freeze({ key: 'available', label: 'Sistem kullanılabilir', tone: 'success' });
  }

  function eventLabel(value) {
    return eventLabels[String(value || '').toLowerCase()] || 'Sınıflandırılmış sistem olayı';
  }

  function notificationTypeLabel(value) {
    var normalized = String(value || '').trim();
    if (!normalized || normalized.length > 50 || /[^a-z0-9._-]/i.test(normalized)) {
      return 'Sistem bildirimi';
    }
    return normalized.replace(/[._-]+/g, ' ').replace(/\b\w/g, function(letter) {
      return letter.toUpperCase();
    });
  }

  function canReplay(item) {
    return !!(item && item.id && Number(item.attempts || 0) > 0);
  }

  return Object.freeze({
    hasPermission: hasPermission,
    dependencyLabel: dependencyLabel,
    dependencyState: dependencyState,
    overallState: overallState,
    eventLabel: eventLabel,
    notificationTypeLabel: notificationTypeLabel,
    canReplay: canReplay
  });
});
