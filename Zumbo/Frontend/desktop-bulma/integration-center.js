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
          vm.developmentCore = $window.ZumboDevelopmentIntegrationCore;
          vm.developmentProviders = vm.developmentCore.providers;
          vm.integrationSurface = 'webhooks';
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
          vm.developmentCenter = {
            loading: false,
            saving: false,
            action: '',
            error: '',
            connections: [],
            selected: null,
            mappings: [],
            repositories: [],
            repositoryStatus: '',
            editorOpen: false,
            credentialOpen: false,
            draft: vm.developmentCore.emptyConnectionDraft(),
            credentialDraft: '',
            mappingDraft: { projectId: '', repositoryId: '' },
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
            if (tab !== 'integrations') {
              clearSensitiveState();
              clearDevelopmentSensitiveState();
            }
            if (tab === 'integrations' && !vm.canManageIntegrations()) return;
            vm.settingsTab = tab;
            if (tab === 'integrations') return vm.loadIntegrationCenter(true);
          };

          vm.openIntegrationSurface = function(surface) {
            if (surface !== 'development' && surface !== 'webhooks') return;
            vm.integrationSurface = surface;
            if (surface === 'development') {
              clearSensitiveState();
              return vm.loadDevelopmentCenter(false);
            }
            clearDevelopmentSensitiveState();
            return vm.loadIntegrationCenter(false);
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

          vm.loadDevelopmentCenter = function(resetSelection) {
            if (!vm.canManageIntegrations()) return $q.when([]);
            vm.developmentCenter.loading = true;
            vm.developmentCenter.error = '';
            return $q.all([
              apiClient.get('/api/integrations/development', {
                scope: 'desktop-development-integrations',
                replace: true
              }),
              apiClient.get('/api/projects?organizationId=' + encodeURIComponent(
                vm.session.currentUser.organizationId
              ), {
                scope: 'desktop-development-projects',
                replace: true
              }).catch(function() { return vm.projects || []; })
            ]).then(function(results) {
              vm.developmentCenter.connections = results[0] || [];
              vm.projects = results[1] || vm.projects || [];
              var selectedId = !resetSelection && vm.developmentCenter.selected
                ? vm.developmentCenter.selected.id
                : null;
              var selected = vm.developmentCenter.connections.find(function(item) {
                return item.id === selectedId;
              }) || vm.developmentCenter.connections[0] || null;
              if (selected) return vm.selectDevelopmentConnection(selected);
              vm.developmentCenter.selected = null;
              vm.developmentCenter.mappings = [];
              vm.developmentCenter.repositories = [];
              return [];
            }).catch(function(error) {
              vm.developmentCenter.error = apiActionError(
                error,
                'Geliştirme entegrasyonları yüklenemedi.'
              );
              return [];
            }).finally(function() {
              vm.developmentCenter.loading = false;
            });
          };

          vm.selectDevelopmentConnection = function(connection) {
            clearDevelopmentSensitiveState();
            vm.developmentCenter.selected = connection || null;
            vm.developmentCenter.editorOpen = false;
            vm.developmentCenter.credentialOpen = false;
            vm.developmentCenter.repositories = [];
            vm.developmentCenter.repositoryStatus = '';
            vm.developmentCenter.error = '';
            return loadDevelopmentMappings();
          };

          vm.newDevelopmentConnection = function() {
            clearDevelopmentSensitiveState();
            vm.developmentCenter.selected = null;
            vm.developmentCenter.draft = vm.developmentCore.emptyConnectionDraft();
            vm.developmentCenter.editorOpen = true;
            vm.developmentCenter.credentialOpen = false;
            vm.developmentCenter.error = '';
          };

          vm.selectDevelopmentProvider = function(provider) {
            vm.developmentCore.selectProvider(vm.developmentCenter.draft, provider);
          };

          vm.closeDevelopmentEditor = function() {
            vm.developmentCenter.editorOpen = false;
            vm.developmentCenter.draft = vm.developmentCore.emptyConnectionDraft();
            vm.developmentCenter.selected = vm.developmentCenter.connections[0] || null;
            if (vm.developmentCenter.selected) return loadDevelopmentMappings();
          };

          vm.saveDevelopmentConnection = function() {
            if (vm.developmentCenter.saving || vm.pwa.offline) return;
            var request;
            try {
              request = vm.developmentCore.validateConnectionDraft(
                vm.developmentCenter.draft
              );
            } catch (error) {
              vm.developmentCenter.error = error.message;
              return;
            }
            vm.developmentCenter.saving = true;
            vm.developmentCenter.error = '';
            return apiClient.post('/api/integrations/development', request)
              .then(function(receipt) {
                replaceDevelopmentConnection(receipt.connection);
                vm.developmentCenter.selected = receipt.connection;
                vm.developmentCenter.editorOpen = false;
                vm.developmentCenter.secretReceipt = {
                  secret: receipt.webhookSecret,
                  fingerprint: receipt.connection.webhookSecretFingerprint,
                  version: receipt.connection.webhookSecretVersion
                };
                vm.notify('success', 'Sağlayıcı bağlantısı oluşturuldu.');
                return loadDevelopmentMappings();
              }).catch(handleDevelopmentMutationError).finally(function() {
                vm.developmentCenter.saving = false;
                vm.developmentCenter.draft.accessToken = '';
              });
          };

          vm.openDevelopmentCredential = function() {
            vm.developmentCenter.credentialDraft = '';
            vm.developmentCenter.credentialOpen = true;
            vm.developmentCenter.error = '';
          };

          vm.closeDevelopmentCredential = function() {
            vm.developmentCenter.credentialOpen = false;
            vm.developmentCenter.credentialDraft = '';
          };

          vm.rotateDevelopmentCredential = function() {
            var selected = vm.developmentCenter.selected;
            var credential = String(vm.developmentCenter.credentialDraft || '').trim();
            if (!selected || vm.developmentCenter.action || vm.pwa.offline) return;
            if (credential.length < 16 || credential.length > 512 || /\s/.test(credential)) {
              vm.developmentCenter.error =
                'Erişim anahtarı 16 ile 512 arasında boşluksuz karakter içermelidir.';
              return;
            }
            vm.developmentCenter.action = 'credential';
            return apiClient.post(
              '/api/integrations/development/' + selected.id + '/rotate-credential',
              { accessToken: credential, expectedVersion: selected.version }
            ).then(function(connection) {
              replaceDevelopmentConnection(connection);
              vm.developmentCenter.selected = connection;
              vm.closeDevelopmentCredential();
              vm.notify('success', 'Sağlayıcı erişim anahtarı döndürüldü.');
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
              vm.developmentCenter.credentialDraft = '';
            });
          };

          vm.rotateDevelopmentWebhookSecret = function() {
            var selected = vm.developmentCenter.selected;
            if (!selected || vm.developmentCenter.action || vm.pwa.offline) return;
            if (!$window.confirm(
              'Mevcut imza sırrı 15 dakika sonra geçersiz olacak. Yeni sır oluşturulsun mu?'
            )) return;
            vm.developmentCenter.action = 'secret';
            clearDevelopmentSensitiveState();
            return apiClient.post(
              '/api/integrations/development/' + selected.id + '/rotate-webhook-secret',
              { expectedVersion: selected.version }
            ).then(function(receipt) {
              replaceDevelopmentConnection(receipt.connection);
              vm.developmentCenter.selected = receipt.connection;
              vm.developmentCenter.secretReceipt = {
                secret: receipt.webhookSecret,
                fingerprint: receipt.connection.webhookSecretFingerprint,
                version: receipt.connection.webhookSecretVersion
              };
              vm.notify('success', 'Webhook imza sırrı döndürüldü.');
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
            });
          };

          vm.checkDevelopmentHealth = function() {
            var selected = vm.developmentCenter.selected;
            if (!selected || !selected.isConnected || vm.developmentCenter.action) return;
            vm.developmentCenter.action = 'health';
            vm.developmentCenter.error = '';
            return apiClient.post(
              '/api/integrations/development/' + selected.id + '/health',
              {}
            ).then(function(health) {
              vm.notify(
                health.status === 'Healthy' ? 'success' : 'warning',
                health.status === 'Healthy'
                  ? 'Sağlayıcı bağlantısı sağlıklı.'
                  : 'Sağlayıcı bağlantısı müdahale gerektiriyor.'
              );
              return apiClient.get(
                '/api/integrations/development/' + selected.id
              );
            }).then(function(connection) {
              replaceDevelopmentConnection(connection);
              vm.developmentCenter.selected = connection;
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
            });
          };

          vm.discoverDevelopmentRepositories = function() {
            var selected = vm.developmentCenter.selected;
            if (!selected || !selected.isConnected || vm.developmentCenter.action) return;
            vm.developmentCenter.action = 'repositories';
            vm.developmentCenter.error = '';
            return apiClient.get(
              '/api/integrations/development/' + selected.id + '/repositories',
              { scope: 'desktop-development-repositories', replace: true }
            ).then(function(page) {
              vm.developmentCenter.repositories = page.items || [];
              vm.developmentCenter.repositoryStatus = page.sourceStatus || 'Complete';
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
            });
          };

          vm.createDevelopmentMapping = function() {
            var selected = vm.developmentCenter.selected;
            if (!selected || vm.developmentCenter.action || vm.pwa.offline) return;
            var repository = vm.developmentCenter.repositories.find(function(item) {
              return item.externalRepositoryId
                === vm.developmentCenter.mappingDraft.repositoryId;
            });
            var request;
            try {
              request = vm.developmentCore.mappingRequest(
                vm.developmentCenter.mappingDraft.projectId,
                repository
              );
            } catch (error) {
              vm.developmentCenter.error = error.message;
              return;
            }
            vm.developmentCenter.action = 'mapping';
            return apiClient.post(
              '/api/integrations/development/' + selected.id + '/mappings',
              request
            ).then(function(mapping) {
              vm.developmentCenter.mappings.push(mapping);
              vm.developmentCenter.mappingDraft = { projectId: '', repositoryId: '' };
              vm.notify('success', 'Repository projeye bağlandı.');
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
            });
          };

          vm.deleteDevelopmentMapping = function(mapping) {
            if (!mapping || vm.developmentCenter.action || vm.pwa.offline) return;
            if (!$window.confirm(
              mapping.repositoryFullName
                + ' eşlemesi ve ona bağlı geliştirme bağlantıları kaldırılsın mı?'
            )) return;
            vm.developmentCenter.action = mapping.id;
            return apiClient.delete(
              '/api/integrations/development/mappings/' + mapping.id
                + '?expectedVersion=' + mapping.version
            ).then(function() {
              vm.developmentCenter.mappings =
                vm.developmentCenter.mappings.filter(function(item) {
                  return item.id !== mapping.id;
                });
              vm.notify('success', 'Repository eşlemesi kaldırıldı.');
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
            });
          };

          vm.disconnectDevelopmentConnection = function() {
            var selected = vm.developmentCenter.selected;
            if (!selected || !selected.isConnected || vm.developmentCenter.action
                || vm.pwa.offline) return;
            if (!$window.confirm(
              'Erişim anahtarı ve webhook sırları kalıcı olarak silinsin mi?'
            )) return;
            vm.developmentCenter.action = 'disconnect';
            clearDevelopmentSensitiveState();
            return apiClient.post(
              '/api/integrations/development/' + selected.id + '/disconnect',
              { expectedVersion: selected.version }
            ).then(function(connection) {
              replaceDevelopmentConnection(connection);
              vm.developmentCenter.selected = connection;
              vm.developmentCenter.repositories = [];
              return loadDevelopmentMappings().then(function() {
                vm.notify('success', 'Sağlayıcı bağlantısı güvenli biçimde kesildi.');
              });
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
            });
          };

          vm.deleteDevelopmentConnection = function() {
            var selected = vm.developmentCenter.selected;
            if (!selected || vm.developmentCenter.action || vm.pwa.offline) return;
            if (!$window.confirm(
              selected.name + ' ve tüm eşleme/bağlantı kayıtları kalıcı olarak silinsin mi?'
            )) return;
            vm.developmentCenter.action = 'delete';
            clearDevelopmentSensitiveState();
            return apiClient.delete(
              '/api/integrations/development/' + selected.id
                + '?expectedVersion=' + selected.version
            ).then(function() {
              vm.developmentCenter.connections =
                vm.developmentCenter.connections.filter(function(item) {
                  return item.id !== selected.id;
                });
              vm.developmentCenter.selected =
                vm.developmentCenter.connections[0] || null;
              vm.developmentCenter.mappings = [];
              if (vm.developmentCenter.selected) return loadDevelopmentMappings();
            }).then(function() {
              vm.notify('success', 'Sağlayıcı bağlantısı silindi.');
            }).catch(handleDevelopmentMutationError).finally(function() {
              vm.developmentCenter.action = '';
            });
          };

          vm.dismissDevelopmentSecret = clearDevelopmentSensitiveState;
          vm.developmentHealthState = vm.developmentCore.healthState;
          vm.developmentSafeHealthError = vm.developmentCore.safeHealthError;
          vm.developmentSafeUrl = vm.developmentCore.safeUrlLabel;
          vm.developmentShortFingerprint = vm.developmentCore.shortFingerprint;

          function replaceSubscription(subscription) {
            var found = false;
            vm.integrationCenter.subscriptions = vm.integrationCenter.subscriptions.map(function(item) {
              if (item.id !== subscription.id) return item;
              found = true;
              return subscription;
            });
            if (!found) vm.integrationCenter.subscriptions.unshift(subscription);
          }

          function replaceDevelopmentConnection(connection) {
            var found = false;
            vm.developmentCenter.connections =
              vm.developmentCenter.connections.map(function(item) {
                if (item.id !== connection.id) return item;
                found = true;
                return connection;
              });
            if (!found) vm.developmentCenter.connections.unshift(connection);
          }

          function loadDevelopmentMappings() {
            var selected = vm.developmentCenter.selected;
            if (!selected) return $q.when([]);
            return apiClient.get(
              '/api/integrations/development/' + selected.id + '/mappings',
              { scope: 'desktop-development-mappings', replace: true }
            ).then(function(mappings) {
              vm.developmentCenter.mappings = mappings || [];
              return vm.developmentCenter.mappings;
            }).catch(function(error) {
              vm.developmentCenter.error = apiActionError(
                error,
                'Repository eşlemeleri yüklenemedi.'
              );
              return [];
            });
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

          function clearDevelopmentSensitiveState() {
            vm.developmentCenter.secretReceipt = null;
            vm.developmentCenter.credentialDraft = '';
            vm.developmentCenter.credentialOpen = false;
          }

          function handleMutationError(error) {
            vm.integrationCenter.error = apiActionError(error, 'Webhook işlemi tamamlanamadı.');
            if (error && (error.status === 409 || error.code === 'WEBHOOK_SUBSCRIPTION_CONFLICT')) {
              return vm.loadIntegrationCenter(false);
            }
          }

          function handleDevelopmentMutationError(error) {
            vm.developmentCenter.error = apiActionError(
              error,
              'Geliştirme entegrasyonu işlemi tamamlanamadı.'
            );
            var code = error && error.data && error.data.error
              && error.data.error.code;
            if (error && (error.status === 409
                || /_CONFLICT$/.test(String(code || '')))) {
              return vm.loadDevelopmentCenter(false);
            }
          }
        }
      };
    });
})();
