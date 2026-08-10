(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('realtimeService', function($q, $rootScope, API_BASE_URL) {
      var protocolVersion = 1;
      var connection = null;
      var projectId = null;
      var requestedProjectId = null;
      var transition = $q.when();
      var listeners = [];
      var knownVersions = Object.create(null);
      var resyncPending = false;

      function notify(change) {
        $rootScope.$evalAsync(function() {
          listeners.slice().forEach(function(listener) { listener(change); });
        });
      }

      function requestResync(reason) {
        if (!projectId || resyncPending) return;
        resyncPending = true;
        notify({
          eventType: 'resyncRequired',
          projectId: projectId,
          schemaVersion: protocolVersion,
          reason: reason
        });
      }

      function acceptChange(change) {
        if (!change || change.projectId !== projectId) return;
        if (change.schemaVersion !== protocolVersion || !Number.isSafeInteger(change.resourceVersion)) {
          requestResync('protocol');
          return;
        }
        var previous = knownVersions[change.workItemId];
        if (previous !== undefined && change.resourceVersion <= previous) return;
        if (previous !== undefined && change.resourceVersion > previous + 1) {
          requestResync('version-gap');
          return;
        }
        knownVersions[change.workItemId] = change.resourceVersion;
        notify(change);
      }

      function synchronize(items) {
        knownVersions = Object.create(null);
        (items || []).forEach(remember);
        resyncPending = false;
      }

      function remember(item) {
        if (!item || !item.id || !Number.isSafeInteger(item.version)) return;
        var previous = knownVersions[item.id];
        if (previous === undefined || item.version > previous) knownVersions[item.id] = item.version;
      }

      window.addEventListener('online', function() {
        requestResync('network-online');
      });

      function connect(nextProjectId) {
        if (!window.signalR) { return $q.reject(new Error('Realtime client is unavailable.')); }
        requestedProjectId = nextProjectId;
        transition = transition.catch(angular.noop).then(function() {
          if (requestedProjectId !== nextProjectId) return;
          if (connection && projectId === nextProjectId) return;
          var active = connection;
          connection = null;
          projectId = null;
          return $q.when(active ? active.stop() : null).then(function() {
            if (requestedProjectId !== nextProjectId) return;
            projectId = nextProjectId;
            synchronize([]);
            connection = new signalR.HubConnectionBuilder()
              .withUrl(API_BASE_URL + '/hubs/work-items', {
                withCredentials: true,
                transport: signalR.HttpTransportType.WebSockets,
                skipNegotiation: true,
                headers: { 'X-CSRF-Token': window.sessionStorage.getItem('zumbo.csrfToken') || '' }
              })
              .withAutomaticReconnect([0, 2000, 5000, 10000])
              .withStatefulReconnect({ bufferSize: 65536 })
              .build();
            connection.on('workItemChanged', acceptChange);
            connection.onreconnected(function() {
              if (!projectId) return;
              synchronize([]);
              connection.invoke('SubscribeProject', projectId).then(function() {
                requestResync('reconnected');
              }).catch(function() {
                requestResync('subscription-failed');
              });
            });
            return connection.start().then(function() {
              if (requestedProjectId !== nextProjectId) return;
              return connection.invoke('SubscribeProject', projectId);
            });
          });
        });
        return transition;
      }

      return {
        connect: connect,
        remember: remember,
        synchronize: synchronize,
        subscribe: function(listener) {
          listeners.push(listener);
          return function() {
            var index = listeners.indexOf(listener);
            if (index >= 0) { listeners.splice(index, 1); }
          };
        },
        stop: function() {
          requestedProjectId = null;
          transition = transition.catch(angular.noop).then(function() {
            projectId = null;
            if (!connection) return;
            var active = connection;
            connection = null;
            return active.stop();
          });
          return transition;
        }
      };
    });
})();
