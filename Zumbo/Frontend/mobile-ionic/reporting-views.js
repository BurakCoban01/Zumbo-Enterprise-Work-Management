(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('ProjectReportingController', function($scope, $state, $stateParams, $q, $window, apiClient, zumboApi, sessionStore, mobileActionError, displayNameResolver) {
      var vm = this;
      var core = $window.ZumboReportingCore;
      apiClient.transitionContext('project-reporting:' + $stateParams.projectId);
      vm.mode = ['workload', 'reports'].indexOf($stateParams.mode) >= 0 ? $stateParams.mode : 'workload';
      vm.rangeDays = [30, 90, 180].indexOf(Number($stateParams.range)) >= 0 ? Number($stateParams.range) : 30;
      vm.loading = true;
      vm.error = null;
      vm.tasks = [];
      vm.scopeComplete = false;
      vm.workloadModel = core.workloadModel({});
      vm.reportingModel = core.reportingModel({});
      vm.freshness = core.freshness({});
      vm.rowLimits = {};

      vm.setMode = function(mode) {
        if (['workload', 'reports'].indexOf(mode) < 0) return;
        vm.mode = mode;
        $state.go('project-reporting', { projectId: vm.project.id, mode: mode, range: vm.rangeDays }, { notify: false, location: 'replace' });
      };
      vm.setRange = function(days) {
        if ([30, 90, 180].indexOf(Number(days)) < 0) return;
        vm.rangeDays = Number(days);
        $state.go('project-reporting', { projectId: vm.project.id, mode: vm.mode, range: vm.rangeDays }, { notify: false, location: 'replace' });
        vm.load();
      };
      vm.openTask = function(task) { if (task) $state.go('task-detail', { taskId: task.id }); };
      vm.toggleWorkload = function(row) {
        vm.expandedId = vm.expandedId === row.id ? null : row.id;
        if (vm.expandedId !== null && !vm.rowLimits[row.id]) vm.rowLimits[row.id] = 20;
      };
      vm.showMoreWorkload = function(row) { vm.rowLimits[row.id] = (vm.rowLimits[row.id] || 20) + 20; };
      vm.userName = function(id) { return displayNameResolver.user(id, [], sessionStore.state.currentUser); };
      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        vm.scopeComplete = false;
        vm.expandedId = null;
        return zumboApi.projects().then(function(projects) {
          vm.project = projects.find(function(project) { return project.id === $stateParams.projectId; });
          if (!vm.project) throw new Error('PROJECT_NOT_FOUND');
          sessionStore.state.project = vm.project;
          return $q.all([loadTasks(vm.project.id), loadReports(vm.project.id)]);
        }).then(function(result) {
          vm.tasks = result[0];
          vm.scopeComplete = true;
          rebuild(result[1]);
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'İş yükü ve raporlar yüklenemedi.');
        }).finally(function() { vm.loading = false; });
      };

      function loadTasks(projectId) {
        var tasks = [];
        function page(number) {
          return zumboApi.projectTasks(projectId, '', number, 100).then(function(result) {
            var items = result.items || [];
            tasks = tasks.concat(items);
            return tasks.length < Number(result.totalCount || tasks.length) ? page(number + 1) : tasks;
          });
        }
        return page(1);
      }

      function loadReports(projectId) {
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
        ]).then(function() { return snapshots; });
      }

      function rebuild(snapshots) {
        var data = function(name, fallback) { return snapshots[name] ? snapshots[name].data : fallback; };
        vm.workloadModel = core.workloadModel({ workload: data('workload', []), tasks: vm.tasks, scopeComplete: true, userName: vm.userName });
        vm.reportingModel = core.reportingModel({ summary: data('summary', {}), status: data('status', []), risks: data('risks', []), flow: data('flow', null), completion: data('completion', null), teams: data('teams', []), rangeDays: vm.rangeDays });
        vm.freshness = core.freshness(snapshots);
      }

      function dateRange() {
        var to = new Date();
        var from = new Date(to.getFullYear(), to.getMonth(), to.getDate() - vm.rangeDays + 1);
        return '?from=' + key(from) + '&to=' + key(to);
      }
      function key(value) { return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0'); }
      $scope.$on('$ionicView.beforeEnter', vm.load);
    });
})();
