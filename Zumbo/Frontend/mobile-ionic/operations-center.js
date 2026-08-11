(function() {
  'use strict';

  angular.module('zumboMobile').controller('OperationsCenterController', function(
    $q,
    $state,
    $window,
    $ionicPopup,
    apiClient,
    sessionStore,
    mobileActionError
  ) {
    var vm = this;
    var core = $window.ZumboOperationsCore;
    vm.loading = true;
    vm.forbidden = false;
    vm.busy = '';
    vm.error = '';
    vm.sectionErrors = {};
    vm.dependencies = [];
    vm.messaging = null;
    vm.messageDeadLetters = [];
    vm.notifications = null;
    vm.notificationDeadLetters = [];
    vm.storage = null;
    vm.searchResult = null;

    vm.back = function() { $state.go('app.profile'); };
    vm.canManage = function() {
      return core.hasPermission(sessionStore.state.currentUser, sessionStore.state.systemRoles);
    };
    vm.dependencyLabel = core.dependencyLabel;
    vm.dependencyState = core.dependencyState;
    vm.eventLabel = core.eventLabel;
    vm.notificationLabel = core.notificationTypeLabel;
    vm.overallState = function() {
      return core.overallState(vm.dependencies, vm.messaging, vm.notifications, vm.storage);
    };

    vm.load = function() {
      if (!vm.canManage()) {
        vm.forbidden = true;
        vm.loading = false;
        return $q.when([]);
      }
      var organizationId = sessionStore.state.currentUser.organizationId;
      vm.forbidden = false;
      vm.loading = true;
      vm.error = '';
      vm.sectionErrors = {};
      return $q.all([
        load('dependencies', apiClient.get(
          '/api/operations/external-dependencies',
          requestOptions('dependencies')), function(result) {
          vm.dependencies = result.dependencies || [];
        }),
        load('messaging', apiClient.get(
          '/api/work-items/durable-messaging/metrics',
          requestOptions('messaging')), function(result) {
          vm.messaging = result;
        }),
        load('messageDeadLetters', apiClient.get(
          '/api/work-items/durable-messaging/dead-letters?pageSize=10',
          requestOptions('messageDeadLetters')), function(result) {
          vm.messageDeadLetters = result || [];
        }),
        load('notifications', apiClient.get(
          '/api/notifications/delivery/status?organizationId=' + encodeURIComponent(organizationId),
          requestOptions('notifications')), function(result) {
          vm.notifications = result;
        }),
        load('notificationDeadLetters', apiClient.get(
          '/api/notifications/delivery/dead-letters?organizationId='
            + encodeURIComponent(organizationId) + '&pageSize=10',
          requestOptions('notificationDeadLetters')), function(result) {
          vm.notificationDeadLetters = result || [];
        }),
        load('storage', apiClient.get(
          '/api/operations/storage/security?organizationId=' + encodeURIComponent(organizationId),
          requestOptions('storage')), function(result) {
          vm.storage = result;
        })
      ]).finally(function() {
        vm.loading = false;
      });
    };

    vm.reconcileSearch = function() {
      return confirmAction(
        'Aramayı uzlaştır',
        'Arama görünümü güncel iş kayıtlarıyla yeniden kurulacak.',
        'search',
        function() {
          return apiClient.post('/api/work-items/search/reconcile', {}).then(function(result) {
            vm.searchResult = result;
            return vm.load();
          });
        });
    };

    vm.replayMessage = function(item) {
      if (!core.canReplay(item)) return;
      return confirmAction(
        'Sistem olayını yeniden dene',
        'Seçilen olay yeniden işleme sırasına alınacak.',
        item.id,
        function() {
          return apiClient.post('/api/work-items/durable-messaging/dead-letter/'
            + encodeURIComponent(item.id) + '/replay', {}).then(function(result) {
            if (!result.replayed) throw new Error('MESSAGE_STATE_CHANGED');
            return vm.load();
          });
        });
    };

    vm.replayNotification = function(item) {
      if (!core.canReplay(item)) return;
      return confirmAction(
        'Teslimatı yeniden dene',
        'Seçilen bildirim yeniden teslimat sırasına alınacak.',
        item.id,
        function() {
          var organizationId = sessionStore.state.currentUser.organizationId;
          return apiClient.post('/api/notifications/delivery/' + encodeURIComponent(item.id)
            + '/replay?organizationId=' + encodeURIComponent(organizationId), {}).then(vm.load);
        });
    };

    vm.runStorageMaintenance = function() {
      return confirmAction(
        'Karantinayı denetle',
        'Bu organizasyondaki karantina kayıtları yeniden taranacak.',
        'storage',
        function() {
          var organizationId = sessionStore.state.currentUser.organizationId;
          return apiClient.post('/api/operations/storage/security/maintenance?organizationId='
            + encodeURIComponent(organizationId), {}).then(vm.load);
        });
    };

    function requestOptions(key) {
      return {
        scope: 'mobile-operations-' + key,
        replace: true
      };
    }

    function load(key, request, assign) {
      return request.then(assign).catch(function(error) {
        if (error && error.status === 403) vm.forbidden = true;
        vm.sectionErrors[key] = mobileActionError(
          error,
          'Bu operasyon bölümü şu anda alınamıyor.');
      });
    }

    function confirmAction(title, template, key, operation) {
      if (vm.busy || !key) return;
      return $ionicPopup.confirm({
        title: title,
        template: template,
        cancelText: 'Vazgeç',
        okText: 'Devam et'
      }).then(function(confirmed) {
        if (!confirmed) return;
        vm.busy = key;
        vm.error = '';
        return operation().catch(function(error) {
          vm.error = mobileActionError(
            error,
            'Operasyon tamamlanamadı; güncel durum yeniden yüklenebilir.');
        }).finally(function() {
          vm.busy = '';
        });
      });
    }

    vm.load();
  });
})();
