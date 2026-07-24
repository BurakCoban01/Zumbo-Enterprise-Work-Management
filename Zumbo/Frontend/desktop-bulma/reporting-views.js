(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopReportingViewsFeature', function($q, $window, apiClient) {
      return {
        install: function(vm, helpers) {
          var core = $window.ZumboReportingCore;
          var legacyLoadReports = vm.loadReports;
          var updateLocation = helpers.updateLocation;
          var apiActionError = helpers.apiActionError;
          vm.reportingRangeDays = 30;
          vm.reportingLoading = false;
          vm.reportingError = null;
          vm.reportingPartial = false;
          vm.reportingSnapshots = {};
          vm.reportingTasks = [];
          vm.reportingScopeComplete = false;
          vm.reportingFreshness = core.freshness({});
          vm.workloadModel = core.workloadModel({});
          vm.reportingModel = core.reportingModel({});
          vm.reportingDrilldown = null;
          vm.reportingTableOpen = false;

          vm.loadReports = function(projectId) {
            return legacyLoadReports(projectId).then(function(result) {
              if (vm.isReportingView()) return vm.prepareReportingView();
              return result;
            });
          };

          vm.isReportingView = function(mode) {
            return ['workload', 'reports'].indexOf(mode || vm.workMode) >= 0;
          };

          vm.prepareReportingView = function() {
            if (!vm.project || !vm.isReportingView() || vm.reportingLoading) return $q.when(vm.reportingModel);
            var projectId = vm.project.id;
            vm.reportingLoading = true;
            vm.reportingError = null;
            vm.reportingPartial = false;
            vm.reportingScopeComplete = false;
            return loadCompleteTaskScope(projectId)
              .then(function() { return loadSnapshots(projectId); })
              .then(function() {
                if (!vm.project || vm.project.id !== projectId) return;
                rebuild();
                return vm.reportingModel;
              }).catch(function(error) {
                if (vm.project && vm.project.id === projectId) {
                  vm.reportingPartial = true;
                  vm.reportingError = apiActionError(error, 'Raporların bir bölümü yüklenemedi. Kullanılabilen sonuçlar gösteriliyor.');
                  rebuild();
                }
                return vm.reportingModel;
              }).finally(function() {
                if (vm.project && vm.project.id === projectId) vm.reportingLoading = false;
              });
          };

          vm.setReportingRange = function(days) {
            if ([30, 90, 180].indexOf(Number(days)) < 0) return;
            vm.reportingRangeDays = Number(days);
            vm.reportingDrilldown = null;
            updateLocation(vm.activeSection, null, false);
            return vm.prepareReportingView();
          };

          vm.openWorkloadDrilldown = function(row) {
            vm.reportingDrilldown = row ? { title: row.label, tasks: row.tasks || [], limit: 50 } : null;
          };

          vm.showMoreReportingTasks = function() {
            if (vm.reportingDrilldown) vm.reportingDrilldown.limit += 50;
          };

          vm.openRiskDrilldown = function(risk) {
            var task = (vm.tasks || []).find(function(item) { return item.id === risk.id; });
            if (task) vm.selectTask(task);
          };

          vm.applyReportingLocation = function(params) {
            var days = Number(params.get('range'));
            if ([30, 90, 180].indexOf(days) >= 0) vm.reportingRangeDays = days;
          };

          function loadCompleteTaskScope(projectId) {
            var tasks = [];
            function page(number) {
              return apiClient.post('/api/work-items/search', {
                projectId: projectId,
                text: null,
                page: number,
                pageSize: 100
              }, { scope: 'desktop-report-task-load' }).then(function(result) {
                if (!vm.project || vm.project.id !== projectId) return tasks;
                tasks = tasks.concat(result.items || []);
                return tasks.length < Number(result.totalCount || tasks.length) ? page(number + 1) : tasks;
              });
            }
            return page(1).then(function(result) {
              if (vm.project && vm.project.id === projectId) {
                vm.reportingTasks = result;
                vm.reportingScopeComplete = true;
              }
            });
          }

          function dateRange() {
            var to = new Date();
            var from = new Date(to.getFullYear(), to.getMonth(), to.getDate() - vm.reportingRangeDays + 1);
            return '?from=' + key(from) + '&to=' + key(to);
          }

          function loadSnapshots(projectId) {
            var range = dateRange();
            var snapshots = {};
            function capture(name, request) {
              return request.then(function(response) { snapshots[name] = core.snapshot(response); });
            }
            return $q.all([
              capture('summary', apiClient.get('/api/work-items/reports/project-summary/' + projectId, { rawResponse: true })),
              capture('status', apiClient.get('/api/work-items/reports/status-distribution/' + projectId, { rawResponse: true })),
              capture('workload', apiClient.get('/api/work-items/reports/user-workload/' + projectId, { rawResponse: true })),
              capture('risks', apiClient.get('/api/work-items/reports/due-date-risks/' + projectId + '?days=30', { rawResponse: true })),
              capture('flow', apiClient.get('/api/work-items/reports/flow-time/' + projectId + range, { rawResponse: true })),
              capture('completion', apiClient.get('/api/work-items/reports/completion-rate/' + projectId + range, { rawResponse: true })),
              capture('teams', apiClient.get('/api/work-items/reports/team-performance/' + projectId + range, { rawResponse: true }))
            ]).then(function() { vm.reportingSnapshots = snapshots; });
          }

          function rebuild() {
            var data = function(name, fallback) {
              return vm.reportingSnapshots[name] ? vm.reportingSnapshots[name].data : fallback;
            };
            vm.workloadModel = core.workloadModel({
              workload: data('workload', vm.workload || []),
              tasks: vm.reportingTasks,
              scopeComplete: vm.reportingScopeComplete,
              userName: vm.userName
            });
            vm.reportingModel = core.reportingModel({
              summary: data('summary', vm.summary),
              status: data('status', vm.statusDistribution),
              risks: data('risks', vm.dueDateRisks),
              flow: data('flow', null),
              completion: data('completion', null),
              teams: data('teams', []),
              rangeDays: vm.reportingRangeDays
            });
            vm.reportingFreshness = core.freshness(vm.reportingSnapshots);
          }

          function key(value) {
            return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0');
          }
        }
      };
    });
})();
