(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('IntegrationCenterController', function(
    $scope,
    $state,
    $q,
    $timeout,
    $window,
    $ionicPopup,
    sessionStore,
    zumboApi,
    mobileActionError
  ) {
    var vm = this;
    var core = $window.ZumboWebhookCore;
    var refreshTimer;
    vm.scopes = core.scopes;
    vm.roles = [];
    vm.loading = true;
    vm.busy = '';
    vm.error = '';
    vm.forbidden = false;
    vm.view = 'list';
    vm.subscriptions = [];
    vm.selected = null;
    vm.metrics = null;
    vm.deliveries = [];
    vm.nextCursor = null;
    vm.draft = core.emptyDraft();
    vm.editorMode = 'create';
    vm.secretReceipt = null;

    vm.load = function() {
      clearSecret();
      vm.loading = true;
      vm.error = '';
      vm.forbidden = false;
      return zumboApi.roles().then(function(roles) {
        vm.roles = roles || [];
        if (!core.hasPermission(sessionStore.state.currentUser, vm.roles)) {
          vm.forbidden = true;
          vm.subscriptions = [];
          return [];
        }
        return $q.all([zumboApi.webhookSubscriptions(), zumboApi.webhookMetrics()])
          .then(function(results) {
            vm.subscriptions = results[0] || [];
            vm.metrics = results[1] || null;
            if (!vm.selected && vm.subscriptions.length) return vm.select(vm.subscriptions[0], false);
            if (vm.selected) {
              vm.selected = vm.subscriptions.find(function(item) {
                return item.id === vm.selected.id;
              }) || null;
              if (vm.selected) return vm.loadDeliveries(true);
            }
            vm.deliveries = [];
            vm.view = vm.subscriptions.length ? vm.view : 'list';
            return [];
          });
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Entegrasyonlar yüklenemedi.');
      }).finally(function() {
        vm.loading = false;
        $scope.$broadcast('scroll.refreshComplete');
      });
    };

    vm.select = function(subscription, showDetail) {
      clearSecret();
      vm.selected = subscription;
      vm.error = '';
      if (showDetail !== false) vm.view = 'detail';
      return vm.loadDeliveries(true);
    };

    vm.loadDeliveries = function(reset) {
      if (!vm.selected) return $q.when([]);
      if (reset) {
        vm.deliveries = [];
        vm.nextCursor = null;
      }
      return zumboApi.webhookDeliveries(
        vm.selected.id,
        reset ? null : vm.nextCursor
      ).then(function(page) {
        vm.deliveries = reset ? (page.items || []) : vm.deliveries.concat(page.items || []);
        vm.nextCursor = page.nextCursor || null;
        return vm.deliveries;
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Teslimatlar yüklenemedi.');
        return [];
      });
    };

    vm.newSubscription = function() {
      clearSecret();
      vm.editorMode = 'create';
      vm.draft = core.emptyDraft();
      vm.view = 'editor';
      vm.error = '';
    };

    vm.editSubscription = function() {
      if (!vm.selected) return;
      clearSecret();
      vm.editorMode = 'edit';
      vm.draft = core.draftFrom(vm.selected);
      vm.view = 'editor';
      vm.error = '';
    };

    vm.cancelEditor = function() {
      vm.view = vm.selected ? 'detail' : 'list';
      vm.draft = core.emptyDraft();
    };

    vm.toggleScope = function(scope) {
      core.toggleScope(vm.draft, scope);
    };

    vm.scopeSelected = function(scope) {
      return vm.draft.eventScopes.indexOf(scope) >= 0;
    };

    vm.save = function() {
      if (vm.busy) return;
      var request;
      try {
        request = core.validateDraft(vm.draft);
      } catch (error) {
        vm.error = error.message;
        return;
      }
      vm.busy = 'save';
      vm.error = '';
      var operation = vm.editorMode === 'edit'
        ? zumboApi.updateWebhookSubscription(vm.selected.id, request)
        : zumboApi.createWebhookSubscription(request);
      return operation.then(function(result) {
        var subscription = result.subscription || result;
        replaceSubscription(subscription);
        vm.selected = subscription;
        vm.view = 'detail';
        if (result.secret) {
          vm.secretReceipt = {
            secret: result.secret,
            fingerprint: subscription.secretFingerprint,
            version: subscription.secretVersion
          };
        }
        return refreshOperationalState();
      }).catch(handleMutationError).finally(function() {
        vm.busy = '';
      });
    };

    vm.rotateSecret = function() {
      if (!vm.selected || vm.busy) return;
      return confirm(
        'Sırrı döndür',
        'Mevcut sır kısa geçiş süresinden sonra geçersiz olacak.',
        'Yeni sır oluştur'
      ).then(function(accepted) {
        if (!accepted) return;
        clearSecret();
        vm.busy = 'rotate';
        return zumboApi.rotateWebhookSecret(vm.selected.id, vm.selected.version)
          .then(function(receipt) {
            replaceSubscription(receipt.subscription);
            vm.selected = receipt.subscription;
            vm.secretReceipt = {
              secret: receipt.secret,
              fingerprint: receipt.subscription.secretFingerprint,
              version: receipt.subscription.secretVersion
            };
          }).catch(handleMutationError).finally(function() { vm.busy = ''; });
      });
    };

    vm.setActive = function(active) {
      if (!vm.selected || vm.busy) return;
      var confirmation = active
        ? $q.when(true)
        : confirm('Webhook’u durdur', 'Bu uç nokta için yeni teslimatlar duracak.', 'Durdur');
      return confirmation.then(function(accepted) {
        if (!accepted) return;
        clearSecret();
        vm.busy = active ? 'enable' : 'disable';
        return zumboApi.setWebhookActive(vm.selected.id, active, vm.selected.version)
          .then(function(subscription) {
            replaceSubscription(subscription);
            vm.selected = subscription;
          }).catch(handleMutationError).finally(function() { vm.busy = ''; });
      });
    };

    vm.sendTest = function() {
      if (!vm.selected || !vm.selected.isActive || vm.busy) return;
      vm.busy = 'test';
      vm.error = '';
      return zumboApi.sendWebhookTest(vm.selected.id).then(function(delivery) {
        vm.deliveries.unshift(delivery);
        scheduleRefresh();
        return zumboApi.webhookMetrics().then(function(metrics) { vm.metrics = metrics; });
      }).catch(handleMutationError).finally(function() { vm.busy = ''; });
    };

    vm.replay = function(delivery) {
      if (!core.canReplay(delivery) || vm.busy) return;
      return confirm(
        'Teslimatı yeniden dene',
        'Aynı değişmez yük yeniden teslimat sırasına alınacak.',
        'Yeniden sırala'
      ).then(function(accepted) {
        if (!accepted) return;
        vm.busy = delivery.id;
        return zumboApi.replayWebhookDelivery(delivery.id).then(function(replayed) {
          vm.deliveries = vm.deliveries.map(function(item) {
            return item.id === replayed.id ? replayed : item;
          });
          scheduleRefresh();
          return zumboApi.webhookMetrics().then(function(metrics) { vm.metrics = metrics; });
        }).catch(handleMutationError).finally(function() { vm.busy = ''; });
      });
    };

    vm.copySecret = function() {
      if (!vm.secretReceipt || !$window.navigator.clipboard) return;
      return $window.navigator.clipboard.writeText(vm.secretReceipt.secret);
    };

    vm.dismissSecret = clearSecret;
    vm.targetLabel = core.safeTargetLabel;
    vm.scopeLabel = core.scopeLabel;
    vm.deliveryState = core.deliveryState;
    vm.safeError = core.safeError;
    vm.canReplay = core.canReplay;
    vm.shortHash = core.shortHash;
    vm.back = function() {
      clearSecret();
      if (vm.view === 'editor' || vm.view === 'detail') {
        vm.view = 'list';
        return;
      }
      $state.go('app.profile');
    };

    function replaceSubscription(subscription) {
      var found = false;
      vm.subscriptions = vm.subscriptions.map(function(item) {
        if (item.id !== subscription.id) return item;
        found = true;
        return subscription;
      });
      if (!found) vm.subscriptions.unshift(subscription);
    }

    function refreshOperationalState() {
      return $q.all([
        zumboApi.webhookMetrics().then(function(metrics) { vm.metrics = metrics; }),
        vm.loadDeliveries(true)
      ]);
    }

    function scheduleRefresh() {
      if (refreshTimer) $timeout.cancel(refreshTimer);
      refreshTimer = $timeout(refreshOperationalState, 1200);
    }

    function clearSecret() {
      vm.secretReceipt = null;
      if (refreshTimer) {
        $timeout.cancel(refreshTimer);
        refreshTimer = null;
      }
    }

    function handleMutationError(error) {
      vm.error = mobileActionError(error, 'Webhook işlemi tamamlanamadı.');
      var code = error && error.data && error.data.error && error.data.error.code;
      if (error && (error.status === 409 || code === 'WEBHOOK_SUBSCRIPTION_CONFLICT')) {
        return vm.load();
      }
    }

    function confirm(title, template, okText) {
      return $ionicPopup.confirm({
        title: title,
        template: template,
        cancelText: 'Vazgeç',
        okText: okText
      });
    }

    $scope.$on('$ionicView.beforeEnter', vm.load);
    $scope.$on('$ionicView.afterLeave', clearSecret);
    $scope.$on('$destroy', clearSecret);
  });
})();
