(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopOperationsFeature', function($q, $window, apiClient) {
      var core = $window.ZumboOperationsCore;

      return {
        install: function(vm, apiActionError) {
          var openSettingsTab = vm.openSettingsTab;
          vm.operationsCenter = {
            loading: false,
            forbidden: false,
            action: '',
            error: '',
            sectionErrors: {},
            dependencies: [],
            messaging: null,
            messageDeadLetters: [],
            notifications: null,
            notificationDeadLetters: [],
            storage: null,
            searchResult: null
          };

          vm.canManageOperations = function() {
            return core.hasPermission(vm.session.currentUser, vm.roles);
          };

          vm.openSettingsTab = function(tab) {
            if (tab === 'operations') {
              if (!vm.canManageOperations()) return;
              vm.settingsTab = tab;
              return vm.loadOperationsCenter();
            }
            return openSettingsTab(tab);
          };

          vm.loadOperationsCenter = function() {
            if (!vm.canManageOperations()) {
              vm.operationsCenter.forbidden = true;
              return $q.when([]);
            }
            var organizationId = vm.session.currentUser.organizationId;
            vm.operationsCenter.loading = true;
            vm.operationsCenter.forbidden = false;
            vm.operationsCenter.error = '';
            vm.operationsCenter.sectionErrors = {};
            return $q.all([
              load('dependencies', apiClient.get(
                '/api/operations/external-dependencies',
                requestOptions('dependencies')), function(result) {
                vm.operationsCenter.dependencies = result.dependencies || [];
              }),
              load('messaging', apiClient.get(
                '/api/work-items/durable-messaging/metrics',
                requestOptions('messaging')), function(result) {
                vm.operationsCenter.messaging = result;
              }),
              load('messageDeadLetters', apiClient.get(
                '/api/work-items/durable-messaging/dead-letters?pageSize=20',
                requestOptions('messageDeadLetters')), function(result) {
                vm.operationsCenter.messageDeadLetters = result || [];
              }),
              load('notifications', apiClient.get(
                '/api/notifications/delivery/status?organizationId=' + encodeURIComponent(organizationId),
                requestOptions('notifications')), function(result) {
                vm.operationsCenter.notifications = result;
              }),
              load('notificationDeadLetters', apiClient.get(
                '/api/notifications/delivery/dead-letters?organizationId='
                  + encodeURIComponent(organizationId) + '&pageSize=20',
                requestOptions('notificationDeadLetters')), function(result) {
                vm.operationsCenter.notificationDeadLetters = result || [];
              }),
              load('storage', apiClient.get(
                '/api/operations/storage/security?organizationId=' + encodeURIComponent(organizationId),
                requestOptions('storage')), function(result) {
                vm.operationsCenter.storage = result;
              })
            ]).finally(function() {
              vm.operationsCenter.loading = false;
            });
          };

          vm.reconcileSearch = function() {
            if (blocked('search')) return;
            if (!$window.confirm('Arama görünümü güncel kayıtlarla uzlaştırılsın mı?')) return;
            vm.operationsCenter.action = 'search';
            return apiClient.post('/api/work-items/search/reconcile', {}).then(function(result) {
              vm.operationsCenter.searchResult = result;
              vm.notify('success', 'Arama görünümü uzlaştırıldı.');
              return vm.loadOperationsCenter();
            }).catch(actionFailed).finally(clearAction);
          };

          vm.replayDurableMessage = function(item) {
            if (!core.canReplay(item) || blocked(item.id)) return;
            if (!$window.confirm('Seçilen sistem olayı yeniden işleme sırasına alınsın mı?')) return;
            vm.operationsCenter.action = item.id;
            return apiClient.post('/api/work-items/durable-messaging/dead-letter/'
              + encodeURIComponent(item.id) + '/replay', {}).then(function(result) {
                if (!result.replayed) throw new Error('MESSAGE_STATE_CHANGED');
                vm.notify('success', 'Sistem olayı yeniden sıraya alındı.');
                return vm.loadOperationsCenter();
              }).catch(actionFailed).finally(clearAction);
          };

          vm.replayNotificationDelivery = function(item) {
            if (!core.canReplay(item) || blocked(item.id)) return;
            if (!$window.confirm('Seçilen bildirim teslimatı yeniden sıraya alınsın mı?')) return;
            vm.operationsCenter.action = item.id;
            var organizationId = vm.session.currentUser.organizationId;
            return apiClient.post('/api/notifications/delivery/' + encodeURIComponent(item.id)
              + '/replay?organizationId=' + encodeURIComponent(organizationId), {}).then(function() {
              vm.notify('success', 'Bildirim teslimatı yeniden sıraya alındı.');
              return vm.loadOperationsCenter();
            }).catch(actionFailed).finally(clearAction);
          };

          vm.runStorageMaintenance = function() {
            if (blocked('storage')) return;
            if (!$window.confirm('Bu organizasyondaki karantina kayıtları yeniden denetlensin mi?')) return;
            vm.operationsCenter.action = 'storage';
            var organizationId = vm.session.currentUser.organizationId;
            return apiClient.post('/api/operations/storage/security/maintenance?organizationId='
              + encodeURIComponent(organizationId), {}).then(function(result) {
              vm.notify('success', result.retried
                ? result.retried + ' karantina kaydı yeniden denetlendi.'
                : 'Karantinada yeniden denetlenecek kayıt yok.');
              return vm.loadOperationsCenter();
            }).catch(actionFailed).finally(clearAction);
          };

          vm.operationsOverallState = function() {
            return core.overallState(
              vm.operationsCenter.dependencies,
              vm.operationsCenter.messaging,
              vm.operationsCenter.notifications,
              vm.operationsCenter.storage);
          };
          vm.operationDependencyLabel = core.dependencyLabel;
          vm.operationDependencyState = core.dependencyState;
          vm.operationEventLabel = core.eventLabel;
          vm.operationNotificationLabel = core.notificationTypeLabel;

          function requestOptions(key) {
            return {
              scope: 'desktop-operations-' + key,
              replace: true
            };
          }

          function load(key, request, assign) {
            return request.then(assign).catch(function(error) {
              if (error && error.status === 403) vm.operationsCenter.forbidden = true;
              vm.operationsCenter.sectionErrors[key] = apiActionError(
                error,
                'Bu operasyon bölümü şu anda alınamıyor.');
            });
          }

          function blocked(action) {
            return !!(vm.operationsCenter.action || vm.pwa.offline || !action);
          }

          function actionFailed(error) {
            vm.operationsCenter.error = apiActionError(
              error,
              'Operasyon tamamlanamadı; güncel durum yeniden yüklenebilir.');
          }

          function clearAction() {
            vm.operationsCenter.action = '';
          }
        }
      };
    });
})();
