(function() {
  'use strict';

  angular.module('zumboMobile')
    .factory('mobilePrivacyFeature', function(
      $q, $timeout, $window, $ionicPopup, zumboApi, sessionStore, mobileActionError
    ) {
      var core = $window.ZumboAuditPrivacyCore;

      return {
        install: function(vm) {
          var pollGeneration = 0;
          vm.privacyDraft = { password: '', confirmation: '' };
          vm.privacyWorkflow = null;
          vm.privacyError = '';
          vm.privacyBusy = '';
          vm.privacyStateLabel = core.privacyStateLabel;
          vm.privacyProgress = core.privacyProgress;
          vm.privacyCanRetry = core.canRetryPrivacy;
          vm.privacyCanReconcile = core.canReconcilePrivacy;
          vm.privacyTerminal = core.isPrivacyTerminal;

          vm.loadPrivacyWorkflow = function() {
            var receipt = core.loadPrivacyReceipt($window.sessionStorage, currentUser());
            if (!receipt) {
              vm.privacyWorkflow = null;
              return $q.when(null);
            }
            return loadPublicStatus(receipt, ++pollGeneration);
          };

          vm.exportPrivacyData = function() {
            if (vm.privacyBusy) return $q.when(null);
            vm.privacyBusy = 'export';
            vm.privacyError = '';
            return zumboApi.exportPrivacyData().then(function(blob) {
              download(blob, 'zumbo-privacy-export.ndjson');
            }).catch(function(error) {
              vm.privacyError = mobileActionError(error, 'Gizlilik verileri aktarılamadı.');
            }).finally(function() { vm.privacyBusy = ''; });
          };

          vm.anonymizeAccount = function() {
            if (vm.privacyBusy) return $q.when(null);
            var request;
            try {
              request = core.validateAnonymization(vm.privacyDraft);
            } catch (error) {
              vm.privacyError = error.message;
              return $q.when(null);
            }
            return $ionicPopup.confirm({
              title: 'Hesabı anonimleştir',
              template: 'Hesap erişimi kalıcı olarak kapanacak ve kişisel referanslar anonimleştirilecek.',
              cancelText: 'Vazgeç',
              okText: 'Kalıcı olarak başlat'
            }).then(function(confirmed) {
              if (!confirmed) return null;
              vm.privacyBusy = 'submit';
              vm.privacyError = '';
              return zumboApi.createPrivacyJob(request).then(function(receipt) {
                core.savePrivacyReceipt($window.sessionStorage, currentUser(), receipt);
                vm.privacyWorkflow = receipt.job;
                vm.privacyDraft = { password: '', confirmation: '' };
                var storedReceipt = core.loadPrivacyReceipt($window.sessionStorage, currentUser());
                if (storedReceipt) schedule(storedReceipt, ++pollGeneration);
                return receipt.job;
              }).catch(function(error) {
                vm.privacyError = mobileActionError(error, 'Anonimleştirme işi başlatılamadı.');
                return null;
              }).finally(function() { vm.privacyBusy = ''; });
            });
          };

          vm.retryPrivacyWorkflow = function() {
            if (!core.canRetryPrivacy(vm.privacyWorkflow) || vm.privacyBusy) return $q.when(null);
            return mutate('retry');
          };

          vm.reconcilePrivacyWorkflow = function() {
            if (!core.canReconcilePrivacy(vm.privacyWorkflow) || vm.privacyBusy) return $q.when(null);
            return $ionicPopup.confirm({
              title: 'Durumu uzlaştır',
              template: 'Sunucu mevcut checkpoint durumunu yeniden değerlendirecek.',
              cancelText: 'Vazgeç',
              okText: 'Uzlaştır'
            }).then(function(confirmed) {
              return confirmed ? mutate('reconcile') : null;
            });
          };

          vm.dismissPrivacyWorkflow = function() {
            pollGeneration++;
            core.clearPrivacyReceipt($window.sessionStorage, currentUser());
            vm.privacyWorkflow = null;
            vm.privacyError = '';
          };

          vm.stopPrivacyPolling = function() {
            pollGeneration++;
          };

          function mutate(action) {
            vm.privacyBusy = action;
            vm.privacyError = '';
            var request = action === 'retry'
              ? zumboApi.retryPrivacyJob(vm.privacyWorkflow.id)
              : zumboApi.reconcilePrivacyJob(vm.privacyWorkflow.id);
            return request.then(function(job) {
              vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, job);
              var receipt = core.loadPrivacyReceipt($window.sessionStorage, currentUser());
              if (receipt) schedule(receipt, ++pollGeneration);
              return job;
            }).catch(function(error) {
              vm.privacyError = mobileActionError(error, 'Gizlilik işi güncellenemedi.');
              return null;
            }).finally(function() { vm.privacyBusy = ''; });
          }

          function loadPublicStatus(receipt, generation) {
            return zumboApi.privacyJobStatus(receipt.id, receipt.statusToken).then(function(status) {
              if (generation !== pollGeneration) return null;
              vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, status);
              vm.privacyError = '';
              if (status.state === 'Failed') loadOwnedFailure(receipt.id);
              schedule(receipt, generation);
              return status;
            }).catch(function(error) {
              if (generation !== pollGeneration) return null;
              if (error.status === 404) {
                vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, {
                  id: receipt.id,
                  state: 'Expired',
                  progressPercent: 100
                });
                return vm.privacyWorkflow;
              }
              vm.privacyError = mobileActionError(error, 'Gizlilik işinin durumu alınamadı.');
              return null;
            });
          }

          function loadOwnedFailure(jobId) {
            return zumboApi.privacyJob(jobId).then(function(job) {
              vm.privacyWorkflow = core.mergePrivacyStatus(vm.privacyWorkflow, job);
            }).catch(angular.noop);
          }

          function schedule(receipt, generation) {
            if (!vm.privacyWorkflow || core.isPrivacyTerminal(vm.privacyWorkflow)
                || vm.privacyWorkflow.state === 'Failed') return;
            $timeout(function() {
              if (generation !== pollGeneration) return;
              loadPublicStatus(receipt, generation);
            }, 1500);
          }

          function currentUser() {
            return sessionStore.state.currentUser;
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
