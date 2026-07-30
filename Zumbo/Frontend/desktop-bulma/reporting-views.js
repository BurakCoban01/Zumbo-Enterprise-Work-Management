(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopReportingViewsFeature', function($q, $window, apiClient) {
      return {
        install: function(vm, helpers) {
          var core = $window.ZumboReportingCore;
          var dashboardCore = $window.ZumboDashboardCore;
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
          vm.dashboardCatalog = dashboardCore.catalog;
          vm.dashboards = [];
          vm.dashboardDraft = null;
          vm.dashboardRender = null;
          vm.dashboardBusy = false;
          vm.dashboardNotice = null;
          vm.dashboardError = null;
          vm.dashboardProjectId = null;
          vm.dashboardWidgetType = dashboardCore.catalog[0].type;

          vm.loadReports = function(projectId) {
            return legacyLoadReports(projectId).then(function(result) {
              if (vm.isReportingView()) return vm.prepareReportingView();
              return result;
            });
          };

          vm.isReportingView = function(mode) {
            return ['workload', 'reports', 'dashboards'].indexOf(mode || vm.workMode) >= 0;
          };

          vm.prepareReportingView = function() {
            if (!vm.project || !vm.isReportingView() || vm.reportingLoading) return $q.when(vm.reportingModel);
            var projectId = vm.project.id;
            if (vm.workMode === 'dashboards') return vm.loadDashboards();
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

          vm.loadDashboards = function() {
            if (!vm.project || vm.dashboardBusy) return $q.when(vm.dashboards);
            vm.dashboardBusy = true;
            vm.dashboardError = null;
            var directory = !vm.users.length && typeof vm.loadUsers === 'function'
              ? vm.loadUsers()
              : $q.when(vm.users);
            return $q.all([
              apiClient.get('/api/dashboards?page=1&pageSize=100', {
                scope: 'desktop-dashboards',
                replace: true
              }),
              directory
            ]).then(function(result) {
              var page = result[0];
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
              vm.dashboardError = apiActionError(error, 'Dashboardlar yüklenemedi.');
            }).finally(function() { vm.dashboardBusy = false; });
          };

          vm.newDashboard = function() {
            vm.dashboardDraft = dashboardCore.create(vm.project && vm.project.id);
            vm.dashboardRender = null;
            vm.dashboardProjectId = vm.project && vm.project.id;
            vm.dashboardNotice = null;
          };

          vm.selectDashboard = function(item) {
            if (!item) return $q.when(null);
            vm.dashboardBusy = true;
            vm.dashboardError = null;
            return apiClient.get('/api/dashboards/' + item.id, {
              scope: 'desktop-dashboard-detail',
              replace: true
            }).then(function(value) {
              vm.dashboardDraft = dashboardCore.fromResponse(value);
              vm.dashboardProjectId = vm.dashboardDraft.projectIds[0] || (vm.project && vm.project.id);
              return vm.renderDashboard();
            }).catch(function(error) {
              vm.dashboardError = apiActionError(error, 'Dashboard açılamadı.');
            }).finally(function() { vm.dashboardBusy = false; });
          };

          vm.addDashboardWidget = function() {
            if (!dashboardCore.addWidget(vm.dashboardDraft, vm.dashboardWidgetType)) {
              vm.dashboardError = 'Bir dashboard en fazla 12 widget içerebilir.';
            }
          };
          vm.removeDashboardWidget = function(widget) {
            if (!dashboardCore.removeWidget(vm.dashboardDraft, widget.id)) {
              vm.dashboardError = 'Dashboard en az bir widget içermelidir.';
            }
          };
          vm.moveDashboardWidget = function(index, direction) {
            dashboardCore.moveWidget(vm.dashboardDraft, index, direction);
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
            if (vm.dashboardDraft) {
              vm.dashboardDraft.projectIds = projectId ? [projectId] : [];
            }
          };
          vm.dashboardProjectName = function(projectId) {
            var project = (vm.projects || []).find(function(item) { return item.id === projectId; });
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

          vm.saveDashboard = function() {
            if (!vm.dashboardDraft || vm.dashboardBusy) return $q.when(null);
            var validationError = dashboardCore.validate(vm.dashboardDraft);
            if (validationError) {
              vm.dashboardError = validationError;
              return $q.when(null);
            }
            vm.dashboardBusy = true;
            vm.dashboardError = null;
            vm.dashboardNotice = null;
            var payload = dashboardCore.payload(vm.dashboardDraft);
            var request = vm.dashboardDraft.id
              ? apiClient.put('/api/dashboards/' + vm.dashboardDraft.id, payload)
              : apiClient.post('/api/dashboards', payload);
            return request.then(function(value) {
              vm.dashboardDraft = dashboardCore.fromResponse(value);
              var existing = vm.dashboards.findIndex(function(item) { return item.id === value.id; });
              if (existing >= 0) vm.dashboards[existing] = value;
              else vm.dashboards.unshift(value);
              vm.dashboardNotice = 'Dashboard kaydedildi.';
              return vm.renderDashboard();
            }).catch(function(error) {
              vm.dashboardError = apiActionError(error, 'Dashboard kaydedilemedi.');
            }).finally(function() { vm.dashboardBusy = false; });
          };

          vm.shareDashboard = function() {
            if (!vm.dashboardDraft || !vm.dashboardDraft.id || !vm.dashboardDraft.canEdit) return $q.when(null);
            vm.dashboardBusy = true;
            vm.dashboardError = null;
            return apiClient.put('/api/dashboards/' + vm.dashboardDraft.id + '/sharing', {
              viewerUserIds: (vm.dashboardDraft.viewerUserIds || []).slice()
            }).then(function(value) {
              vm.dashboardDraft = dashboardCore.fromResponse(value);
              vm.dashboardNotice = 'Paylaşım güncellendi.';
            }).catch(function(error) {
              vm.dashboardError = apiActionError(error, 'Paylaşım güncellenemedi.');
            }).finally(function() { vm.dashboardBusy = false; });
          };

          vm.renderDashboard = function() {
            if (!vm.dashboardDraft || !vm.dashboardDraft.id) {
              vm.dashboardRender = null;
              return $q.when(null);
            }
            return apiClient.get('/api/dashboards/' + vm.dashboardDraft.id + '/render', {
              scope: 'desktop-dashboard-render',
              replace: true
            }).then(function(value) {
              vm.dashboardRender = value;
              return value;
            }).catch(function(error) {
              vm.dashboardError = apiActionError(error, 'Dashboard verileri yenilenemedi.');
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
                vm.dashboardError = apiActionError(error, 'Dashboard dışa aktarılamadı.');
              });
          };

          vm.archiveDashboard = function() {
            if (!vm.dashboardDraft || !vm.dashboardDraft.id || !vm.dashboardDraft.canEdit
                || !$window.confirm('Bu dashboard arşivlensin mi?')) return $q.when(null);
            vm.dashboardBusy = true;
            return apiClient.delete('/api/dashboards/' + vm.dashboardDraft.id)
              .then(function() {
                vm.dashboardDraft = null;
                vm.dashboardRender = null;
                vm.dashboardNotice = 'Dashboard arşivlendi.';
                vm.dashboardBusy = false;
                return vm.loadDashboards();
              }).catch(function(error) {
                vm.dashboardError = apiActionError(error, 'Dashboard arşivlenemedi.');
              }).finally(function() { vm.dashboardBusy = false; });
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
