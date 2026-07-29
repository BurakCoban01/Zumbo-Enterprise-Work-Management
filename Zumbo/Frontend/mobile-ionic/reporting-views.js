(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('ProjectReportingController', function($scope, $state, $stateParams, $q, $window, apiClient, zumboApi, sessionStore, mobileActionError, displayNameResolver) {
      var vm = this;
      var core = $window.ZumboReportingCore;
      var dashboardCore = $window.ZumboDashboardCore;
      apiClient.transitionContext('project-reporting:' + $stateParams.projectId);
      vm.mode = ['workload', 'reports', 'dashboards'].indexOf($stateParams.mode) >= 0 ? $stateParams.mode : 'workload';
      vm.rangeDays = [30, 90, 180].indexOf(Number($stateParams.range)) >= 0 ? Number($stateParams.range) : 30;
      vm.loading = true;
      vm.error = null;
      vm.notice = null;
      vm.tasks = [];
      vm.projects = [];
      vm.users = [];
      vm.scopeComplete = false;
      vm.workloadModel = core.workloadModel({});
      vm.reportingModel = core.reportingModel({});
      vm.freshness = core.freshness({});
      vm.rowLimits = {};
      vm.dashboardCatalog = dashboardCore.catalog;
      vm.dashboardWidgetType = dashboardCore.catalog[0].type;
      vm.dashboards = [];
      vm.dashboardDraft = null;
      vm.dashboardRender = null;
      vm.dashboardProjectId = null;
      vm.dashboardBusy = false;

      vm.setMode = function(mode) {
        if (['workload', 'reports', 'dashboards'].indexOf(mode) < 0) return;
        vm.mode = mode;
        $state.go('project-reporting', { projectId: vm.project.id, mode: mode, range: vm.rangeDays }, { notify: false, location: 'replace' });
        return loadCurrentMode();
      };
      vm.setRange = function(days) {
        if ([30, 90, 180].indexOf(Number(days)) < 0) return;
        vm.rangeDays = Number(days);
        $state.go('project-reporting', { projectId: vm.project.id, mode: vm.mode, range: vm.rangeDays }, { notify: false, location: 'replace' });
        if (vm.mode !== 'dashboards') loadCurrentMode();
      };
      vm.openTask = function(task) { if (task) $state.go('task-detail', { taskId: task.id }); };
      vm.toggleWorkload = function(row) {
        vm.expandedId = vm.expandedId === row.id ? null : row.id;
        if (vm.expandedId !== null && !vm.rowLimits[row.id]) vm.rowLimits[row.id] = 20;
      };
      vm.showMoreWorkload = function(row) { vm.rowLimits[row.id] = (vm.rowLimits[row.id] || 20) + 20; };
      vm.userName = function(id) { return displayNameResolver.user(id, vm.users, sessionStore.state.currentUser); };
      vm.dashboardProjectName = function(id) {
        var project = vm.projects.find(function(item) { return item.id === id; });
        return project ? project.name : 'Erişilebilir proje';
      };
      vm.dashboardWidgetLabel = function(type) {
        var item = vm.dashboardCatalog.find(function(candidate) { return candidate.type === type; });
        return item ? item.label : type;
      };
      vm.dashboardScopeLabel = function(scope) {
        return { Personal: 'Kişisel', Project: 'Proje', Portfolio: 'Portföy' }[scope] || scope;
      };
      vm.dashboardStatusLabel = function(status) {
        return { Ready: 'Hazır', Stale: 'Eski veri', Degraded: 'Kısmi' }[status] || status;
      };
      vm.dashboardCellValue = function(row, column) {
        var value = row && row[column.key];
        if (value === null || value === undefined || value === '') return '—';
        if (/userId$/i.test(column.key)) return vm.userName(value);
        return value;
      };
      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        vm.notice = null;
        vm.scopeComplete = false;
        vm.expandedId = null;
        return zumboApi.projects().then(function(projects) {
          vm.projects = projects;
          vm.project = projects.find(function(project) { return project.id === $stateParams.projectId; });
          if (!vm.project) throw new Error('PROJECT_NOT_FOUND');
          sessionStore.state.project = vm.project;
          return zumboApi.users().catch(function() { return []; });
        }).then(function(users) {
          vm.users = users || [];
          return loadCurrentMode();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Proje içgörüleri yüklenemedi.');
        }).finally(function() { vm.loading = false; });
      };

      vm.newDashboard = function() {
        vm.dashboardDraft = dashboardCore.create(vm.project && vm.project.id);
        vm.dashboardProjectId = vm.project && vm.project.id;
        vm.dashboardRender = null;
        vm.notice = null;
        vm.error = null;
      };
      vm.selectDashboard = function(item) {
        if (!item) return $q.when(null);
        vm.dashboardBusy = true;
        vm.error = null;
        return apiClient.get('/api/dashboards/' + item.id, {
          scope: 'mobile-dashboard-detail',
          replace: true
        }).then(function(value) {
          vm.dashboardDraft = dashboardCore.fromResponse(value);
          vm.dashboardProjectId = vm.dashboardDraft.projectIds[0] || (vm.project && vm.project.id);
          return vm.renderDashboard();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Dashboard açılamadı.');
        }).finally(function() { vm.dashboardBusy = false; });
      };
      vm.dashboardScopeChanged = function() {
        if (!vm.dashboardDraft) return;
        var currentProjectId = vm.project && vm.project.id;
        if (vm.dashboardDraft.scope === 'Project') {
          vm.dashboardProjectId = vm.dashboardProjectId || currentProjectId
            || vm.dashboardDraft.projectIds[0] || null;
          vm.dashboardDraft.projectIds = vm.dashboardProjectId ? [vm.dashboardProjectId] : [];
          return;
        }
        if (!vm.dashboardDraft.projectIds.length && currentProjectId) {
          vm.dashboardDraft.projectIds = [currentProjectId];
        }
      };
      vm.setDashboardProject = function(projectId) {
        vm.dashboardProjectId = projectId;
        if (vm.dashboardDraft) vm.dashboardDraft.projectIds = projectId ? [projectId] : [];
      };
      vm.addDashboardWidget = function() {
        if (!dashboardCore.addWidget(vm.dashboardDraft, vm.dashboardWidgetType)) {
          vm.error = 'Bir dashboard en fazla 12 widget içerebilir.';
        }
      };
      vm.removeDashboardWidget = function(widget) {
        if (!dashboardCore.removeWidget(vm.dashboardDraft, widget.id)) {
          vm.error = 'Dashboard en az bir widget içermelidir.';
        }
      };
      vm.moveDashboardWidget = function(index, direction) {
        dashboardCore.moveWidget(vm.dashboardDraft, index, direction);
      };
      vm.saveDashboard = function() {
        if (!vm.dashboardDraft || vm.dashboardBusy) return $q.when(null);
        var validationError = dashboardCore.validate(vm.dashboardDraft);
        if (validationError) {
          vm.error = validationError;
          return $q.when(null);
        }
        vm.dashboardBusy = true;
        vm.error = null;
        vm.notice = null;
        var payload = dashboardCore.payload(vm.dashboardDraft);
        var request = vm.dashboardDraft.id
          ? apiClient.put('/api/dashboards/' + vm.dashboardDraft.id, payload)
          : apiClient.post('/api/dashboards', payload);
        return request.then(function(value) {
          vm.dashboardDraft = dashboardCore.fromResponse(value);
          vm.dashboardProjectId = vm.dashboardDraft.projectIds[0] || null;
          var index = vm.dashboards.findIndex(function(item) { return item.id === value.id; });
          if (index >= 0) vm.dashboards[index] = value;
          else vm.dashboards.unshift(value);
          vm.notice = 'Dashboard kaydedildi.';
          return vm.renderDashboard();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Dashboard kaydedilemedi.');
        }).finally(function() { vm.dashboardBusy = false; });
      };
      vm.shareDashboard = function() {
        if (!vm.dashboardDraft || !vm.dashboardDraft.id || !vm.dashboardDraft.canEdit || vm.dashboardBusy) {
          return $q.when(null);
        }
        vm.dashboardBusy = true;
        vm.error = null;
        return apiClient.put('/api/dashboards/' + vm.dashboardDraft.id + '/sharing', {
          viewerUserIds: (vm.dashboardDraft.viewerUserIds || []).slice()
        }).then(function(value) {
          vm.dashboardDraft = dashboardCore.fromResponse(value);
          vm.notice = 'Paylaşım güncellendi.';
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Paylaşım güncellenemedi.');
        }).finally(function() { vm.dashboardBusy = false; });
      };
      vm.renderDashboard = function() {
        if (!vm.dashboardDraft || !vm.dashboardDraft.id) {
          vm.dashboardRender = null;
          return $q.when(null);
        }
        return apiClient.get('/api/dashboards/' + vm.dashboardDraft.id + '/render', {
          scope: 'mobile-dashboard-render',
          replace: true
        }).then(function(value) {
          vm.dashboardRender = value;
          return value;
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Dashboard verileri yenilenemedi.');
        });
      };
      vm.exportDashboard = function() {
        if (!vm.dashboardDraft || !vm.dashboardDraft.id) return $q.when(null);
        return apiClient.download('/api/dashboards/' + vm.dashboardDraft.id + '/export')
          .then(function(blob) {
            var url = $window.URL.createObjectURL(blob);
            var link = $window.document.createElement('a');
            link.href = url;
            link.download = 'zumbo-dashboard-' + vm.dashboardDraft.id + '.json';
            link.click();
            $window.URL.revokeObjectURL(url);
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Dashboard dışa aktarılamadı.');
          });
      };
      vm.archiveDashboard = function() {
        if (!vm.dashboardDraft || !vm.dashboardDraft.id || !vm.dashboardDraft.canEdit
            || !$window.confirm('Bu dashboard arşivlensin mi?')) return $q.when(null);
        vm.dashboardBusy = true;
        return apiClient.delete('/api/dashboards/' + vm.dashboardDraft.id)
          .then(function() {
            vm.notice = 'Dashboard arşivlendi.';
            vm.dashboardDraft = null;
            vm.dashboardRender = null;
            vm.dashboardBusy = false;
            return loadDashboards();
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'Dashboard arşivlenemedi.');
          }).finally(function() { vm.dashboardBusy = false; });
      };

      function loadCurrentMode() {
        if (!vm.project) return $q.when(null);
        vm.loading = true;
        vm.error = null;
        if (vm.mode === 'dashboards') {
          return loadDashboards().finally(function() { vm.loading = false; });
        }
        return $q.all([loadTasks(vm.project.id), loadReports(vm.project.id)])
          .then(function(result) {
            vm.tasks = result[0];
            vm.scopeComplete = true;
            rebuild(result[1]);
          }).catch(function(error) {
            vm.error = mobileActionError(error, 'İş yükü ve raporlar yüklenemedi.');
          }).finally(function() { vm.loading = false; });
      }

      function loadDashboards() {
        if (!vm.project || vm.dashboardBusy) return $q.when(vm.dashboards);
        vm.dashboardBusy = true;
        return apiClient.get('/api/dashboards?page=1&pageSize=100', {
          scope: 'mobile-dashboards',
          replace: true
        }).then(function(page) {
          vm.dashboards = (page.items || []).filter(function(item) {
            return (item.projectIds || []).indexOf(vm.project.id) >= 0;
          });
          if (vm.dashboardDraft && vm.dashboardDraft.id) {
            var current = vm.dashboards.find(function(item) { return item.id === vm.dashboardDraft.id; });
            if (current) return vm.selectDashboard(current);
          }
          if (vm.dashboards.length) return vm.selectDashboard(vm.dashboards[0]);
          vm.newDashboard();
          return vm.dashboardDraft;
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Dashboardlar yüklenemedi.');
        }).finally(function() { vm.dashboardBusy = false; });
      }

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
