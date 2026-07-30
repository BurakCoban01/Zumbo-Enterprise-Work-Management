(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopBulkJobFeature', function($q, $timeout, $window, apiClient) {
      var core = window.ZumboBulkJobCore;

      return {
        install: function(vm, apiActionError) {
          var pollToken = 0;
          vm.bulkJobs = [];
          vm.bulkJobTotal = 0;
          vm.bulkJobSelected = null;
          vm.bulkJobLoading = false;
          vm.bulkJobBusy = false;
          vm.bulkJobError = null;
          vm.bulkJobNotice = null;
          vm.bulkImportFile = null;
          vm.bulkImportParsed = null;
          vm.bulkExportDraft = { includeArchived: false };

          vm.bulkJobState = core.state;
          vm.bulkJobTypeLabel = core.typeLabel;
          vm.bulkJobProgress = core.progress;
          vm.bulkJobCanCancel = core.canCancel;
          vm.bulkJobCanRetry = core.canRetry;
          vm.bulkJobArtifactsExpired = core.artifactsExpired;

          vm.loadBulkJobs = function(options) {
            options = options || {};
            if (!vm.project) return $q.when([]);
            var projectId = vm.project.id;
            var token = ++pollToken;
            if (!options.quiet) vm.bulkJobLoading = true;
            vm.bulkJobError = null;
            return apiClient.get(
              '/api/work-items/bulk/jobs?projectId=' + encodeURIComponent(projectId) + '&page=1&pageSize=50',
              { scope: 'desktop-bulk-jobs', replace: true }
            ).then(function(page) {
              if (!vm.project || vm.project.id !== projectId || token !== pollToken) return [];
              vm.bulkJobs = page.items || [];
              vm.bulkJobTotal = page.totalCount || vm.bulkJobs.length;
              if (vm.bulkJobSelected) {
                vm.bulkJobSelected = vm.bulkJobs.find(function(job) {
                  return job.id === vm.bulkJobSelected.id;
                }) || null;
              }
              schedulePoll(projectId, token);
              return vm.bulkJobs;
            }).catch(function(error) {
              if (!options.quiet) {
                vm.bulkJobError = apiActionError(error, 'İş merkezi yüklenemedi.');
              }
              return [];
            }).finally(function() {
              if (!options.quiet) vm.bulkJobLoading = false;
            });
          };

          vm.selectBulkJob = function(job) {
            vm.bulkJobSelected = job;
            vm.bulkJobError = null;
          };

          vm.chooseBulkImportFile = function(file) {
            vm.bulkImportFile = file || null;
            vm.bulkImportParsed = null;
            vm.bulkJobError = null;
            if (!file) return $q.when(null);
            if (!/\.json$/i.test(file.name || '')) {
              vm.bulkJobError = 'İçe aktarım dosyası .json uzantılı olmalı.';
              return $q.when(null);
            }
            var deferred = $q.defer();
            var reader = new $window.FileReader();
            reader.onload = function() {
              $timeout(function() {
                vm.bulkImportParsed = core.parseImport(reader.result, file.size);
                if (!vm.bulkImportParsed.valid) {
                  vm.bulkJobError = vm.bulkImportParsed.errors[0];
                }
                deferred.resolve(vm.bulkImportParsed);
              });
            };
            reader.onerror = function() {
              $timeout(function() {
                vm.bulkJobError = 'Dosya okunamadı.';
                deferred.resolve(null);
              });
            };
            reader.readAsText(file);
            return deferred.promise;
          };

          vm.submitBulkImport = function(dryRun) {
            if (!vm.project || !vm.bulkImportParsed || !vm.bulkImportParsed.valid || vm.bulkJobBusy) {
              return $q.when(null);
            }
            return mutate(
              apiClient.post(
                '/api/work-items/bulk/jobs/import',
                core.importRequest(vm.project.id, vm.bulkImportParsed, dryRun),
                { idempotencyKey: core.idempotencyKey(dryRun ? 'import-preview' : 'import') }
              ),
              dryRun ? 'İçe aktarım önizlemesi sıraya alındı.' : 'İçe aktarım sıraya alındı.'
            );
          };

          vm.submitBulkExport = function(dryRun) {
            if (!vm.project || vm.bulkJobBusy) return $q.when(null);
            return mutate(
              apiClient.post('/api/work-items/bulk/jobs/export', {
                projectId: vm.project.id,
                dryRun: dryRun === true,
                includeArchived: vm.bulkExportDraft.includeArchived === true
              }, { idempotencyKey: core.idempotencyKey(dryRun ? 'export-preview' : 'export') }),
              dryRun ? 'Dışa aktarım önizlemesi sıraya alındı.' : 'Dışa aktarım sıraya alındı.'
            );
          };

          vm.cancelBulkJob = function(job) {
            if (!core.canCancel(job) || vm.bulkJobBusy) return $q.when(null);
            if (!$window.confirm('Çalışan işi iptal etmek istiyor musunuz?')) return $q.when(null);
            return mutate(
              apiClient.post('/api/work-items/bulk/jobs/' + job.id + '/cancel', {}),
              'İptal isteği kaydedildi.'
            );
          };

          vm.retryBulkJob = function(job) {
            if (!core.canRetry(job) || vm.bulkJobBusy) return $q.when(null);
            return mutate(
              apiClient.post('/api/work-items/bulk/jobs/' + job.id + '/retry', {}),
              'Başarısız satırlar yeniden sıraya alındı.'
            );
          };

          vm.downloadBulkJobArtifact = function(job, errors) {
            if (!job || core.artifactsExpired(job) || (errors ? !job.hasErrorFile : !job.hasResult)) {
              return $q.when(null);
            }
            vm.bulkJobError = null;
            return apiClient.download(
              '/api/work-items/bulk/jobs/' + job.id + '/' + (errors ? 'errors' : 'result')
            ).then(function(blob) {
              var url = $window.URL.createObjectURL(blob);
              var link = $window.document.createElement('a');
              link.href = url;
              link.download = 'zumbo-' + job.type.toLowerCase() + '-' + job.id.slice(0, 8)
                + (errors ? '-errors.ndjson' : '-result.ndjson');
              link.click();
              $window.URL.revokeObjectURL(url);
            }).catch(function(error) {
              vm.bulkJobError = apiActionError(error, 'İş dosyası indirilemedi.');
            });
          };

          vm.clearBulkJobNotice = function() { vm.bulkJobNotice = null; };

          function mutate(request, message) {
            vm.bulkJobBusy = true;
            vm.bulkJobError = null;
            vm.bulkJobNotice = null;
            return request.then(function(job) {
              vm.bulkJobNotice = message;
              vm.bulkJobSelected = job;
              return vm.loadBulkJobs().then(function() { return job; });
            }).catch(function(error) {
              vm.bulkJobError = apiActionError(error, 'İş merkezi işlemi tamamlanamadı.');
              return null;
            }).finally(function() { vm.bulkJobBusy = false; });
          }

          function schedulePoll(projectId, token) {
            var active = vm.bulkJobs.some(function(job) { return !core.isTerminal(job) && job.state !== 'Failed'; });
            if (!active || vm.workMode !== 'jobs') return;
            $timeout(function() {
              if (token !== pollToken || !vm.project || vm.project.id !== projectId || vm.workMode !== 'jobs') return;
              vm.loadBulkJobs({ quiet: true });
            }, 2500, false);
          }
        }
      };
    });
})();
