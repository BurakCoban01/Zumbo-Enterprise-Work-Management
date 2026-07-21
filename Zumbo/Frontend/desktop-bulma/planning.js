(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopPlanningFeature', function($q, apiClient) {
      function dateKey(value) {
        if (!value) return '';
        if (typeof value === 'string') return value.slice(0, 10);
        return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0');
      }

      function dateLabel(value) {
        if (!value) return 'Tarih yok';
        var parts = dateKey(value).split('-');
        return parts.length === 3 ? parts[2] + '.' + parts[1] + '.' + parts[0] : value;
      }

      function initialSprintDraft() {
        var start = new Date();
        var end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 13);
        return { name: '', goal: '', startDate: start, endDate: end };
      }

      return {
        install: function(vm, apiActionError) {
          vm.workMode = vm.workMode || 'board';
          vm.backlogItems = vm.backlogItems || [];
          vm.timelineEntries = vm.timelineEntries || [];
          vm.calendarGroups = vm.calendarGroups || [];
          vm.roadmapSprints = vm.roadmapSprints || [];
          vm.selectedPlanningSprintId = vm.selectedPlanningSprintId || '';
          vm.sprintDraft = vm.sprintDraft || initialSprintDraft();

          vm.canPlanSprint = function() {
            return !!vm.projectMembership && vm.projectMembership.role !== 'Viewer';
          };

          vm.setWorkMode = function(mode) {
            vm.workMode = mode;
            vm.clearSelection();
            if (mode === 'timeline') vm.loadTimeline();
            vm.rebuildAdvancedViews();
          };

          vm.rebuildAdvancedViews = function() {
            var tasks = (vm.tasks || []).slice();
            vm.calendarGroups = [];
            var byDate = {};
            tasks.filter(function(task) { return !!task.dueDate; }).forEach(function(task) {
              var key = dateKey(task.dueDate);
              byDate[key] = byDate[key] || [];
              byDate[key].push(task);
            });
            vm.calendarGroups = Object.keys(byDate).sort().map(function(key) {
              return { key: key, label: dateLabel(key), tasks: byDate[key] };
            });
            vm.roadmapSprints = (vm.sprints || []).slice().sort(function(left, right) {
              return dateKey(left.startDate).localeCompare(dateKey(right.startDate));
            });
            if (!vm.selectedPlanningSprintId || !(vm.sprints || []).some(function(sprint) {
              return sprint.id === vm.selectedPlanningSprintId;
            })) {
              var preferred = (vm.sprints || []).find(function(sprint) { return sprint.status === 'Active'; })
                || (vm.sprints || []).find(function(sprint) { return sprint.status === 'Planned'; })
                || (vm.sprints || [])[0];
              vm.selectedPlanningSprintId = preferred ? preferred.id : '';
            }
            vm.selectPlanningSprint();
          };

          vm.selectPlanningSprint = function() {
            vm.selectedPlanningSprint = (vm.sprints || []).find(function(sprint) {
              return sprint.id === vm.selectedPlanningSprintId;
            }) || null;
            vm.sprintItems = (vm.tasks || []).filter(function(task) {
              return vm.selectedPlanningSprint && task.sprintId === vm.selectedPlanningSprint.id;
            });
            if (!vm.selectedPlanningSprint) {
              vm.burndown = [];
              return;
            }
            return apiClient.get('/api/sprints/' + vm.selectedPlanningSprint.id + '/burndown')
              .then(function(points) { vm.burndown = points; return points; })
              .catch(function() { vm.burndown = []; return []; });
          };

          vm.loadTimeline = function() {
            if (!vm.project || !vm.projectMembership) return $q.when([]);
            var projectId = vm.project.id;
            function entityAudit(type, id) {
              return id ? apiClient.get('/api/audit/entity/' + type + '/' + id).catch(function() { return []; }) : $q.when([]);
            }
            var requests = [entityAudit('Project', projectId)];
            if (vm.board) requests.push(entityAudit('Board', vm.board.id));
            (vm.sprints || []).forEach(function(sprint) { requests.push(entityAudit('Sprint', sprint.id)); });
            if (vm.selectedTask) requests.push(entityAudit('WorkItem', vm.selectedTask.id));
            return $q.all(requests)
              .then(function(groups) {
                if (!vm.project || vm.project.id !== projectId) return [];
                var seen = new Set();
                vm.timelineEntries = groups.flat().filter(function(entry) {
                  if (seen.has(entry.id)) return false;
                  seen.add(entry.id);
                  return true;
                }).sort(function(left, right) {
                  return new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime();
                });
                return vm.timelineEntries;
              }).catch(function() { vm.timelineEntries = []; return []; });
          };

          vm.loadReports = function(projectId) {
            if (!vm.project) return $q.when();
            projectId = projectId || vm.project.id;
            function assignIfCurrent(assign) {
              return function(data) {
                if (vm.project && vm.project.id === projectId) assign(data);
              };
            }
            return $q.all([
              apiClient.get('/api/work-items/reports/status-distribution/' + projectId)
                .then(assignIfCurrent(function(data) { vm.statusDistribution = data; })),
              apiClient.get('/api/work-items/reports/user-workload/' + projectId)
                .then(assignIfCurrent(function(data) { vm.workload = data; })),
              apiClient.get('/api/work-items/reports/due-date-risks/' + projectId + '?days=14')
                .then(assignIfCurrent(function(data) { vm.dueDateRisks = data; })),
              apiClient.get('/api/work-items/reports/sprint-velocity/' + projectId + '?sprintCount=3')
                .then(assignIfCurrent(function(data) { vm.velocity = data; })),
              apiClient.get('/api/sprints/projects/' + projectId + '?pageSize=50')
                .then(assignIfCurrent(function(data) { vm.sprints = data.items || data; })),
              apiClient.get('/api/sprints/projects/' + projectId + '/backlog?pageSize=100')
                .then(assignIfCurrent(function(data) { vm.backlogItems = data.items || data; }))
            ]).then(function() {
              if (!vm.project || vm.project.id !== projectId) return;
              vm.rebuildAdvancedViews();
              return vm.workMode === 'timeline' ? vm.loadTimeline() : null;
            });
          };

          function planningMutation(request, successMessage) {
            if (vm.sprintBusy) return;
            vm.sprintBusy = true;
            return request.then(function() {
              vm.notify('success', successMessage);
              return vm.loadTasks().then(function() { return true; });
            }).catch(function(error) {
              vm.notify('error', apiActionError(error, 'Sprint işlemi tamamlanamadı.'));
              return false;
            }).finally(function() { vm.sprintBusy = false; });
          }

          vm.createSprint = function() {
            if (!vm.project || !vm.canPlanSprint() || !vm.sprintDraft.name || vm.sprintBusy) return;
            var draft = vm.sprintDraft;
            return planningMutation(apiClient.post('/api/sprints', {
              projectId: vm.project.id,
              name: draft.name,
              goal: draft.goal || null,
              startDate: dateKey(draft.startDate),
              endDate: dateKey(draft.endDate)
            }), 'Sprint oluşturuldu.').then(function(succeeded) {
              if (succeeded) vm.sprintDraft = initialSprintDraft();
            });
          };

          vm.planBacklogItem = function(item) {
            if (!item || !vm.canPlanSprint() || !vm.selectedPlanningSprint || vm.selectedPlanningSprint.status !== 'Planned') return;
            return planningMutation(apiClient.put(
              '/api/sprints/' + vm.selectedPlanningSprint.id + '/items/' + item.id,
              { estimatePoints: item.estimatePoints || 0 }
            ), 'İş sprint kapsamına alındı.');
          };

          vm.unplanSprintItem = function(item) {
            if (!item || !vm.canPlanSprint() || !vm.selectedPlanningSprint || vm.selectedPlanningSprint.status !== 'Planned') return;
            return planningMutation(apiClient.delete(
              '/api/sprints/' + vm.selectedPlanningSprint.id + '/items/' + item.id
            ), 'İş backlog alanına taşındı.');
          };

          vm.startSelectedSprint = function() {
            if (!vm.canPlanSprint() || !vm.selectedPlanningSprint || vm.selectedPlanningSprint.status !== 'Planned') return;
            return planningMutation(apiClient.post(
              '/api/sprints/' + vm.selectedPlanningSprint.id + '/start', {}
            ), 'Sprint başlatıldı.');
          };

          vm.completeSelectedSprint = function() {
            if (!vm.canPlanSprint() || !vm.selectedPlanningSprint || vm.selectedPlanningSprint.status !== 'Active') return;
            return planningMutation(apiClient.post(
              '/api/sprints/' + vm.selectedPlanningSprint.id + '/complete',
              { carryoverSprintId: vm.carryoverSprintId || null }
            ), 'Sprint tamamlandı.');
          };

          vm.loadWorkflow = function(projectId) {
            if (!vm.project) return $q.when();
            projectId = projectId || vm.project.id;
            vm.workflowLoading = true;
            return apiClient.get('/api/workflows/' + projectId).then(function(workflow) {
              if (vm.project && vm.project.id === projectId) {
                vm.workflow = workflow;
                vm.workflowDraft = angular.copy(workflow);
              }
            }).finally(function() {
              if (vm.project && vm.project.id === projectId) vm.workflowLoading = false;
            });
          };

          vm.addWorkflowStatus = function() {
            vm.workflowDraft.statuses = vm.workflowDraft.statuses || [];
            vm.workflowDraft.statuses.push({ name: '', category: 'InProgress' });
          };

          vm.removeWorkflowStatus = function(index) {
            if (!vm.workflowDraft.statuses || vm.workflowDraft.statuses.length <= 1) return;
            vm.workflowDraft.statuses.splice(index, 1);
          };

          vm.addWorkflowTransition = function() {
            vm.workflowDraft.transitions = vm.workflowDraft.transitions || [];
            vm.workflowDraft.transitions.push({
              fromStatus: '',
              toStatus: '',
              requiresAssignee: false,
              requiresCompletedChecklist: false,
              requiresApproval: false,
              automations: []
            });
          };

          vm.removeWorkflowTransition = function(index) {
            vm.workflowDraft.transitions.splice(index, 1);
          };

          vm.saveWorkflow = function() {
            if (!vm.project || !vm.canManageProject || vm.entitySaving) return;
            var invalidStatus = (vm.workflowDraft.statuses || []).some(function(status) { return !status.name || !status.category; });
            var invalidTransition = (vm.workflowDraft.transitions || []).some(function(transition) {
              return !transition.fromStatus || !transition.toStatus;
            });
            if (invalidStatus || invalidTransition) return vm.notify('error', 'Workflow durum ve geçiş alanlarını tamamlayın.');
            vm.entitySaving = true;
            return apiClient.put('/api/workflows/' + vm.project.id, {
              projectId: vm.project.id,
              statuses: vm.workflowDraft.statuses,
              transitions: vm.workflowDraft.transitions
            }).then(function(workflow) {
              vm.workflow = workflow;
              vm.workflowDraft = angular.copy(workflow);
              vm.notify('success', 'Workflow kaydedildi.');
              return vm.loadProjectAudit();
            }).catch(function(error) { vm.notify('error', apiActionError(error, 'Workflow kaydedilemedi.')); })
              .finally(function() { vm.entitySaving = false; });
          };
        }
      };
    });
})();
