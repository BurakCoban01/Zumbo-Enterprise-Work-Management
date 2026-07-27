/* global module */
(function(root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ZumboWebhookCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  var scopes = Object.freeze([
    { value: 'work-item.created', label: 'İş oluşturuldu' },
    { value: 'work-item.updated', label: 'İş güncellendi' },
    { value: 'work-item.moved', label: 'İş taşındı' },
    { value: 'work-item.reordered', label: 'İş sıralandı' },
    { value: 'work-item.archived', label: 'İş arşivlendi' },
    { value: 'work-item.restored', label: 'İş geri yüklendi' }
  ]);
  var knownScopes = scopes.map(function(scope) { return scope.value; });
  var deliveryStates = Object.freeze({
    Pending: { label: 'Sırada', tone: 'warning' },
    Processing: { label: 'Gönderiliyor', tone: 'info' },
    Delivered: { label: 'Teslim edildi', tone: 'success' },
    DeadLetter: { label: 'Müdahale gerekli', tone: 'danger' }
  });
  var safeErrors = Object.freeze({
    HTTP_400: 'Alıcı isteği kabul etmedi (400).',
    HTTP_401: 'Alıcı imzayı veya kimliği kabul etmedi (401).',
    HTTP_403: 'Alıcı isteği reddetti (403).',
    HTTP_404: 'Alıcı uç noktası bulunamadı (404).',
    HTTP_408: 'Alıcı zaman aşımı bildirdi (408).',
    HTTP_429: 'Alıcı istek sınırına ulaştı (429).',
    HTTP_500: 'Alıcı geçici bir sunucu hatası bildirdi (500).',
    HTTP_502: 'Alıcı ağ geçidi hatası bildirdi (502).',
    HTTP_503: 'Alıcı geçici olarak kullanılamıyor (503).',
    HTTP_504: 'Alıcı ağ geçidi zaman aşımı bildirdi (504).',
    REQUEST_TIMEOUT: 'Alıcı yanıt süresini aştı.',
    RECEIVER_FAILURE: 'Alıcıya teslimat tamamlanamadı.',
    TARGET_RESOLUTION_FAILED: 'Uç nokta güvenli biçimde çözümlenemedi.',
    TARGET_ADDRESS_BLOCKED: 'Uç noktanın ağ adresine izin verilmiyor.'
  });

  function emptyDraft() {
    return { name: '', targetUrl: '', eventScopes: ['work-item.created'], expectedVersion: null };
  }

  function draftFrom(subscription) {
    if (!subscription) return emptyDraft();
    return {
      name: String(subscription.name || ''),
      targetUrl: String(subscription.targetUrl || ''),
      eventScopes: (subscription.eventScopes || []).slice(),
      expectedVersion: subscription.version
    };
  }

  function validateDraft(draft) {
    draft = draft || {};
    var name = String(draft.name || '').trim();
    var targetUrl = String(draft.targetUrl || '').trim();
    var selectedScopes = unique((draft.eventScopes || []).filter(function(scope) {
      return knownScopes.indexOf(scope) >= 0;
    })).sort();
    if (name.length < 1 || name.length > 100) {
      throw validation('WEBHOOK_NAME_INVALID', 'Ad 1 ile 100 karakter arasında olmalıdır.');
    }
    if (targetUrl.length < 1 || targetUrl.length > 2048) {
      throw validation('WEBHOOK_TARGET_INVALID', 'Geçerli bir HTTPS uç noktası girin.');
    }
    try {
      var parsed = new URL(targetUrl);
      if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') throw new Error('protocol');
      if (!parsed.hostname) throw new Error('host');
    } catch (_) {
      throw validation('WEBHOOK_TARGET_INVALID', 'Geçerli bir HTTPS uç noktası girin.');
    }
    if (!selectedScopes.length) {
      throw validation('WEBHOOK_SCOPE_REQUIRED', 'En az bir olay seçin.');
    }
    var result = { name: name, targetUrl: targetUrl, eventScopes: selectedScopes };
    if (draft.expectedVersion !== null && draft.expectedVersion !== undefined) {
      result.expectedVersion = Number(draft.expectedVersion);
    }
    return result;
  }

  function toggleScope(draft, scope) {
    if (!draft || knownScopes.indexOf(scope) < 0) return;
    draft.eventScopes = draft.eventScopes || [];
    var index = draft.eventScopes.indexOf(scope);
    if (index >= 0) draft.eventScopes.splice(index, 1);
    else draft.eventScopes.push(scope);
  }

  function hasPermission(user, roles) {
    var names = (user && user.roles) || [];
    if (names.indexOf('SystemAdmin') >= 0 || names.indexOf('OrganizationAdmin') >= 0) return true;
    return (roles || []).some(function(role) {
      var permissions = role.permissions || [];
      return names.indexOf(role.name) >= 0
        && (permissions.indexOf('*') >= 0 || permissions.indexOf('IntegrationManage') >= 0);
    });
  }

  function safeTargetLabel(value) {
    try {
      var parsed = new URL(String(value || ''));
      var path = parsed.pathname === '/' ? '' : parsed.pathname;
      return parsed.protocol + '//' + parsed.host + path + (parsed.search ? '?…' : '');
    } catch (_) {
      return 'Geçersiz uç nokta';
    }
  }

  function scopeLabel(scope) {
    if (scope === 'webhook.test') return 'Test teslimatı';
    var found = scopes.find(function(item) { return item.value === scope; });
    return found ? found.label : 'Bilinmeyen olay';
  }

  function deliveryState(delivery) {
    return deliveryStates[String(delivery && delivery.status || '')]
      || { label: 'Bilinmiyor', tone: 'neutral' };
  }

  function safeError(code) {
    if (!code) return '';
    return safeErrors[String(code)] || 'Teslimat tamamlanamadı. Ayrıntılar için sistem yöneticisine başvurun.';
  }

  function canReplay(delivery) {
    return !!delivery && delivery.status === 'DeadLetter';
  }

  function shortHash(value) {
    value = String(value || '');
    return value ? value.slice(0, 12) : '';
  }

  function unique(values) {
    return values.filter(function(value, index) { return values.indexOf(value) === index; });
  }

  function validation(code, message) {
    var error = new Error(message);
    error.code = code;
    return error;
  }

  return Object.freeze({
    scopes: scopes,
    emptyDraft: emptyDraft,
    draftFrom: draftFrom,
    validateDraft: validateDraft,
    toggleScope: toggleScope,
    hasPermission: hasPermission,
    safeTargetLabel: safeTargetLabel,
    scopeLabel: scopeLabel,
    deliveryState: deliveryState,
    safeError: safeError,
    canReplay: canReplay,
    shortHash: shortHash
  });
});
