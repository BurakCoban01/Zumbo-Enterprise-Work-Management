(function() {
  'use strict';

  angular.module('zumboMobile')
    .factory('mobilePwaService', function($rootScope, $window) {
      var state = {
        offline: !$window.navigator.onLine,
        updateReady: false,
        installError: false,
        registration: null
      };
      var started = false;
      var refreshing = false;
      var controlled = Boolean($window.navigator.serviceWorker && $window.navigator.serviceWorker.controller);

      function updateConnectivity(event) {
        var offline = event && event.type
          ? event.type === 'offline'
          : !$window.navigator.onLine;
        $rootScope.$evalAsync(function() {
          state.offline = offline;
        });
        if (!offline && state.registration) {
          state.registration.update().catch(angular.noop);
        }
      }

      function watchRegistration(registration) {
        state.registration = registration;
        state.installError = false;
        if (registration.waiting && $window.navigator.serviceWorker.controller) {
          state.updateReady = true;
        }
        function watchWorker(worker) {
          if (!worker) return;
          worker.addEventListener('statechange', function() {
            if (worker.state === 'installed' && $window.navigator.serviceWorker.controller) {
              $rootScope.$evalAsync(function() { state.updateReady = true; });
            }
            if (worker.state === 'redundant' && !registration.active) {
              $rootScope.$evalAsync(function() { state.installError = true; });
            }
          });
        }
        watchWorker(registration.installing);
        registration.addEventListener('updatefound', function() {
          watchWorker(registration.installing);
        });
      }

      function register() {
        return $window.navigator.serviceWorker.register('./service-worker.js', { updateViaCache: 'none' })
          .then(watchRegistration)
          .catch(function() {
            $rootScope.$evalAsync(function() { state.installError = true; });
          });
      }

      return {
        state: state,
        start: function() {
          if (started) return;
          started = true;
          $window.addEventListener('online', updateConnectivity);
          $window.addEventListener('offline', updateConnectivity);
          if (!('serviceWorker' in $window.navigator)) return;
          $window.navigator.serviceWorker.addEventListener('controllerchange', function() {
            if (!controlled) {
              controlled = true;
              return;
            }
            if (refreshing) return;
            refreshing = true;
            $window.location.reload();
          });
          if ($window.document.readyState === 'complete') register();
          else $window.addEventListener('load', register);
        },
        applyUpdate: function() {
          var waiting = state.registration && state.registration.waiting;
          if (waiting) waiting.postMessage({ type: 'SKIP_WAITING' });
        }
      };
    });
})();
