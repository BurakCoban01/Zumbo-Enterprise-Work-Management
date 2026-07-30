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
    var developmentCore = $window.ZumboDevelopmentIntegrationCore;
    var refreshTimer;
    vm.scopes = core.scopes;
    vm.developmentProviders = developmentCore.providers;
    vm.roles = [];
    vm.loading = true;
    vm.busy = '';
    vm.error = '';
    vm.forbidden = false;
    vm.surface = 'webhooks';
    vm.view = 'list';
    vm.subscriptions = [];
    vm.selected = null;
    vm.metrics = null;
    vm.deliveries = [];
    vm.nextCursor = null;
    vm.draft = core.emptyDraft();
    vm.editorMode = 'create';
    vm.secretReceipt = null;
    vm.development = {
      connections: [],
      selected: null,
      mappings: [],
      repositories: [],
      repositoryStatus: '',
      projects: [],
      draft: developmentCore.emptyConnectionDraft(),
      credentialDraft: '',
      mappingDraft: { projectId: '', repositoryId: '' },
      secretReceipt: null
    };

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
        if (vm.surface === 'development') return loadDevelopment();
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

    vm.openSurface = function(surface) {
      if (surface !== 'webhooks' && surface !== 'development') return;
      clearSecret();
      vm.surface = surface;
      vm.view = 'list';
      vm.selected = null;
      vm.development.selected = null;
      return vm.load();
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

    vm.selectDevelopment = function(connection) {
      clearSecret();
      vm.development.selected = connection;
      vm.view = 'development-detail';
      vm.error = '';
      vm.development.repositories = [];
      vm.development.repositoryStatus = '';
      return loadDevelopmentMappings();
    };

    vm.newDevelopment = function() {
      clearSecret();
      vm.development.selected = null;
      vm.development.draft = developmentCore.emptyConnectionDraft();
      vm.view = 'development-editor';
      vm.error = '';
    };

    vm.selectDevelopmentProvider = function(provider) {
      developmentCore.selectProvider(vm.development.draft, provider);
    };

    vm.cancelDevelopmentEditor = function() {
      vm.view = 'list';
      vm.development.draft = developmentCore.emptyConnectionDraft();
    };

    vm.saveDevelopment = function() {
      if (vm.busy) return;
      var request;
      try {
        request = developmentCore.validateConnectionDraft(vm.development.draft);
      } catch (error) {
        vm.error = error.message;
        return;
      }
      vm.busy = 'development-save';
      vm.error = '';
      return zumboApi.createDevelopmentConnection(request).then(function(receipt) {
        replaceDevelopmentConnection(receipt.connection);
        vm.development.selected = receipt.connection;
        vm.development.secretReceipt = {
          secret: receipt.webhookSecret,
          fingerprint: receipt.connection.webhookSecretFingerprint,
          version: receipt.connection.webhookSecretVersion
        };
        vm.view = 'development-detail';
        return loadDevelopmentMappings();
      }).catch(handleDevelopmentError).finally(function() {
        vm.busy = '';
        vm.development.draft.accessToken = '';
      });
    };

    vm.checkDevelopmentHealth = function() {
      var selected = vm.development.selected;
      if (!selected || !selected.isConnected || vm.busy) return;
      vm.busy = 'development-health';
      vm.error = '';
      return zumboApi.checkDevelopmentHealth(selected.id).then(function() {
        return zumboApi.developmentConnection(selected.id);
      }).then(function(connection) {
        replaceDevelopmentConnection(connection);
        vm.development.selected = connection;
      }).catch(handleDevelopmentError).finally(function() { vm.busy = ''; });
    };

    vm.discoverDevelopmentRepositories = function() {
      var selected = vm.development.selected;
      if (!selected || !selected.isConnected || vm.busy) return;
      vm.busy = 'development-repositories';
      vm.error = '';
      return zumboApi.developmentRepositories(selected.id).then(function(page) {
        vm.development.repositories = page.items || [];
        vm.development.repositoryStatus = page.sourceStatus || 'Complete';
      }).catch(handleDevelopmentError).finally(function() { vm.busy = ''; });
    };

    vm.createDevelopmentMapping = function() {
      var selected = vm.development.selected;
      if (!selected || vm.busy) return;
      var repository = vm.development.repositories.find(function(item) {
        return item.externalRepositoryId === vm.development.mappingDraft.repositoryId;
      });
      var request;
      try {
        request = developmentCore.mappingRequest(
          vm.development.mappingDraft.projectId,
          repository
        );
      } catch (error) {
        vm.error = error.message;
        return;
      }
      vm.busy = 'development-mapping';
      return zumboApi.createDevelopmentMapping(selected.id, request).then(function(mapping) {
        vm.development.mappings.push(mapping);
        vm.development.mappingDraft = { projectId: '', repositoryId: '' };
      }).catch(handleDevelopmentError).finally(function() { vm.busy = ''; });
    };

    vm.deleteDevelopmentMapping = function(mapping) {
      if (!mapping || vm.busy) return;
      return confirm(
        'Eşlemeyi kaldır',
        mapping.repositoryFullName
          + ' eşlemesi ve ona bağlı geliştirme bağlantıları kaldırılacak.',
        'Kaldır'
      ).then(function(accepted) {
        if (!accepted) return;
        vm.busy = mapping.id;
        return zumboApi.deleteDevelopmentMapping(mapping.id, mapping.version)
          .then(function() {
            vm.development.mappings = vm.development.mappings.filter(function(item) {
              return item.id !== mapping.id;
            });
          }).catch(handleDevelopmentError).finally(function() { vm.busy = ''; });
      });
    };

    vm.rotateDevelopmentCredential = function() {
      var selected = vm.development.selected;
      var credential = String(vm.development.credentialDraft || '').trim();
      if (!selected || vm.busy) return;
      if (credential.length < 16 || credential.length > 512 || /\s/.test(credential)) {
        vm.error = 'Erişim anahtarı 16 ile 512 arasında boşluksuz karakter içermelidir.';
        return;
      }
      vm.busy = 'development-credential';
      return zumboApi.rotateDevelopmentCredential(
        selected.id,
        credential,
        selected.version
      ).then(function(connection) {
        replaceDevelopmentConnection(connection);
        vm.development.selected = connection;
        vm.development.credentialDraft = '';
      }).catch(handleDevelopmentError).finally(function() {
        vm.busy = '';
        vm.development.credentialDraft = '';
      });
    };

    vm.rotateDevelopmentSecret = function() {
      var selected = vm.development.selected;
      if (!selected || vm.busy) return;
      return confirm(
        'Webhook sırrını döndür',
        'Mevcut sır 15 dakikalık geçiş süresinden sonra geçersiz olacak.',
        'Yeni sır oluştur'
      ).then(function(accepted) {
        if (!accepted) return;
        clearSecret();
        vm.busy = 'development-secret';
        return zumboApi.rotateDevelopmentSecret(selected.id, selected.version)
          .then(function(receipt) {
            replaceDevelopmentConnection(receipt.connection);
            vm.development.selected = receipt.connection;
            vm.development.secretReceipt = {
              secret: receipt.webhookSecret,
              fingerprint: receipt.connection.webhookSecretFingerprint,
              version: receipt.connection.webhookSecretVersion
            };
          }).catch(handleDevelopmentError).finally(function() { vm.busy = ''; });
      });
    };

    vm.disconnectDevelopment = function() {
      var selected = vm.development.selected;
      if (!selected || !selected.isConnected || vm.busy) return;
      return confirm(
        'Bağlantıyı kes',
        'Erişim anahtarı ve webhook sırları kalıcı olarak silinecek.',
        'Bağlantıyı kes'
      ).then(function(accepted) {
        if (!accepted) return;
        clearSecret();
        vm.busy = 'development-disconnect';
        return zumboApi.disconnectDevelopmentConnection(selected.id, selected.version)
          .then(function(connection) {
            replaceDevelopmentConnection(connection);
            vm.development.selected = connection;
            vm.development.repositories = [];
            return loadDevelopmentMappings();
          }).catch(handleDevelopmentError).finally(function() { vm.busy = ''; });
      });
    };

    vm.deleteDevelopment = function() {
      var selected = vm.development.selected;
      if (!selected || vm.busy) return;
      return confirm(
        'Bağlantıyı sil',
        selected.name + ' ve tüm ilişkili eşlemeler kalıcı olarak silinecek.',
        'Kalıcı sil'
      ).then(function(accepted) {
        if (!accepted) return;
        clearSecret();
        vm.busy = 'development-delete';
        return zumboApi.deleteDevelopmentConnection(selected.id, selected.version)
          .then(function() {
            vm.development.connections = vm.development.connections.filter(function(item) {
              return item.id !== selected.id;
            });
            vm.development.selected = null;
            vm.development.mappings = [];
            vm.view = 'list';
          }).catch(handleDevelopmentError).finally(function() { vm.busy = ''; });
      });
    };

    vm.developmentHealthState = developmentCore.healthState;
    vm.developmentSafeHealthError = developmentCore.safeHealthError;
    vm.developmentSafeUrl = developmentCore.safeUrlLabel;
    vm.developmentShortFingerprint = developmentCore.shortFingerprint;

    vm.copySecret = function() {
      var receipt = vm.surface === 'development'
        ? vm.development.secretReceipt
        : vm.secretReceipt;
      if (!receipt || !$window.navigator.clipboard) return;
      return $window.navigator.clipboard.writeText(receipt.secret);
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
      if (vm.view === 'editor' || vm.view === 'detail'
          || vm.view === 'development-editor'
          || vm.view === 'development-detail') {
        vm.view = 'list';
        return;
      }
      $state.go('app.profile');
    };

    function loadDevelopment() {
      return $q.all([
        zumboApi.developmentConnections(),
        zumboApi.projects().catch(function() { return []; })
      ]).then(function(results) {
        vm.development.connections = results[0] || [];
        vm.development.projects = results[1] || [];
        var selectedId = vm.development.selected && vm.development.selected.id;
        var selected = vm.development.connections.find(function(item) {
          return item.id === selectedId;
        }) || vm.development.connections[0] || null;
        if (selected && vm.view === 'development-detail') {
          vm.development.selected = selected;
          return loadDevelopmentMappings();
        }
        vm.development.selected = null;
        vm.development.mappings = [];
        return vm.development.connections;
      });
    }

    function loadDevelopmentMappings() {
      if (!vm.development.selected) return $q.when([]);
      return zumboApi.developmentMappings(vm.development.selected.id)
        .then(function(mappings) {
          vm.development.mappings = mappings || [];
          return vm.development.mappings;
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Repository eşlemeleri yüklenemedi.');
          return [];
        });
    }

    function replaceSubscription(subscription) {
      var found = false;
      vm.subscriptions = vm.subscriptions.map(function(item) {
        if (item.id !== subscription.id) return item;
        found = true;
        return subscription;
      });
      if (!found) vm.subscriptions.unshift(subscription);
    }

    function replaceDevelopmentConnection(connection) {
      var found = false;
      vm.development.connections = vm.development.connections.map(function(item) {
        if (item.id !== connection.id) return item;
        found = true;
        return connection;
      });
      if (!found) vm.development.connections.unshift(connection);
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
      vm.development.secretReceipt = null;
      vm.development.credentialDraft = '';
      if (refreshTimer) {
        $timeout.cancel(refreshTimer);
        refreshTimer = null;
      }
    }

    function handleDevelopmentError(error) {
      vm.error = mobileActionError(
        error,
        'Geliştirme entegrasyonu işlemi tamamlanamadı.'
      );
      var code = error && error.data && error.data.error
        && error.data.error.code;
      if (error && (error.status === 409 || /_CONFLICT$/.test(String(code || '')))) {
        return loadDevelopment();
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
