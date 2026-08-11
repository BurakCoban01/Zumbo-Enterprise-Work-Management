(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('BulkJobCenterController', function(
      $scope, $state, $stateParams, $q, $timeout, $window,
      zumboApi, sessionStore, apiClient, mobileActionError, mobilePwaService, authorizationCatalog
    ) {
      var vm = this;
      var core = $window.ZumboBulkJobCore;
      var projectId = $stateParams.projectId;
      var pollGeneration = 0;
      var destroyed = false;

      apiClient.transitionContext('project:' + projectId);
      vm.mode = $stateParams.mode === 'history' ? 'history' : 'launch';
      vm.project = sessionStore.state.project && sessionStore.state.project.id === projectId
        ? sessionStore.state.project
        : null;
      vm.pwa = mobilePwaService.state;
      vm.jobs = [];
      vm.total = 0;
      vm.selected = null;
      vm.loading = true;
      vm.busy = false;
      vm.error = null;
      vm.notice = null;
      vm.importFile = null;
      vm.importParsed = null;
      vm.exportDraft = { includeArchived: false };
      vm.cancelCandidateId = null;

      vm.jobState = core.state;
      vm.jobTypeLabel = core.typeLabel;
      vm.jobProgress = core.progress;
      vm.canCancel = core.canCancel;
      vm.canRetry = core.canRetry;
      vm.artifactsExpired = core.artifactsExpired;

      vm.canEdit = function() {
        var user = sessionStore.state.currentUser;
        var membership = vm.project && user && (vm.project.members || []).find(function(member) {
          return member.userId === user.id;
        });
        return !!membership
          && authorizationCatalog.hasProjectPermission(membership.role, 'WorkItemUpdate')
          && !vm.pwa.offline;
      };

      vm.setMode = function(mode) {
        if (['launch', 'history'].indexOf(mode) < 0) return;
        vm.mode = mode;
        vm.error = null;
        $state.go('project-jobs', { projectId: projectId, mode: mode }, {
          notify: false,
          location: 'replace'
        });
      };

      vm.load = function(options) {
        options = options || {};
        var generation = ++pollGeneration;
        if (!options.quiet) vm.loading = true;
        vm.error = null;
        var projectRequest = vm.project ? $q.when(vm.project) : zumboApi.project(projectId);
        return projectRequest.then(function(project) {
          vm.project = project;
          sessionStore.state.project = project;
          return zumboApi.bulkJobs(projectId, 1, 50);
        }).then(function(page) {
          if (destroyed || generation !== pollGeneration) return vm.jobs;
          vm.jobs = page.items || [];
          vm.total = page.totalCount || vm.jobs.length;
          if (vm.selected) {
            vm.selected = vm.jobs.find(function(job) { return job.id === vm.selected.id; }) || null;
          }
          schedulePoll(generation);
          return vm.jobs;
        }).catch(function(error) {
          if (!options.quiet) vm.error = mobileActionError(error, 'İş merkezi yüklenemedi.');
          return [];
        }).finally(function() {
          if (!options.quiet) vm.loading = false;
          $scope.$broadcast('scroll.refreshComplete');
        });
      };

      vm.select = function(job) {
        vm.selected = job;
        vm.cancelCandidateId = null;
        vm.error = null;
      };

      vm.chooseImportFile = function(file) {
        vm.importFile = file || null;
        vm.importParsed = null;
        vm.error = null;
        if (!file) return $q.when(null);
        if (!/\.json$/i.test(file.name || '')) {
          vm.error = 'İçe aktarım dosyası .json uzantılı olmalı.';
          return $q.when(null);
        }
        var deferred = $q.defer();
        var reader = new $window.FileReader();
        reader.onload = function() {
          $scope.$evalAsync(function() {
            vm.importParsed = core.parseImport(reader.result, file.size);
            if (!vm.importParsed.valid) vm.error = vm.importParsed.errors[0];
            deferred.resolve(vm.importParsed);
          });
        };
        reader.onerror = function() {
          $scope.$evalAsync(function() {
            vm.error = 'Dosya okunamadı.';
            deferred.resolve(null);
          });
        };
        reader.readAsText(file);
        return deferred.promise;
      };

      vm.submitImport = function(dryRun) {
        if (!vm.canEdit() || !vm.importParsed || !vm.importParsed.valid || vm.busy) {
          return $q.when(null);
        }
        return mutate(
          zumboApi.submitBulkImport(
            core.importRequest(projectId, vm.importParsed, dryRun),
            core.idempotencyKey(dryRun ? 'mobile-import-preview' : 'mobile-import')
          ),
          dryRun ? 'İçe aktarım önizlemesi sıraya alındı.' : 'İçe aktarım sıraya alındı.'
        );
      };

      vm.submitExport = function(dryRun) {
        if (vm.pwa.offline || vm.busy) return $q.when(null);
        return mutate(
          zumboApi.submitBulkExport({
            projectId: projectId,
            dryRun: dryRun === true,
            includeArchived: vm.exportDraft.includeArchived === true
          }, core.idempotencyKey(dryRun ? 'mobile-export-preview' : 'mobile-export')),
          dryRun ? 'Dışa aktarım kapsamı doğrulanıyor.' : 'Dışa aktarım sıraya alındı.'
        );
      };

      vm.requestCancel = function(job) {
        vm.cancelCandidateId = job && job.id;
      };

      vm.dismissCancel = function() {
        vm.cancelCandidateId = null;
      };

      vm.cancel = function(job) {
        if (!vm.canEdit() || !core.canCancel(job) || vm.busy) return $q.when(null);
        vm.cancelCandidateId = null;
        return mutate(zumboApi.cancelBulkJob(job.id), 'İptal isteği kaydedildi.');
      };

      vm.retry = function(job) {
        if (!vm.canEdit() || !core.canRetry(job) || vm.busy) return $q.when(null);
        return mutate(zumboApi.retryBulkJob(job.id), 'Başarısız satırlar yeniden sıraya alındı.');
      };

      vm.download = function(job, errors) {
        if (!job || vm.pwa.offline || core.artifactsExpired(job)
          || (errors ? !job.hasErrorFile : !job.hasResult)) {
          return $q.when(null);
        }
        vm.error = null;
        return zumboApi.downloadBulkJobArtifact(job.id, errors).then(function(blob) {
          var url = $window.URL.createObjectURL(blob);
          var link = $window.document.createElement('a');
          link.href = url;
          link.download = 'zumbo-' + String(job.type || 'job').toLowerCase() + '-'
            + job.id.slice(0, 8) + (errors ? '-errors.ndjson' : '-result.ndjson');
          link.click();
          $window.URL.revokeObjectURL(url);
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'İş dosyası indirilemedi.');
        });
      };

      function mutate(request, message) {
        vm.busy = true;
        vm.error = null;
        vm.notice = null;
        return request.then(function(job) {
          vm.notice = message;
          vm.selected = job;
          vm.mode = 'history';
          return vm.load().then(function() { return job; });
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'İş merkezi işlemi tamamlanamadı.');
          return null;
        }).finally(function() {
          vm.busy = false;
        });
      }

      function schedulePoll(generation) {
        var active = vm.jobs.some(function(job) {
          return !core.isTerminal(job) && job.state !== 'Failed';
        });
        if (!active || destroyed) return;
        $timeout(function() {
          if (destroyed || generation !== pollGeneration) return;
          vm.load({ quiet: true });
        }, 3000, false);
      }

      $scope.$on('$destroy', function() {
        destroyed = true;
        pollGeneration += 1;
        apiClient.cancelScope('mobile-bulk-jobs');
      });

      vm.load();
    });
})();
