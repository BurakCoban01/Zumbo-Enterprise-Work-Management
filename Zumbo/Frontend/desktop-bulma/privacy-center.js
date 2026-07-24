(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopPrivacyFeature', function($q, $timeout, $window, apiClient) {
      var core = $window.ZumboAuditPrivacyCore;
      return {
        install: function(vm, apiActionError) {
          var pollToken = 0;
          var completionNotified = false;
          vm.privacyWorkflow = null;
          vm.privacyWorkflowError = '';
          vm.privacyWorkflowBusy = '';

          vm.loadPrivacyWorkflowStatus = function() {
            var receipt = core.loadPrivacyReceipt($window.sessionStorage, vm.session.currentUser);
            if (!receipt) {
              vm.privacyWorkflow = null;
              return $q.when(null);
            }
            return loadPublicStatus(receipt, ++pollToken);
          };

          vm.exportPrivacyData = function() {
            if (vm.privacyWorkflowBusy) return $q.when(null);
            vm.privacyWorkflowBusy = 'export';
            vm.privacyWorkflowError = '';
            return apiClient.download('/api/auth/privacy/export.ndjson').then(function(blob) {
              download(blob, 'zumbo-privacy-export.ndjson');
              vm.notify('success', 'Gizlilik dışa aktarımı NDJSON olarak indirildi.');
            }).catch(function(error) {
              vm.privacyWorkflowError = apiActionError(error, 'Gizlilik verileri aktarılamadı.');
            }).finally(function() { vm.privacyWorkflowBusy = ''; });
          };

          vm.anonymizeAccount = function() {
            if (vm.privacyWorkflowBusy) return $q.when(null);
            var request;
            try {
              request = core.validateAnonymization(vm.privacyDraft);
            } catch (error) {
              vm.privacyWorkflowError = error.message;
              return $q.when(null);
            }
            if (!$window.confirm(
              'Hesap erişimi kalıcı olarak kapanacak ve kişisel referanslar anonimleştirilecek. Devam edilsin mi?'
            )) return $q.when(null);
            vm.privacyWorkflowBusy = 'submit';
            vm.privacyWorkflowError = '';
            completionNotified = false;
            return apiClient.post('/api/auth/privacy/anonymization-jobs', request)
              .then(function(receipt) {
                core.savePrivacyReceipt($window.sessionStorage, vm.session.currentUser, receipt);
                vm.privacyWorkflow = receipt.job;
                vm.privacyDraft = { password: '', confirmation: '' };
                vm.notify('success', 'Anonimleştirme işi sıraya alındı; ilerleme bu cihazda izleniyor.');
                var storedReceipt = core.loadPrivacyReceipt(
                  $window.sessionStorage,
                  vm.session.currentUser
                );
                if (storedReceipt) schedule(storedReceipt, ++pollToken);
                return receipt.job;
              }).catch(function(error) {
                vm.privacyWorkflowError = apiActionError(error, 'Anonimleştirme işi başlatılamadı.');
                return null;
              }).finally(function() { vm.privacyWorkflowBusy = ''; });
          };

          vm.retryPrivacyWorkflow = function() {
            if (!vm.privacyWorkflow || !core.canRetryPrivacy(vm.privacyWorkflow)
                || vm.privacyWorkflowBusy) return $q.when(null);
            return mutatePrivacyWorkflow('retry', 'Anonimleştirme işi yeniden sıraya alındı.');
          };

          vm.reconcilePrivacyWorkflow = function() {
            if (!vm.privacyWorkflow || !core.canReconcilePrivacy(vm.privacyWorkflow)
                || vm.privacyWorkflowBusy) return $q.when(null);
            if (!$window.confirm(
              'Sunucu mevcut checkpoint durumunu yeniden değerlendirecek. Uzlaştırma başlatılsın mı?'
            )) return $q.when(null);
            return mutatePrivacyWorkflow('reconcile', 'Anonimleştirme işi uzlaştırma için sıraya alındı.');
          };

          vm.dismissPrivacyWorkflow = function() {
            pollToken++;
            core.clearPrivacyReceipt($window.sessionStorage, vm.session.currentUser);
            vm.privacyWorkflow = null;
            vm.privacyWorkflowError = '';
          };

          vm.privacyStateLabel = core.privacyStateLabel;
          vm.privacyProgress = core.privacyProgress;
          vm.privacyCanRetry = core.canRetryPrivacy;
          vm.privacyCanReconcile = core.canReconcilePrivacy;
          vm.privacyTerminal = core.isPrivacyTerminal;

          function mutatePrivacyWorkflow(action, message) {
            vm.privacyWorkflowBusy = action;
            vm.privacyWorkflowError = '';
            return apiClient.post(
              '/api/auth/privacy/jobs/' + encodeURIComponent(vm.privacyWorkflow.id) + '/' + action,
              {}
            ).then(function(job) {
              vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, job);
              vm.notify('success', message);
              var receipt = core.loadPrivacyReceipt($window.sessionStorage, vm.session.currentUser);
              if (receipt) schedule(receipt, ++pollToken);
              return job;
            }).catch(function(error) {
              vm.privacyWorkflowError = apiActionError(error, 'Gizlilik işi güncellenemedi.');
              return null;
            }).finally(function() { vm.privacyWorkflowBusy = ''; });
          }

          function loadPublicStatus(receipt, token) {
            return apiClient.get(
              '/api/auth/privacy/jobs/' + encodeURIComponent(receipt.id) + '/status',
              {
                refresh: false,
                privacyStatusToken: receipt.statusToken,
                scope: 'desktop-privacy-status',
                replace: true
              }
            ).then(function(status) {
              if (token !== pollToken) return null;
              vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, status);
              vm.privacyWorkflowError = '';
              if (status.state === 'Failed') loadOwnedFailure(receipt.id);
              if (status.state === 'Completed' && !completionNotified) {
                completionNotified = true;
                vm.notify('success', 'Hesap anonimleştirme işi tamamlandı.');
              }
              schedule(receipt, token);
              return status;
            }).catch(function(error) {
              if (token !== pollToken) return null;
              if (error.status === 404) {
                vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, {
                  id: receipt.id,
                  state: 'Expired',
                  progressPercent: 100
                });
                return vm.privacyWorkflow;
              }
              vm.privacyWorkflowError = apiActionError(error, 'Gizlilik işinin durumu alınamadı.');
              return null;
            });
          }

          function loadOwnedFailure(jobId) {
            return apiClient.get('/api/auth/privacy/jobs/' + encodeURIComponent(jobId))
              .then(function(job) {
                vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, job);
              }).catch(angular.noop);
          }

          function schedule(receipt, token) {
            if (!vm.privacyWorkflow || core.isPrivacyTerminal(vm.privacyWorkflow)
                || vm.privacyWorkflow.state === 'Failed') return;
            $timeout(function() {
              if (token !== pollToken || vm.activeSection !== 'settings'
                  || vm.settingsTab !== 'account') return;
              loadPublicStatus(receipt, token);
            }, 1500);
          }

          function download(blob, fileName) {
            var url = $window.URL.createObjectURL(blob);
            var link = $window.document.createElement('a');
            link.href = url;
            link.download = fileName;
            link.click();
            $window.URL.revokeObjectURL(url);
          }
        }
      };
    });
})();
