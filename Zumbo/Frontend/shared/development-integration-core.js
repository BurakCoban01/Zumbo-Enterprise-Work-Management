/* global module */
(function(root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ZumboDevelopmentIntegrationCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function() {
  'use strict';

  var providers = Object.freeze([
    { value: 'GitHub', label: 'GitHub', baseUrl: 'https:/' + '/api.github.com' },
    { value: 'GitLab', label: 'GitLab', baseUrl: 'https:/' + '/gitlab.com/api/v4' }
  ]);
  var linkKinds = Object.freeze([
    { value: 'PullRequest', label: 'Pull request' },
    { value: 'Commit', label: 'Commit' },
    { value: 'Branch', label: 'Dal' },
    { value: 'Build', label: 'Build' }
  ]);
  var statuses = Object.freeze([
    'Open', 'Merged', 'Closed', 'Success', 'Failed', 'Pending', 'Running',
    'Pushed', 'Unknown'
  ]);
  var healthStates = Object.freeze({
    Healthy: { label: 'Sağlıklı', tone: 'success' },
    Degraded: { label: 'Müdahale gerekli', tone: 'danger' },
    Disconnected: { label: 'Bağlantı kesildi', tone: 'neutral' },
    NotChecked: { label: 'Henüz denetlenmedi', tone: 'warning' }
  });
  var linkStates = Object.freeze({
    Open: { label: 'Açık', tone: 'info' },
    Merged: { label: 'Birleştirildi', tone: 'success' },
    Closed: { label: 'Kapalı', tone: 'neutral' },
    Success: { label: 'Başarılı', tone: 'success' },
    Failed: { label: 'Başarısız', tone: 'danger' },
    Pending: { label: 'Bekliyor', tone: 'warning' },
    Running: { label: 'Çalışıyor', tone: 'info' },
    Pushed: { label: 'Gönderildi', tone: 'success' },
    Unknown: { label: 'Bilinmiyor', tone: 'neutral' }
  });

  function emptyConnectionDraft(provider) {
    provider = providerValue(provider || 'GitHub');
    return {
      name: '',
      provider: provider,
      baseUrl: providerDefinition(provider).baseUrl,
      accessToken: ''
    };
  }

  function selectProvider(draft, provider) {
    provider = providerValue(provider);
    var previous = providerDefinition(draft.provider);
    draft.provider = provider;
    if (!draft.baseUrl || draft.baseUrl === previous.baseUrl) {
      draft.baseUrl = providerDefinition(provider).baseUrl;
    }
  }

  function validateConnectionDraft(draft) {
    draft = draft || {};
    var name = required(draft.name, 'Bağlantı adı', 100);
    var provider = providerValue(draft.provider);
    var baseUrl = normalizeBaseUrl(draft.baseUrl || providerDefinition(provider).baseUrl);
    var accessToken = String(draft.accessToken || '').trim();
    if (accessToken.length < 16 || accessToken.length > 512 || /\s/.test(accessToken)) {
      throw validation(
        'DEVELOPMENT_CREDENTIAL_INVALID',
        'Erişim anahtarı 16 ile 512 arasında boşluksuz karakter içermelidir.'
      );
    }
    return {
      name: name,
      provider: provider,
      baseUrl: baseUrl,
      accessToken: accessToken
    };
  }

  function emptyLinkDraft() {
    return {
      mappingId: '',
      kind: 'PullRequest',
      externalId: '',
      title: '',
      url: '',
      branch: '',
      commitSha: '',
      status: 'Open'
    };
  }

  function validateLinkDraft(draft, mappings) {
    draft = draft || {};
    var mapping = (mappings || []).find(function(item) {
      return item.id === draft.mappingId && item.isActive !== false;
    });
    if (!mapping) {
      throw validation(
        'DEVELOPMENT_MAPPING_REQUIRED',
        'Bu projeye bağlı etkin bir repository seçin.'
      );
    }
    var kind = enumValue(draft.kind, linkKinds, 'Bağlantı türü');
    var status = enumText(draft.status, statuses, 'Durum');
    var url = safeHttpsUrl(draft.url, 'Bağlantı URL’si');
    if (new URL(url).hostname.toLowerCase()
        !== new URL(mapping.repositoryUrl).hostname.toLowerCase()) {
      throw validation(
        'DEVELOPMENT_LINK_HOST_INVALID',
        'Bağlantı URL’si seçilen repository ile aynı hostta olmalıdır.'
      );
    }
    return {
      mappingId: mapping.id,
      kind: kind,
      externalId: required(draft.externalId, 'Harici kimlik', 300),
      title: required(draft.title, 'Başlık', 200),
      url: url,
      branch: optional(draft.branch, 255),
      commitSha: optional(draft.commitSha, 128),
      status: status
    };
  }

  function mappingRequest(projectId, repository) {
    if (!repository || !repository.externalRepositoryId) {
      throw validation('DEVELOPMENT_REPOSITORY_REQUIRED', 'Bir repository seçin.');
    }
    return {
      projectId: required(projectId, 'Proje', 128),
      externalRepositoryId: required(repository.externalRepositoryId, 'Repository kimliği', 200),
      repositoryName: required(repository.name, 'Repository adı', 120),
      repositoryFullName: required(repository.fullName, 'Repository tam adı', 240),
      repositoryUrl: safeHttpsUrl(repository.url, 'Repository URL’si'),
      defaultBranch: required(repository.defaultBranch, 'Varsayılan dal', 255)
    };
  }

  function healthState(connection) {
    var key = String(connection && connection.healthStatus || 'NotChecked');
    if (connection && !connection.isConnected) key = 'Disconnected';
    return healthStates[key] || healthStates.NotChecked;
  }

  function linkState(link) {
    return linkStates[String(link && link.status || 'Unknown')] || linkStates.Unknown;
  }

  function safeUrlLabel(value) {
    try {
      var parsed = new URL(String(value || ''));
      var path = parsed.pathname === '/' ? '' : parsed.pathname.replace(/\/$/, '');
      return parsed.protocol + '//' + parsed.host + path + (parsed.search ? '?…' : '');
    } catch (_) {
      return 'Geçersiz adres';
    }
  }

  function providerLabel(value) {
    return providerDefinition(value).label;
  }

  function kindLabel(value) {
    var found = linkKinds.find(function(item) { return item.value === value; });
    return found ? found.label : 'Bağlantı';
  }

  function safeHealthError(code) {
    var labels = {
      PROVIDER_AUTHENTICATION_FAILED: 'Sağlayıcı erişim anahtarını kabul etmedi.',
      PROVIDER_FORBIDDEN: 'Sağlayıcı gerekli erişim kapsamını reddetti.',
      PROVIDER_UNAVAILABLE: 'Sağlayıcıya şu anda ulaşılamıyor.',
      PROVIDER_RESPONSE_INVALID: 'Sağlayıcı beklenen yanıtı vermedi.',
      PROVIDER_RESPONSE_TOO_LARGE: 'Sağlayıcı yanıtı güvenli sınırı aştı.',
      TARGET_RESOLUTION_FAILED: 'Sağlayıcı adresi güvenli biçimde çözümlenemedi.',
      TARGET_ADDRESS_BLOCKED: 'Sağlayıcı adresine ağ politikasınca izin verilmiyor.'
    };
    return code ? labels[String(code)] || 'Sağlayıcı sağlık denetimi tamamlanamadı.' : '';
  }

  function shortFingerprint(value) {
    value = String(value || '');
    return value ? value.slice(0, 16) : '—';
  }

  function providerValue(value) {
    return enumValue(value, providers, 'Sağlayıcı');
  }

  function providerDefinition(value) {
    return providers.find(function(item) {
      return item.value.toLowerCase() === String(value || '').toLowerCase();
    }) || providers[0];
  }

  function enumValue(value, definitions, label) {
    var found = definitions.find(function(item) {
      return item.value.toLowerCase() === String(value || '').trim().toLowerCase();
    });
    if (!found) throw validation('DEVELOPMENT_ENUM_INVALID', label + ' desteklenmiyor.');
    return found.value;
  }

  function enumText(value, definitions, label) {
    var found = definitions.find(function(item) {
      return item.toLowerCase() === String(value || '').trim().toLowerCase();
    });
    if (!found) throw validation('DEVELOPMENT_ENUM_INVALID', label + ' desteklenmiyor.');
    return found;
  }

  function normalizeBaseUrl(value) {
    value = String(value || '').trim().replace(/\/+$/, '');
    try {
      var parsed = new URL(value);
      if (!/^https?:$/.test(parsed.protocol) || parsed.username || parsed.password
          || parsed.search || parsed.hash || !parsed.hostname) throw new Error('unsafe');
      return parsed.origin + parsed.pathname.replace(/\/+$/, '');
    } catch (_) {
      throw validation(
        'DEVELOPMENT_BASE_URL_INVALID',
        'Temel adres kimlik bilgisi, sorgu veya fragment içermeyen geçerli bir HTTP(S) URL olmalıdır.'
      );
    }
  }

  function safeHttpsUrl(value, label) {
    value = String(value || '').trim();
    try {
      var parsed = new URL(value);
      if (parsed.protocol !== 'https:' || parsed.username || parsed.password
          || parsed.hash || !parsed.hostname || value.length > 2048) throw new Error('unsafe');
      return parsed.href;
    } catch (_) {
      throw validation(
        'DEVELOPMENT_URL_INVALID',
        label + ' güvenli ve mutlak bir HTTPS adresi olmalıdır.'
      );
    }
  }

  function required(value, label, maximum) {
    value = String(value || '').trim();
    if (!value || value.length > maximum) {
      throw validation(
        'DEVELOPMENT_VALUE_INVALID',
        label + ' 1 ile ' + maximum + ' karakter arasında olmalıdır.'
      );
    }
    return value;
  }

  function optional(value, maximum) {
    value = String(value || '').trim();
    if (!value) return null;
    if (value.length > maximum) {
      throw validation(
        'DEVELOPMENT_VALUE_INVALID',
        'İsteğe bağlı alan ' + maximum + ' karakteri aşamaz.'
      );
    }
    return value;
  }

  function validation(code, message) {
    var error = new Error(message);
    error.code = code;
    return error;
  }

  return Object.freeze({
    providers: providers,
    linkKinds: linkKinds,
    statuses: statuses,
    emptyConnectionDraft: emptyConnectionDraft,
    selectProvider: selectProvider,
    validateConnectionDraft: validateConnectionDraft,
    emptyLinkDraft: emptyLinkDraft,
    validateLinkDraft: validateLinkDraft,
    mappingRequest: mappingRequest,
    healthState: healthState,
    linkState: linkState,
    safeUrlLabel: safeUrlLabel,
    providerLabel: providerLabel,
    kindLabel: kindLabel,
    safeHealthError: safeHealthError,
    shortFingerprint: shortFingerprint
  });
});
