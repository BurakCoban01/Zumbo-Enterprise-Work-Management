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
          vm.sprintItems = vm.sprintItems || [];
          vm.burndown = vm.burndown || [];
          vm.planningError = null;
          vm.planningLoading = false;
          vm.burndownLoading = false;
          vm.burndownError = null;
          vm.backlogNextCursor = null;
          vm.sprintNextCursor = null;

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
              vm.burndownError = null;
              return;
            }
            var sprintId = vm.selectedPlanningSprint.id;
            vm.burndownLoading = true;
            vm.burndownError = null;
            return apiClient.get('/api/sprints/' + vm.selectedPlanningSprint.id + '/burndown')
              .then(function(points) {
                if (!vm.selectedPlanningSprint || vm.selectedPlanningSprint.id !== sprintId) return [];
                vm.burndown = points || [];
                vm.burndownRefreshedAt = new Date();
                return vm.burndown;
              })
              .catch(function() {
                if (!vm.selectedPlanningSprint || vm.selectedPlanningSprint.id !== sprintId) return [];
                vm.burndown = [];
                vm.burndownError = 'Burndown verisi yuklenemedi.';
                return [];
              }).finally(function() {
                if (vm.selectedPlanningSprint && vm.selectedPlanningSprint.id === sprintId) vm.burndownLoading = false;
              });
          };

          vm.planningPoints = function() {
            return (vm.sprintItems || []).reduce(function(total, item) {
              return total + Number(item.estimatePoints || 0);
            }, 0);
          };

          vm.capacityBaseline = function() {
            var completed = (vm.velocity || []).map(function(item) { return Number(item.completedPoints || 0); });
            if (!completed.length) return null;
            return completed.reduce(function(total, points) { return total + points; }, 0) / completed.length;
          };

          vm.capacityPercent = function() {
            var baseline = vm.capacityBaseline();
            return baseline ? Math.round(vm.planningPoints() / baseline * 100) : null;
          };

          vm.capacityState = function() {
            var percent = vm.capacityPercent();
            if (percent === null) return 'unknown';
            if (percent > 115) return 'over';
            if (percent > 90) return 'near';
            return 'available';
          };

          vm.burndownWidth = function(point) {
            var max = Math.max.apply(null, (vm.burndown || []).map(function(item) {
              return Number(item.remainingPoints || 0);
            }).concat([0]));
            return max ? Math.round(Number(point.remainingPoints || 0) / max * 100) : 0;
          };

          vm.carryoverTargets = function() {
            return (vm.sprints || []).filter(function(sprint) {
              return sprint.status === 'Planned' && (!vm.selectedPlanningSprint || sprint.id !== vm.selectedPlanningSprint.id);
            });
          };

          vm.loadTimeline = function() {
            if (!vm.project || !vm.projectMembership) return $q.when([]);
            if (vm.timelineLoading) return $q.when(vm.timelineEntries || []);
            var projectId = vm.project.id;
            vm.timelineLoading = true;
            vm.timelineError = null;
            function entityAudit(type, id) {
              return id ? apiClient.get('/api/audit/entity/' + type + '/' + id).catch(function() { return []; }) : $q.when([]);
            }
            var requests = [entityAudit('Project', projectId)];
            if (vm.board) requests.push(entityAudit('Board', vm.board.id));
            if (vm.workMode === 'timeline') {
              (vm.sprints || []).forEach(function(sprint) { requests.push(entityAudit('Sprint', sprint.id)); });
              if (vm.selectedTask) requests.push(entityAudit('WorkItem', vm.selectedTask.id));
            }
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
              }).catch(function() {
                vm.timelineEntries = [];
                vm.timelineError = 'Proje etkinliği yüklenemedi.';
                return [];
              }).finally(function() {
                if (vm.project && vm.project.id === projectId) vm.timelineLoading = false;
              });
          };

          vm.loadReports = function(projectId) {
            if (!vm.project) return $q.when();
            projectId = projectId || vm.project.id;
            vm.planningLoading = true;
            vm.planningError = null;
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
                .then(assignIfCurrent(function(data) {
                  vm.sprints = data.items || data;
                  vm.sprintNextCursor = data.nextCursor || null;
                })),
              apiClient.get('/api/sprints/projects/' + projectId + '/backlog?pageSize=100')
                .then(assignIfCurrent(function(data) {
                  vm.backlogItems = data.items || data;
                  vm.backlogNextCursor = data.nextCursor || null;
                  vm.backlogItems.forEach(function(item) { apiClient.remember('/api/work-items/' + item.id, item); });
                }))
            ]).then(function() {
              if (!vm.project || vm.project.id !== projectId) return;
              vm.rebuildAdvancedViews();
              return vm.workMode === 'timeline' || vm.workMode === 'overview' ? vm.loadTimeline() : null;
            }).catch(function(error) {
              if (vm.project && vm.project.id === projectId) {
                vm.planningError = apiActionError(error, 'Planlama verileri yüklenemedi.');
              }
              return false;
            }).finally(function() {
              if (vm.project && vm.project.id === projectId) vm.planningLoading = false;
            });
          };

          function appendUnique(target, items) {
            (items || []).forEach(function(item) {
              if (!target.some(function(existing) { return existing.id === item.id; })) target.push(item);
            });
          }

          vm.loadMoreBacklog = function() {
            if (!vm.project || !vm.backlogNextCursor || vm.planningLoading) return $q.when();
            var projectId = vm.project.id;
            vm.planningLoading = true;
            return apiClient.get('/api/sprints/projects/' + projectId + '/backlog?pageSize=100&after=' + encodeURIComponent(vm.backlogNextCursor))
              .then(function(data) {
                if (!vm.project || vm.project.id !== projectId) return;
                appendUnique(vm.backlogItems, data.items || []);
                (data.items || []).forEach(function(item) { apiClient.remember('/api/work-items/' + item.id, item); });
                vm.backlogNextCursor = data.nextCursor || null;
              }).catch(function(error) {
                vm.planningError = apiActionError(error, 'Backlog devam kayıtları yüklenemedi.');
              }).finally(function() { vm.planningLoading = false; });
          };

          vm.loadMoreSprints = function() {
            if (!vm.project || !vm.sprintNextCursor || vm.planningLoading) return $q.when();
            var projectId = vm.project.id;
            vm.planningLoading = true;
            return apiClient.get('/api/sprints/projects/' + projectId + '?pageSize=50&after=' + encodeURIComponent(vm.sprintNextCursor))
              .then(function(data) {
                if (!vm.project || vm.project.id !== projectId) return;
                appendUnique(vm.sprints, data.items || []);
                vm.sprintNextCursor = data.nextCursor || null;
                vm.rebuildAdvancedViews();
              }).catch(function(error) {
                vm.planningError = apiActionError(error, 'Sprint devam kayıtları yüklenemedi.');
              }).finally(function() { vm.planningLoading = false; });
          };

          vm.loadRemainingPlanningTasks = function() {
            if (vm.loading || !vm.hasMoreTasks) return $q.when();
            function next() {
              if (!vm.hasMoreTasks) {
                vm.rebuildAdvancedViews();
                return true;
              }
              return vm.loadMoreTasks().then(next);
            }
            return next();
          };

          function planningErrorMessage(error) {
            var code = error && error.data && error.data.error && error.data.error.code;
            if (code === 'CONCURRENCY_CONFLICT') return 'Çakışma algılandı. Güncel plan yeniden yüklendi; işlemi tekrar deneyin.';
            if (code === 'SPRINT_ACTIVE_EXISTS') return 'Bu projede zaten aktif bir sprint var.';
            if (code === 'SPRINT_PLANNING_CLOSED') return 'Sprint başladığı için kapsamı artık değiştirilemez.';
            return apiActionError(error, 'Sprint işlemi tamamlanamadı.');
          }

          function planningMutation(request, successMessage, rollback) {
            if (vm.sprintBusy) return;
            vm.sprintBusy = true;
            vm.planningError = null;
            return request.then(function(result) {
              if (result && result.id) {
                var sprint = (vm.sprints || []).find(function(item) { return item.id === result.id; });
                if (sprint) angular.extend(sprint, result);
                else vm.sprints.push(result);
                vm.selectedPlanningSprintId = result.id;
                vm.selectPlanningSprint();
              }
              vm.notify('success', successMessage);
              return vm.loadTasks().then(function() { return true; });
            }).catch(function(error) {
              if (rollback) rollback();
              var message = planningErrorMessage(error);
              vm.planningError = message;
              vm.notify('error', message);
              return vm.loadTasks().then(function() {
                vm.planningError = message;
                return false;
              });
            }).finally(function() { vm.sprintBusy = false; });
          }

          vm.createSprint = function() {
            if (!vm.project || !vm.canPlanSprint() || !vm.sprintDraft.name || vm.sprintBusy) return;
            var draft = vm.sprintDraft;
            vm.sprintDraftError = null;
            if (!dateKey(draft.startDate) || !dateKey(draft.endDate) || dateKey(draft.endDate) < dateKey(draft.startDate)) {
              vm.sprintDraftError = 'Bitiş tarihi başlangıç tarihinden önce olamaz.';
              return $q.when(false);
            }
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
            var sprintId = vm.selectedPlanningSprint.id;
            var backlogIndex = vm.backlogItems.indexOf(item);
            var task = (vm.tasks || []).find(function(candidate) { return candidate.id === item.id; });
            var previousSprintId = task && task.sprintId;
            apiClient.remember('/api/work-items/' + item.id, item);
            if (backlogIndex >= 0) vm.backlogItems.splice(backlogIndex, 1);
            if (task) task.sprintId = sprintId;
            vm.selectPlanningSprint();
            return planningMutation(apiClient.put(
              '/api/sprints/' + sprintId + '/items/' + item.id,
              { estimatePoints: item.estimatePoints || 0 }
            ), 'İş sprint kapsamına alındı.', function() {
              if (backlogIndex >= 0 && vm.backlogItems.indexOf(item) < 0) vm.backlogItems.splice(backlogIndex, 0, item);
              if (task) task.sprintId = previousSprintId;
              vm.selectPlanningSprint();
            });
          };

          vm.unplanSprintItem = function(item) {
            if (!item || !vm.canPlanSprint() || !vm.selectedPlanningSprint || vm.selectedPlanningSprint.status !== 'Planned') return;
            var sprintId = vm.selectedPlanningSprint.id;
            var backlogCopy = vm.backlogItems.slice();
            apiClient.remember('/api/work-items/' + item.id, item);
            item.sprintId = null;
            if (!vm.backlogItems.some(function(candidate) { return candidate.id === item.id; })) vm.backlogItems.unshift(item);
            vm.selectPlanningSprint();
            return planningMutation(apiClient.delete(
              '/api/sprints/' + sprintId + '/items/' + item.id
            ), 'İş backlog alanına taşındı.', function() {
              vm.backlogItems = backlogCopy;
              item.sprintId = sprintId;
              vm.selectPlanningSprint();
            });
          };

          vm.handlePlanningItemKey = function(event, item, direction) {
            if (!event.altKey || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) return;
            var wantsPlan = direction === 'plan' && event.key === 'ArrowRight';
            var wantsBacklog = direction === 'unplan' && event.key === 'ArrowLeft';
            if (!wantsPlan && !wantsBacklog) return;
            event.preventDefault();
            if (wantsPlan) vm.planBacklogItem(item);
            else vm.unplanSprintItem(item);
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
