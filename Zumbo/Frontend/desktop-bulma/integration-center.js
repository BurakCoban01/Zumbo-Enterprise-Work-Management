(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopIntegrationFeature', function($q, $timeout, $window, apiClient) {
      var core = $window.ZumboWebhookCore;

      return {
        install: function(vm, apiActionError) {
          var capabilityRequest;
          var deliveryRefreshTimer;
          vm.integrationRoles = [];
          vm.webhookScopes = core.scopes;
          vm.integrationCenter = {
            permissionResolved: false,
            loading: false,
            saving: false,
            action: '',
            error: '',
            subscriptions: [],
            selected: null,
            metrics: null,
            deliveries: [],
            nextCursor: null,
            editorOpen: false,
            editorMode: 'create',
            draft: core.emptyDraft(),
            secretReceipt: null
          };

          vm.canManageIntegrations = function() {
            return core.hasPermission(vm.session.currentUser, vm.integrationRoles);
          };

          vm.loadIntegrationCapabilities = function() {
            if (!vm.session.currentUser) return $q.when([]);
            if (capabilityRequest) return capabilityRequest;
            capabilityRequest = apiClient.get('/api/auth/roles').then(function(roles) {
              vm.integrationRoles = roles || [];
              return vm.integrationRoles;
            }).catch(function() {
              vm.integrationRoles = [];
              return [];
            }).finally(function() {
              capabilityRequest = null;
              vm.integrationCenter.permissionResolved = true;
              if (!vm.canManageIntegrations() && vm.settingsTab === 'integrations') {
                vm.openSettingsTab('account');
              }
            });
            return capabilityRequest;
          };

          vm.openSettingsTab = function(tab) {
            if (tab !== 'integrations') clearSensitiveState();
            if (tab === 'integrations' && !vm.canManageIntegrations()) return;
            vm.settingsTab = tab;
            if (tab === 'integrations') return vm.loadIntegrationCenter(true);
          };

          vm.loadIntegrationCenter = function(resetSelection) {
            return vm.loadIntegrationCapabilities().then(function() {
              if (!vm.canManageIntegrations()) return [];
              vm.integrationCenter.loading = true;
              vm.integrationCenter.error = '';
              return $q.all([
                apiClient.get('/api/integrations/webhooks', {
                  scope: 'desktop-integrations',
                  replace: true
                }),
                apiClient.get('/api/integrations/webhooks/metrics', {
                  scope: 'desktop-integration-metrics',
                  replace: true
                })
              ]).then(function(results) {
                vm.integrationCenter.subscriptions = results[0] || [];
                vm.integrationCenter.metrics = results[1] || null;
                var selectedId = !resetSelection && vm.integrationCenter.selected
                  ? vm.integrationCenter.selected.id
                  : null;
                var selected = vm.integrationCenter.subscriptions.find(function(item) {
                  return item.id === selectedId;
                }) || vm.integrationCenter.subscriptions[0] || null;
                if (selected) return vm.selectWebhookSubscription(selected);
                vm.integrationCenter.selected = null;
                vm.integrationCenter.deliveries = [];
                vm.integrationCenter.nextCursor = null;
                return [];
              }).catch(function(error) {
                vm.integrationCenter.error = apiActionError(
                  error,
                  'Entegrasyon merkezi yüklenemedi.'
                );
                return [];
              }).finally(function() {
                vm.integrationCenter.loading = false;
              });
            });
          };

          vm.selectWebhookSubscription = function(subscription) {
            clearSensitiveState();
            vm.integrationCenter.selected = subscription || null;
            vm.integrationCenter.editorOpen = false;
            vm.integrationCenter.error = '';
            return vm.loadWebhookDeliveries(true);
          };

          vm.loadWebhookDeliveries = function(reset) {
            var selected = vm.integrationCenter.selected;
            if (!selected) return $q.when([]);
            if (reset) {
              vm.integrationCenter.deliveries = [];
              vm.integrationCenter.nextCursor = null;
            }
            var cursor = !reset && vm.integrationCenter.nextCursor
              ? '&cursor=' + encodeURIComponent(vm.integrationCenter.nextCursor)
              : '';
            return apiClient.get('/api/integrations/webhooks/' + selected.id
              + '/deliveries?pageSize=30' + cursor, {
              scope: 'desktop-webhook-deliveries',
              replace: !!reset
            }).then(function(page) {
              var incoming = page.items || [];
              vm.integrationCenter.deliveries = reset
                ? incoming
                : vm.integrationCenter.deliveries.concat(incoming);
              vm.integrationCenter.nextCursor = page.nextCursor || null;
              return vm.integrationCenter.deliveries;
            }).catch(function(error) {
              vm.integrationCenter.error = apiActionError(error, 'Teslimat kayıtları yüklenemedi.');
              return [];
            });
          };

          vm.loadMoreWebhookDeliveries = function() {
            if (!vm.integrationCenter.nextCursor || vm.integrationCenter.action) return;
            return vm.loadWebhookDeliveries(false);
          };

          vm.newWebhookSubscription = function() {
            clearSensitiveState();
            vm.integrationCenter.editorMode = 'create';
            vm.integrationCenter.draft = core.emptyDraft();
            vm.integrationCenter.editorOpen = true;
            vm.integrationCenter.error = '';
          };

          vm.editWebhookSubscription = function() {
            if (!vm.integrationCenter.selected) return;
            clearSensitiveState();
            vm.integrationCenter.editorMode = 'edit';
            vm.integrationCenter.draft = core.draftFrom(vm.integrationCenter.selected);
            vm.integrationCenter.editorOpen = true;
            vm.integrationCenter.error = '';
          };

          vm.closeWebhookEditor = function() {
            vm.integrationCenter.editorOpen = false;
            vm.integrationCenter.draft = core.emptyDraft();
          };

          vm.toggleWebhookScope = function(scope) {
            core.toggleScope(vm.integrationCenter.draft, scope);
          };

          vm.webhookScopeSelected = function(scope) {
            return vm.integrationCenter.draft.eventScopes.indexOf(scope) >= 0;
          };

          vm.saveWebhookSubscription = function() {
            if (vm.integrationCenter.saving || vm.pwa.offline) return;
            var request;
            try {
              request = core.validateDraft(vm.integrationCenter.draft);
            } catch (error) {
              vm.integrationCenter.error = error.message;
              return;
            }
            vm.integrationCenter.saving = true;
            vm.integrationCenter.error = '';
            var operation = vm.integrationCenter.editorMode === 'edit'
              ? apiClient.put('/api/integrations/webhooks/'
                + vm.integrationCenter.selected.id, request)
              : apiClient.post('/api/integrations/webhooks', request);
            return operation.then(function(result) {
              var subscription = result.subscription || result;
              if (result.secret) {
                vm.integrationCenter.secretReceipt = {
                  secret: result.secret,
                  fingerprint: subscription.secretFingerprint,
                  version: subscription.secretVersion
                };
              }
              replaceSubscription(subscription);
              vm.integrationCenter.selected = subscription;
              vm.integrationCenter.editorOpen = false;
              vm.notify('success', result.secret
                ? 'Webhook oluşturuldu. Sırrı şimdi güvenli biçimde kaydedin.'
                : 'Webhook güncellendi.');
              return refreshMetricsAndDeliveries();
            }).catch(handleMutationError).finally(function() {
              vm.integrationCenter.saving = false;
            });
          };

          vm.rotateWebhookSecret = function() {
            var selected = vm.integrationCenter.selected;
            if (!selected || vm.integrationCenter.action || vm.pwa.offline) return;
            if (!$window.confirm('Mevcut sır kısa geçiş süresinden sonra geçersiz olacak. Yeni sır oluşturulsun mu?')) return;
            vm.integrationCenter.action = 'rotate';
            clearSensitiveState();
            return apiClient.post('/api/integrations/webhooks/' + selected.id + '/rotate-secret', {
              expectedVersion: selected.version
            }).then(function(receipt) {
              replaceSubscription(receipt.subscription);
              vm.integrationCenter.selected = receipt.subscription;
              vm.integrationCenter.secretReceipt = {
                secret: receipt.secret,
                fingerprint: receipt.subscription.secretFingerprint,
                version: receipt.subscription.secretVersion
              };
              vm.notify('success', 'Yeni webhook sırrı oluşturuldu.');
            }).catch(handleMutationError).finally(function() {
              vm.integrationCenter.action = '';
            });
          };

          vm.setWebhookActive = function(active) {
            var selected = vm.integrationCenter.selected;
            if (!selected || vm.integrationCenter.action || vm.pwa.offline) return;
            if (!active && !$window.confirm('Bu webhook için yeni teslimatlar durdurulsun mu?')) return;
            vm.integrationCenter.action = active ? 'enable' : 'disable';
            clearSensitiveState();
            var request = { expectedVersion: selected.version };
            var operation = active
              ? apiClient.post('/api/integrations/webhooks/' + selected.id + '/enable', request)
              : apiClient.post('/api/integrations/webhooks/' + selected.id + '/disable', request);
            return operation.then(function(subscription) {
              replaceSubscription(subscription);
              vm.integrationCenter.selected = subscription;
              vm.notify('success', active ? 'Webhook etkinleştirildi.' : 'Webhook durduruldu.');
            }).catch(handleMutationError).finally(function() {
              vm.integrationCenter.action = '';
            });
          };

          vm.sendWebhookTest = function() {
            var selected = vm.integrationCenter.selected;
            if (!selected || !selected.isActive || vm.integrationCenter.action || vm.pwa.offline) return;
            vm.integrationCenter.action = 'test';
            vm.integrationCenter.error = '';
            return apiClient.post('/api/integrations/webhooks/' + selected.id + '/test-delivery', {})
              .then(function(delivery) {
                vm.integrationCenter.deliveries.unshift(delivery);
                vm.notify('success', 'Güvenli test teslimatı sıraya alındı.');
                scheduleDeliveryRefresh();
                return refreshMetrics();
              }).catch(handleMutationError).finally(function() {
                vm.integrationCenter.action = '';
              });
          };

          vm.replayWebhookDelivery = function(delivery) {
            if (!core.canReplay(delivery) || vm.integrationCenter.action || vm.pwa.offline) return;
            if (!$window.confirm('Aynı değişmez yük yeniden teslimat sırasına alınsın mı?')) return;
            vm.integrationCenter.action = delivery.id;
            return apiClient.post('/api/integrations/webhooks/deliveries/'
              + delivery.id + '/replay', {}).then(function(replayed) {
              replaceDelivery(replayed);
              vm.notify('success', 'Teslimat yeniden sıraya alındı.');
              scheduleDeliveryRefresh();
              return refreshMetrics();
            }).catch(handleMutationError).finally(function() {
              vm.integrationCenter.action = '';
            });
          };

          vm.dismissWebhookSecret = clearSensitiveState;
          vm.webhookTargetLabel = core.safeTargetLabel;
          vm.webhookScopeLabel = core.scopeLabel;
          vm.webhookDeliveryState = core.deliveryState;
          vm.webhookSafeError = core.safeError;
          vm.webhookCanReplay = core.canReplay;
          vm.webhookShortHash = core.shortHash;

          function replaceSubscription(subscription) {
            var found = false;
            vm.integrationCenter.subscriptions = vm.integrationCenter.subscriptions.map(function(item) {
              if (item.id !== subscription.id) return item;
              found = true;
              return subscription;
            });
            if (!found) vm.integrationCenter.subscriptions.unshift(subscription);
          }

          function replaceDelivery(delivery) {
            vm.integrationCenter.deliveries = vm.integrationCenter.deliveries.map(function(item) {
              return item.id === delivery.id ? delivery : item;
            });
          }

          function refreshMetrics() {
            return apiClient.get('/api/integrations/webhooks/metrics').then(function(metrics) {
              vm.integrationCenter.metrics = metrics;
            });
          }

          function refreshMetricsAndDeliveries() {
            return $q.all([refreshMetrics(), vm.loadWebhookDeliveries(true)]);
          }

          function scheduleDeliveryRefresh() {
            if (deliveryRefreshTimer) $timeout.cancel(deliveryRefreshTimer);
            deliveryRefreshTimer = $timeout(function() {
              if (vm.settingsTab !== 'integrations') return;
              return refreshMetricsAndDeliveries();
            }, 1200);
          }

          function clearSensitiveState() {
            vm.integrationCenter.secretReceipt = null;
            if (deliveryRefreshTimer) {
              $timeout.cancel(deliveryRefreshTimer);
              deliveryRefreshTimer = null;
            }
          }

          function handleMutationError(error) {
            vm.integrationCenter.error = apiActionError(error, 'Webhook işlemi tamamlanamadı.');
            if (error && (error.status === 409 || error.code === 'WEBHOOK_SUBSCRIPTION_CONFLICT')) {
              return vm.loadIntegrationCenter(false);
            }
          }
        }
      };
    });
})();
