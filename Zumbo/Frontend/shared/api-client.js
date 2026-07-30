(function(root) {
  'use strict';

  function resolveBaseUrl(config, fallbackOrigin) {
    var configured = config && typeof config.apiBaseUrl === 'string' ? config.apiBaseUrl.trim() : '';
    var value = configured || fallbackOrigin || '';
    return value.replace(/\/+$/, '');
  }

  function createIdentifier(prefix) {
    if (root.crypto && typeof root.crypto.randomUUID === 'function') {
      return prefix + root.crypto.randomUUID();
    }
    var bytes = new Uint8Array(16);
    root.crypto.getRandomValues(bytes);
    return prefix + Array.prototype.map.call(bytes, function(value) {
      return ('0' + value.toString(16)).slice(-2);
    }).join('');
  }

  function isSafeMethod(method) {
    return ['GET', 'HEAD', 'OPTIONS'].indexOf(String(method || '').toUpperCase()) >= 0;
  }

  function canReplay(method, idempotencyKey) {
    return isSafeMethod(method) || (typeof idempotencyKey === 'string' && idempotencyKey.length > 0);
  }

  function validateIdempotencyKey(value) {
    if (value === undefined || value === null || value === '') return null;
    var normalized = String(value).trim();
    if (!normalized || normalized.length > 128 || /[\r\n]/.test(normalized)) {
      throw new Error('Idempotency-Key must contain between 1 and 128 safe characters.');
    }
    return normalized;
  }

  function syntheticError(code, message, status, correlationId, flags) {
    var error = {
      isZumboApiError: true,
      status: status || 0,
      code: code,
      message: message,
      correlationId: correlationId || null,
      retryable: false,
      canceled: false,
      stale: false,
      data: {
        error: { code: code, message: message },
        correlationId: correlationId || null
      }
    };
    Object.keys(flags || {}).forEach(function(key) { error[key] = flags[key]; });
    return error;
  }

  function normalizeError(error, fallbackCorrelationId) {
    if (error && error.isZumboApiError) return error;
    var status = error && Number.isFinite(Number(error.status)) ? Number(error.status) : 0;
    var envelope = error && error.data && typeof error.data === 'object' ? error.data : null;
    var apiError = envelope && envelope.error && typeof envelope.error === 'object' ? envelope.error : null;
    var correlationId = envelope && typeof envelope.correlationId === 'string'
      ? envelope.correlationId
      : fallbackCorrelationId || null;
    var code = error && error.zumboCode;
    var message = error && error.zumboMessage;
    var canceled = code === 'REQUEST_CANCELLED' || status === -1;
    var stale = code === 'STALE_RESPONSE';

    if (!code && apiError && typeof apiError.code === 'string') code = apiError.code;
    if (!message && apiError && typeof apiError.message === 'string') message = apiError.message.slice(0, 500);
    if (!code) {
      if (canceled) code = 'REQUEST_CANCELLED';
      else if (status === 0) code = 'NETWORK_UNAVAILABLE';
      else if (status === 401) code = 'AUTHENTICATION_REQUIRED';
      else if (status === 403) code = 'FORBIDDEN';
      else if (status === 404) code = 'NOT_FOUND';
      else if (status === 409) code = 'CONFLICT';
      else if (status === 429) code = 'RATE_LIMITED';
      else if (status >= 500) code = 'SERVER_UNAVAILABLE';
      else code = 'REQUEST_FAILED';
    }
    if (!message) {
      if (stale) message = 'The response belongs to an inactive workspace.';
      else if (canceled) message = 'The request was canceled.';
      else if (status === 0) message = 'The service could not be reached.';
      else if (status === 401) message = 'Authentication is required.';
      else if (status === 403) message = 'This action is not permitted.';
      else if (status === 404) message = 'The requested resource was not found.';
      else if (status === 429) message = 'Too many requests were sent.';
      else if (status >= 500) message = 'The service is temporarily unavailable.';
      else message = 'The request could not be completed.';
    }

    return syntheticError(code, message, status, correlationId, {
      retryable: status === 0 || status === 408 || status === 425 || status === 429 || status >= 500,
      canceled: canceled,
      stale: stale
    });
  }

  function createSingleFlight() {
    var pending = null;
    return {
      run: function(start) {
        if (!pending) {
          pending = start();
          var active = pending;
          function clear() {
            if (pending === active) pending = null;
          }
          active.then(clear, clear);
        }
        return pending;
      },
      isPending: function() { return !!pending; }
    };
  }

  function createRequestRegistry() {
    var entries = Object.create(null);
    var generation = 0;
    var context = null;
    function cancelMatching(predicate, reason) {
      Object.keys(entries).forEach(function(id) {
        var entry = entries[id];
        if (!predicate(entry)) return;
        delete entries[id];
        entry.cancel(reason || 'canceled');
      });
    }
    return {
      register: function(id, scope, cancel) {
        entries[id] = { scope: scope || null, cancel: cancel, generation: generation };
        return generation;
      },
      finish: function(id) { delete entries[id]; },
      isCurrent: function(token) { return token === generation; },
      cancelScope: function(scope, reason) {
        cancelMatching(function(entry) { return entry.scope === scope; }, reason);
      },
      transition: function(nextContext) {
        if (context === nextContext) return generation;
        context = nextContext;
        generation += 1;
        cancelMatching(function() { return true; }, 'context-changed');
        return generation;
      },
      cancelAll: function(reason) {
        generation += 1;
        context = null;
        cancelMatching(function() { return true; }, reason || 'session-cleared');
      },
      activeCount: function() { return Object.keys(entries).length; }
    };
  }

  var tenantLocalKeys = Object.freeze([
    'zumbo.currentUser',
    'zumbo.projectId',
    'zumbo.recentProjects',
    'zumbo.favoriteProjects',
    'zumbo.collapsedColumns',
    'zumbo.cardFields',
    'zumbo.accessToken',
    'zumbo.refreshToken'
  ]);
  var tenantSessionKeys = Object.freeze(['zumbo.csrfToken']);

  function readCookie(cookieHeader, name) {
    if (typeof cookieHeader !== 'string' || !name) return null;
    var prefix = encodeURIComponent(name) + '=';
    var entry = cookieHeader.split(';').map(function(value) {
      return value.trim();
    }).find(function(value) {
      return value.indexOf(prefix) === 0;
    });
    if (!entry) return null;
    try {
      return decodeURIComponent(entry.slice(prefix.length)) || null;
    } catch (_) {
      return null;
    }
  }

  function clearTenantStorage(local, session) {
    tenantLocalKeys.forEach(function(key) { local.removeItem(key); });
    tenantSessionKeys.forEach(function(key) { session.removeItem(key); });
  }

  function unwrapResponseBody(body) {
    if (body
        && typeof body === 'object'
        && typeof body.success === 'boolean'
        && Object.prototype.hasOwnProperty.call(body, 'data')) {
      return body.data;
    }
    return body;
  }

  var core = Object.freeze({
    resolveBaseUrl: resolveBaseUrl,
    createIdentifier: createIdentifier,
    isSafeMethod: isSafeMethod,
    canReplay: canReplay,
    validateIdempotencyKey: validateIdempotencyKey,
    syntheticError: syntheticError,
    normalizeError: normalizeError,
    createSingleFlight: createSingleFlight,
    createRequestRegistry: createRequestRegistry,
    unwrapResponseBody: unwrapResponseBody,
    readCookie: readCookie,
    clearTenantStorage: clearTenantStorage,
    tenantLocalKeys: tenantLocalKeys,
    tenantSessionKeys: tenantSessionKeys
  });
  root.ZumboApiClientCore = core;

  if (!root.angular) return;

  var module = root.angular.module('zumbo.shared.api', []);
  var apiBaseUrl = resolveBaseUrl(root.__ZUMBO_RUNTIME_CONFIG__, root.location && root.location.origin);
  module.constant('API_BASE_URL', apiBaseUrl);

  module.factory('sessionStore', function() {
    root.localStorage.removeItem('zumbo.accessToken');
    root.localStorage.removeItem('zumbo.refreshToken');
    var parsedUser = null;
    try { parsedUser = JSON.parse(root.localStorage.getItem('zumbo.currentUser') || 'null'); } catch (_) { parsedUser = null; }
    var state = { currentUser: parsedUser, project: null, board: null, team: null };
    var service = {
      state: state,
      setUser: function(user) {
        state.currentUser = user || null;
        if (user) root.localStorage.setItem('zumbo.currentUser', JSON.stringify(user));
        else root.localStorage.removeItem('zumbo.currentUser');
      },
      getCsrf: function() {
        return root.sessionStorage.getItem('zumbo.csrfToken')
          || readCookie(root.document && root.document.cookie, 'zumbo-csrf');
      },
      setCsrf: function(token) {
        if (token) root.sessionStorage.setItem('zumbo.csrfToken', token);
        else root.sessionStorage.removeItem('zumbo.csrfToken');
      },
      clear: function() {
        state.currentUser = null;
        state.project = null;
        state.board = null;
        state.team = null;
        clearTenantStorage(root.localStorage, root.sessionStorage);
      }
    };
    Object.defineProperty(service, 'currentUser', {
      enumerable: true,
      get: function() { return state.currentUser; },
      set: function(user) { service.setUser(user); }
    });
    return service;
  });

  module.factory('tokenStorage', function(sessionStore) {
    return {
      getCsrf: sessionStore.getCsrf,
      setCsrf: sessionStore.setCsrf,
      clear: sessionStore.clear
    };
  });

  module.factory('apiClient', function($http, $q, $rootScope, API_BASE_URL, sessionStore) {
    var refreshGate = createSingleFlight();
    var requests = createRequestRegistry();
    var resourceVersions = Object.create(null);

    function resource(url) {
      var sprintItem = url.match(/^\/api\/sprints\/[^/?]+\/items\/([^/?]+)/);
      if (sprintItem) return { kind: 'work-items', id: sprintItem[1] };
      var template = url.match(/^\/api\/work-items\/templates\/([^/?]+)/);
      if (template) return { kind: 'work-item-templates', id: template[1] };
      var recurrence = url.match(/^\/api\/work-items\/recurrences\/([^/?]+)/);
      if (recurrence && ['preview', 'process-due'].indexOf(recurrence[1]) < 0) {
        return { kind: 'work-item-recurrences', id: recurrence[1] };
      }
      var automationRun = url.match(/^\/api\/automations\/runs\/([^/?]+)/);
      if (automationRun) return { kind: 'automation-runs', id: automationRun[1] };
      var automation = url.match(/^\/api\/automations\/([^/?]+)/);
      if (automation && automation[1] !== 'runs') {
        return { kind: 'automations', id: automation[1] };
      }
      var collaboration = url.match(/^\/api\/work-items\/([^/?]+)\/(?:collaboration|watch|vote|activity)(?:[/?]|$)/);
      if (collaboration) return { kind: 'work-item-collaboration', id: collaboration[1] };
      var intake = url.match(/^\/api\/intake\/forms(?:\/([^/?]+))?/);
      if (intake) return { kind: 'intake-forms', id: intake[1] || null };
      var match = url.match(/^\/api\/(teams|projects|boards|work-items|workflows|automations|dashboards|portfolios|goals|capacity-plans|knowledge-documents)(?:\/([^/?]+))?/);
      return match ? { kind: match[1], id: match[2] || null } : null;
    }
    function resourceKey(kind, id) { return kind + ':' + id; }
    function remember(url, data) {
      var target = resource(url);
      if (!target || !data) return data;
      var values = Array.isArray(data) ? data : [data];
      values.forEach(function(value) {
        if (value && value.id && Number(value.version) > 0) {
          resourceVersions[resourceKey(target.kind, value.id)] = Number(value.version);
        }
      });
      if (!Array.isArray(data) && target.id && Number(data.version) > 0) {
        resourceVersions[resourceKey(target.kind, target.id)] = Number(data.version);
      }
      return data;
    }
    function publicAuthCall(url) {
      return [
        '/api/browser-auth/login',
        '/api/browser-auth/register',
        '/api/browser-auth/session',
        '/api/browser-auth/refresh',
        '/api/browser-auth/logout',
        '/api/auth/forgot-password',
        '/api/auth/reset-password'
      ].indexOf(url.split('?', 1)[0]) >= 0;
    }
    function createHttpConfig(operation) {
      var headers = {
        'X-Correlation-Id': operation.correlationId
      };
      if (!isSafeMethod(operation.method)) {
        var csrf = sessionStore.getCsrf();
        if (csrf) headers['X-CSRF-Token'] = csrf;
      }
      if (operation.idempotencyKey) headers['Idempotency-Key'] = operation.idempotencyKey;
      if (operation.options.privacyStatusToken) {
        headers['X-Privacy-Status-Token'] = String(operation.options.privacyStatusToken);
      }
      var target = resource(operation.url);
      if (!isSafeMethod(operation.method) && target && target.id) {
        var version = resourceVersions[resourceKey(target.kind, target.id)];
        if (version) headers['If-Match'] = '"' + version + '"';
      }
      var config = {
        method: operation.method,
        url: API_BASE_URL + operation.url,
        data: operation.data,
        withCredentials: true,
        headers: headers,
        timeout: operation.cancellation.promise
      };
      if (operation.options.responseType) config.responseType = operation.options.responseType;
      if (operation.options.transformRequest) config.transformRequest = operation.options.transformRequest;
      if (operation.options.contentTypeUndefined) config.headers['Content-Type'] = undefined;
      return config;
    }
    function refreshSession() {
      return refreshGate.run(function() {
        var correlationId = createIdentifier('web-');
        var headers = { 'X-Correlation-Id': correlationId };
        var csrf = sessionStore.getCsrf();
        if (csrf) headers['X-CSRF-Token'] = csrf;
        return $http({
          method: 'POST',
          url: API_BASE_URL + '/api/browser-auth/refresh',
          data: {},
          withCredentials: true,
          headers: headers
        }).then(function(response) {
          var auth = response.data.data;
          sessionStore.setCsrf(auth.csrfToken);
          sessionStore.setUser(auth.user);
          return auth;
        }).catch(function(error) {
          resourceVersions = Object.create(null);
          requests.cancelAll('refresh-failed');
          sessionStore.clear();
          $rootScope.$broadcast('zumbo:session-expired');
          return $q.reject(error);
        });
      });
    }
    function send(operation) {
      return $http(createHttpConfig(operation)).catch(function(error) {
        if (error.status !== 401 || publicAuthCall(operation.url) || operation.options.refresh === false) {
          return $q.reject(error);
        }
        return refreshSession().then(function() {
          if (!requests.isCurrent(operation.generation)) {
            return $q.reject({ zumboCode: 'STALE_RESPONSE', zumboMessage: 'The response belongs to an inactive workspace.' });
          }
          if (!canReplay(operation.method, operation.idempotencyKey)) {
            return $q.reject(syntheticError(
              'REQUEST_REPLAY_REQUIRED',
              'Your session was renewed. Retry this action to avoid a duplicate change.',
              409,
              operation.correlationId
            ));
          }
          return $http(createHttpConfig(operation));
        });
      });
    }
    function execute(method, url, data, options) {
      options = options || {};
      var idempotencyKey;
      try { idempotencyKey = validateIdempotencyKey(options.idempotencyKey); }
      catch (_) {
        return $q.reject(syntheticError(
          'IDEMPOTENCY_KEY_INVALID',
          'The idempotency key must contain between 1 and 128 safe characters.',
          400,
          null
        ));
      }
      if (options.scope && options.replace) requests.cancelScope(options.scope, 'replaced');
      var operation = {
        id: createIdentifier('request-'),
        correlationId: options.correlationId || createIdentifier('web-'),
        idempotencyKey: idempotencyKey,
        method: String(method).toUpperCase(),
        url: url,
        data: data,
        options: options,
        cancellation: $q.defer(),
        generation: null,
        abortListener: null
      };
      operation.generation = requests.register(operation.id, options.scope, function() {
        operation.cancellation.resolve();
      });
      if (options.signal) {
        operation.abortListener = function() { operation.cancellation.resolve(); };
        if (options.signal.aborted) operation.abortListener();
        else options.signal.addEventListener('abort', operation.abortListener, { once: true });
      }
      return send(operation).then(function(response) {
        if (!requests.isCurrent(operation.generation)) {
          return $q.reject({ zumboCode: 'STALE_RESPONSE', zumboMessage: 'The response belongs to an inactive workspace.' });
        }
        if (options.rawResponse) return response;
        return remember(url, unwrapResponseBody(response.data));
      }).catch(function(error) {
        var target = resource(url);
        var apiError = error && error.data && error.data.error;
        if (error && error.status === 409 && apiError && apiError.code === 'CONCURRENCY_CONFLICT') {
          if (target && target.id) delete resourceVersions[resourceKey(target.kind, target.id)];
          $rootScope.$broadcast('zumbo:concurrency-conflict', { url: url, resource: target });
        }
        if (!requests.isCurrent(operation.generation) && !(error && error.zumboCode)) {
          error = { zumboCode: 'STALE_RESPONSE', zumboMessage: 'The response belongs to an inactive workspace.' };
        }
        return $q.reject(normalizeError(error, operation.correlationId));
      }).finally(function() {
        requests.finish(operation.id);
        if (options.signal && operation.abortListener) {
          options.signal.removeEventListener('abort', operation.abortListener);
        }
      });
    }

    return {
      get: function(url, options) { return execute('GET', url, undefined, options); },
      post: function(url, data, options) { return execute('POST', url, data, options); },
      put: function(url, data, options) { return execute('PUT', url, data, options); },
      patch: function(url, data, options) { return execute('PATCH', url, data, options); },
      delete: function(url, options) { return execute('DELETE', url, undefined, options); },
      upload: function(url, file, options) {
        var form = new FormData();
        form.append('file', file);
        return execute('POST', url, form, root.angular.extend({}, options, {
          contentTypeUndefined: true,
          transformRequest: root.angular.identity
        }));
      },
      download: function(url, options) {
        return execute('GET', url, undefined, root.angular.extend({}, options, {
          responseType: 'blob',
          rawResponse: true
        })).then(function(response) { return response.data; });
      },
      remember: remember,
      cancelScope: function(scope) { requests.cancelScope(scope, 'scope-canceled'); },
      cancelPending: function(reason) { requests.cancelAll(reason || 'pending-canceled'); },
      transitionContext: function(context) {
        resourceVersions = Object.create(null);
        return requests.transition(context);
      },
      clearSession: function(reason) {
        resourceVersions = Object.create(null);
        requests.cancelAll(reason || 'session-cleared');
        sessionStore.clear();
        $rootScope.$broadcast('zumbo:session-cleared');
      },
      newIdempotencyKey: function() { return createIdentifier('idem-'); },
      baseUrl: API_BASE_URL
    };
  });
})(window);
