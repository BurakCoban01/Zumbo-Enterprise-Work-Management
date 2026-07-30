(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopBoardExcellenceFeature', function($q, apiClient) {
      var priorities = { Critical: 0, High: 1, Medium: 2, Low: 3 };

      function readPreferences(storage) {
        var defaults = {
          density: 'comfortable',
          sort: 'rank',
          direction: 'asc',
          columns: { status: true, priority: true, assignee: true, dueDate: true, estimate: false }
        };
        try {
          var stored = JSON.parse(storage.getItem('zumbo.listPreferences') || '{}');
          return angular.extend({}, defaults, stored, {
            columns: angular.extend({}, defaults.columns, stored.columns || {})
          });
        } catch (_) {
          return defaults;
        }
      }

      function dateValue(value) {
        if (!value) return null;
        return value instanceof Date ? value : new Date(value);
      }

      function compare(left, right, field, userName) {
        var leftValue;
        var rightValue;
        if (field === 'priority') {
          leftValue = priorities[left.priority] == null ? 99 : priorities[left.priority];
          rightValue = priorities[right.priority] == null ? 99 : priorities[right.priority];
        } else if (field === 'assignee') {
          leftValue = userName(left.assigneeUserId);
          rightValue = userName(right.assigneeUserId);
        } else if (field === 'dueDate') {
          leftValue = left.dueDate ? new Date(left.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
          rightValue = right.dueDate ? new Date(right.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
        } else {
          leftValue = left[field] == null ? '' : left[field];
          rightValue = right[field] == null ? '' : right[field];
        }
        if (typeof leftValue === 'string') return leftValue.localeCompare(rightValue, 'tr-TR');
        return leftValue === rightValue ? 0 : leftValue < rightValue ? -1 : 1;
      }

      function bulkError(result) {
        var messages = {
          BOARD_WIP_LIMIT_EXCEEDED: 'Hedef kolonun WIP limiti dolu.',
          WIP_LIMIT_EXCEEDED: 'Hedef kolonun WIP limiti dolu.',
          WORKFLOW_TRANSITION_FORBIDDEN: 'Workflow bu geçişe izin vermiyor.',
          CONCURRENCY_CONFLICT: 'Kayıt başka bir kullanıcı tarafından değiştirildi.',
          FORBIDDEN: 'Bu işlem için yetkiniz yok.',
          RESOURCE_BUSY: 'Kayıt şu anda başka bir işlemde.'
        };
        return messages[result.errorCode] || result.errorMessage || 'İşlem tamamlanamadı.';
      }

      return {
        install: function(vm, helpers) {
          var storage = helpers.storage;
          var apiActionError = helpers.apiActionError;
          var originalMove = vm.moveTaskToColumn;
          var originalDropBefore = vm.dropTaskBefore;
          var originalRefreshBoardModel = vm.refreshBoardModel;
          vm.listPreferences = readPreferences(storage);
          vm.listTasks = [];
          vm.listEditTaskId = null;
          vm.listEditDraft = null;
          vm.listEditError = null;
          vm.bulkBusy = false;
          vm.bulkResult = null;

          vm.canEditWorkItems = function() {
            return !!vm.board && !!vm.projectMembership && vm.projectMembership.role !== 'Viewer';
          };

          vm.listColumnVisible = function(column) {
            return vm.listPreferences.columns[column] !== false;
          };
          vm.toggleListColumn = function(column) {
            vm.listPreferences.columns[column] = !vm.listPreferences.columns[column];
            vm.listColumnMenuOpen = false;
            savePreferences();
          };
          vm.setListDensity = function(density) {
            vm.listPreferences.density = density;
            savePreferences();
          };
          vm.sortListBy = function(field) {
            if (vm.listPreferences.sort === field) {
              vm.listPreferences.direction = vm.listPreferences.direction === 'asc' ? 'desc' : 'asc';
            } else {
              vm.listPreferences.sort = field;
              vm.listPreferences.direction = 'asc';
            }
            savePreferences();
            rebuildListTasks();
          };
          vm.listSortIcon = function(field) {
            if (vm.listPreferences.sort !== field) return 'chevrons-up-down';
            return vm.listPreferences.direction === 'asc' ? 'arrow-up' : 'arrow-down';
          };
          vm.visibleListTasks = function() {
            return vm.listTasks;
          };
          vm.refreshBoardModel = function() {
            var result = originalRefreshBoardModel.apply(vm, arguments);
            rebuildListTasks();
            return result;
          };

          function rebuildListTasks() {
            var field = vm.listPreferences.sort;
            var direction = vm.listPreferences.direction === 'desc' ? -1 : 1;
            vm.listTasks = (vm.tasks || []).filter(function(task) {
              return !vm.priorityFilter || task.priority === vm.priorityFilter;
            }).slice().sort(function(left, right) {
              var result = compare(left, right, field, vm.userName);
              return result ? result * direction : compare(left, right, 'rank', vm.userName);
            });
            return vm.listTasks;
          }

          vm.allVisibleTasksSelected = function() {
            var tasks = vm.listTasks;
            return !!tasks.length && tasks.every(function(task) { return !!vm.selectedTaskIds[task.id]; });
          };
          vm.toggleVisibleTaskSelection = function() {
            var select = !vm.allVisibleTasksSelected();
            vm.listTasks.slice(0, 100).forEach(function(task) {
              vm.selectedTaskIds[task.id] = select;
            });
          };

          vm.beginListEdit = function(task) {
            if (!vm.canEditWorkItems() || vm.pendingTaskIds[task.id]) return;
            apiClient.remember('/api/work-items/' + task.id, task);
            vm.listEditTaskId = task.id;
            vm.listEditDraft = {
              title: task.title,
              priority: task.priority,
              dueDate: dateValue(task.dueDate)
            };
            vm.listEditError = null;
          };
          vm.cancelListEdit = function() {
            vm.listEditTaskId = null;
            vm.listEditDraft = null;
            vm.listEditError = null;
          };
          vm.saveListEdit = function(task) {
            if (!task || vm.listEditTaskId !== task.id || !vm.listEditDraft.title.trim() || vm.pendingTaskIds[task.id]) return;
            var snapshot = angular.copy(task);
            var draft = angular.copy(vm.listEditDraft);
            task.title = draft.title.trim();
            task.priority = draft.priority;
            task.dueDate = draft.dueDate || null;
            rebuildListTasks();
            vm.pendingTaskIds[task.id] = true;
            vm.listEditError = null;
            return apiClient.put('/api/work-items/' + task.id, {
              title: task.title,
              description: task.description || '',
              priority: task.priority,
              dueDate: task.dueDate
            }).then(function(updated) {
              angular.extend(task, updated);
              vm.cancelListEdit();
              vm.notify('success', 'Liste satırı kaydedildi.');
            }).catch(function(error) {
              angular.extend(task, snapshot);
              vm.listEditTaskId = null;
              vm.listEditDraft = null;
              vm.listEditError = apiActionError(error, 'Satır kaydedilemedi; önceki değerler geri yüklendi.');
              vm.notify('error', vm.listEditError);
            }).finally(function() {
              delete vm.pendingTaskIds[task.id];
              vm.refreshBoardModel();
            });
          };

          vm.moveTaskToColumn = function(taskId, column) {
            if (!vm.canEditWorkItems()) return $q.when();
            rememberTaskVersion(taskId);
            return originalMove(taskId, column);
          };
          vm.dropTaskBefore = function(taskId, anchor) {
            if (!vm.canEditWorkItems()) return $q.when();
            rememberTaskVersion(taskId);
            return originalDropBefore(taskId, anchor);
          };
          vm.canMoveTaskDirection = function(task, direction) {
            if (!vm.canEditWorkItems() || !vm.board || vm.pendingTaskIds[task.id]) return false;
            var index = columnIndex(task);
            return index >= 0 && !!vm.board.columns[index + direction];
          };
          vm.moveTaskDirection = function(task, direction) {
            if (!vm.canMoveTaskDirection(task, direction)) return $q.when();
            return vm.moveTaskToColumn(task.id, vm.board.columns[columnIndex(task) + direction]);
          };
          vm.handleTaskKey = function(event, task) {
            if (event.key === 'Enter') { event.preventDefault(); vm.selectTask(task); return; }
            if (event.key === ' ' && !event.altKey && vm.canEditWorkItems()) {
              event.preventDefault();
              vm.toggleTaskSelection(task.id);
              return;
            }
            if (!event.altKey || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) return;
            event.preventDefault();
            vm.moveTaskDirection(task, event.key === 'ArrowLeft' ? -1 : 1);
          };

          vm.bulkMove = function(status) {
            var payload = { workItemIds: vm.selectedIds(), status: status };
            return runBulk(function() { return apiClient.post('/api/work-items/bulk/move', payload); }, payload, 'Taşıma');
          };
          vm.bulkAssignToMe = function() {
            var payload = {
              workItemIds: vm.selectedIds(), assigneeUserId: vm.session.currentUser.id
            };
            return runBulk(function() { return apiClient.post('/api/work-items/bulk/assign', payload); }, payload, 'Atama');
          };
          vm.bulkArchive = function() {
            var payload = { workItemIds: vm.selectedIds() };
            return runBulk(function() { return apiClient.post('/api/work-items/bulk/archive', payload); }, payload, 'Arşivleme', true);
          };
          vm.dismissBulkResult = function() { vm.bulkResult = null; };

          vm.blockerCount = function(task) {
            return (task.relations || []).filter(function(relation) {
              return ['blockedby', 'dependson'].indexOf(String(relation.relationType || '').toLowerCase()) >= 0;
            }).length;
          };
          vm.taskDueState = function(task) {
            if (!task.dueDate || String(task.status).toLowerCase() === 'done') return '';
            var days = (new Date(task.dueDate).getTime() - Date.now()) / 86400000;
            return days < 0 ? 'overdue' : days <= 2 ? 'soon' : '';
          };

          function savePreferences() {
            storage.setItem('zumbo.listPreferences', JSON.stringify(vm.listPreferences));
          }
          function columnIndex(task) {
            return vm.board.columns.findIndex(function(column) {
              return column.id === task.columnId || column.name === task.status;
            });
          }
          function rememberTaskVersion(taskId) {
            var task = (vm.tasks || []).find(function(item) { return item.id === taskId; });
            if (task) apiClient.remember('/api/work-items/' + task.id, task);
          }
          function runBulk(request, payload, label, archive) {
            if (!vm.canEditWorkItems() || !payload.workItemIds.length || vm.bulkBusy) return $q.when();
            vm.bulkBusy = true;
            vm.bulkResult = null;
            return request().then(function(response) {
              var results = response.results || [];
              var failures = results.filter(function(result) { return !result.success; }).map(function(result) {
                return { id: result.workItemId, title: vm.taskTitle(result.workItemId), message: bulkError(result) };
              });
              var succeeded = response.succeeded == null ? results.length - failures.length : response.succeeded;
              vm.selectedTaskIds = {};
              failures.forEach(function(failure) { vm.selectedTaskIds[failure.id] = true; });
              vm.bulkResult = { label: label, succeeded: succeeded, failed: response.failed == null ? failures.length : response.failed, failures: failures };
              if (archive && vm.selectedTask && results.some(function(result) {
                return result.success && result.workItemId === vm.selectedTask.id;
              })) vm.selectedTask = null;
              vm.notify(failures.length ? 'error' : 'success', failures.length
                ? label + ' kısmen tamamlandı; başarısız işler seçili bırakıldı.'
                : label + ' tamamlandı.');
              return vm.loadTasks();
            }).catch(function(error) {
              vm.bulkResult = { label: label, succeeded: 0, failed: payload.workItemIds.length, failures: [] };
              vm.notify('error', apiActionError(error, label + ' başlatılamadı.'));
            }).finally(function() { vm.bulkBusy = false; });
          }

          rebuildListTasks();
        }
      };
    });
})();
