(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('TasksController', function($scope, $state, $ionicPopup, $ionicScrollDelegate, $q, $timeout, zumboApi, sessionStore, realtimeService, apiClient, mobileActionError) {
    var vm = this;
    vm.mode = sessionStore.state.taskMode || 'my';
    delete sessionStore.state.taskMode;
    vm.status = '';
    vm.tasks = [];
    vm.backlogItems = [];
    vm.boards = [];
    vm.sprints = [];
    vm.boardLanes = [];
    vm.sprintItems = [];
    vm.burndown = [];
    vm.selectedBoardId = '';
    vm.selectedSprintId = '';
    vm.backlogNextCursor = null;
    vm.sprintNextCursor = null;
    vm.planningBusy = false;
    vm.planningError = null;
    vm.loading = false;
    vm.loadError = null;
    vm.projectMissing = false;
    vm.page = 1;
    vm.pageSize = 50;
    vm.hasMore = false;
    vm.schema = { issueTypes: [], customFields: [], layouts: [] };
    vm.workflowStatuses = [];
    vm.workflowProjectId = null;
    vm.moveError = null;
    vm.createDraft = { title: '', type: 'Task', priority: 'Medium', customFieldValues: {} };
    vm.customFieldsFor = function(typeKey) {
      var layout = (vm.schema.layouts || []).find(function(item) { return item.issueTypeKey === typeKey; });
      return (layout ? layout.fieldKeys : []).map(function(key) {
        return (vm.schema.customFields || []).find(function(field) { return field.key === key; });
      }).filter(Boolean);
    };
    function dateOnly(value) {
      if (!value) return null;
      if (typeof value === 'string') return value.slice(0, 10);
      return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0');
    }
    function customFieldRequests() {
      return vm.customFieldsFor(vm.createDraft.type).filter(function(field) {
        var value = vm.createDraft.customFieldValues[field.key];
        return value !== undefined && value !== null && value !== '';
      }).map(function(field) {
        var value = vm.createDraft.customFieldValues[field.key];
        var request = { fieldKey: field.key };
        if (field.type === 'Text') request.textValue = value;
        if (field.type === 'Number') request.numberValue = value;
        if (field.type === 'Boolean') request.booleanValue = value;
        if (field.type === 'Date') request.dateValue = dateOnly(value);
        if (field.type === 'Select') request.optionKey = value;
        return request;
      });
    }
    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (change.eventType === 'resyncRequired') {
        if (sessionStore.state.project && change.projectId === sessionStore.state.project.id) vm.load();
        return;
      }
      if (!sessionStore.state.project || change.projectId !== sessionStore.state.project.id) { return; }
      if (vm.mode !== 'my') {
        vm.load();
        return;
      }
      var index = vm.tasks.findIndex(function(task) { return task.id === change.workItemId; });
      var visible = change.eventType !== 'archived'
        && change.workItem.assigneeUserId === sessionStore.state.currentUser.id
        && (!vm.status || change.workItem.status === vm.status);
      if (!visible && index >= 0) { vm.tasks.splice(index, 1); }
      else if (visible && index >= 0) { vm.tasks[index] = change.workItem; }
      else if (visible) { vm.tasks.unshift(change.workItem); }
      vm.tasks.sort(function(left, right) { return (left.rank || 0) - (right.rank || 0); });
    });
    $scope.$on('$destroy', unsubscribeRealtime);
    vm.setMode = function(mode) {
      vm.mode = mode;
      vm.status = '';
      return vm.load().finally(function() {
        $timeout(function() {
          $ionicScrollDelegate.$getByHandle('taskWorkScroll').scrollTop(true);
        });
      });
    };
    vm.handleModeKey = function(event) {
      var modes = ['my', 'backlog', 'sprint', 'board', 'list'];
      var current = modes.indexOf(vm.mode);
      var next = event.key === 'Home' ? 0
        : event.key === 'End' ? modes.length - 1
          : event.key === 'ArrowRight' ? (current + 1) % modes.length
            : event.key === 'ArrowLeft' ? (current - 1 + modes.length) % modes.length
              : -1;
      if (next < 0) return;
      event.preventDefault();
      vm.setMode(modes[next]);
      $timeout(function() {
        var tab = window.document.querySelector('.work-mode-segments [aria-selected="true"]');
        if (tab) tab.focus();
      });
    };
    vm.filter = function(status) { vm.status = status; vm.load(); };
    vm.statusOptions = function() { return vm.workflowStatuses; };
    function loadWorkflowStatuses(projectId) {
      if (vm.workflowProjectId === projectId && vm.workflowStatuses.length) return $q.when(vm.workflowStatuses);
      return zumboApi.workflow(projectId).then(function(workflow) {
        vm.workflowProjectId = projectId;
        vm.workflowStatuses = workflow.statuses || [];
        if (vm.status && !vm.workflowStatuses.some(function(status) { return status.name === vm.status; })) vm.status = '';
        return vm.workflowStatuses;
      });
    }
    function rebuildBoardLanes() {
      var board = vm.boards.find(function(item) { return item.id === vm.selectedBoardId; });
      vm.boardLanes = (board ? board.columns : []).map(function(column) {
        var statuses = column.statusNames && column.statusNames.length ? column.statusNames : [column.name];
        return {
          id: column.id,
          name: column.name,
          wipLimit: column.wipLimit,
          tasks: vm.tasks.filter(function(task) { return statuses.indexOf(task.status) >= 0; })
        };
      });
    }
    function rebuildSprintItems() {
      vm.sprintItems = vm.tasks.filter(function(task) { return task.sprintId === vm.selectedSprintId; });
    }
    vm.selectBoard = function() {
      var board = vm.boards.find(function(item) { return item.id === vm.selectedBoardId; });
      if (board) sessionStore.state.board = board;
      rebuildBoardLanes();
    };
    vm.selectedSprint = function() {
      return vm.sprints.find(function(item) { return item.id === vm.selectedSprintId; }) || null;
    };
    vm.planningPoints = function() {
      return vm.sprintItems.reduce(function(total, item) { return total + Number(item.estimatePoints || 0); }, 0);
    };
    vm.selectSprint = function() {
      rebuildSprintItems();
      vm.burndown = [];
      if (!vm.selectedSprintId) return $q.when([]);
      return zumboApi.sprintBurndown(vm.selectedSprintId).then(function(points) {
        vm.burndown = points || [];
        return vm.burndown;
      }).catch(function() { return []; });
    };
    vm.canEditTasks = function() {
      var project = sessionStore.state.project;
      var currentUser = sessionStore.state.currentUser;
      var membership = project && currentUser && (project.members || []).find(function(member) {
        return member.userId === currentUser.id;
      });
      return !!membership && membership.role !== 'Viewer';
    };
    vm.plannedSprints = function() {
      return vm.sprints.filter(function(sprint) { return sprint.status === 'Planned'; });
    };
    vm.carryoverTargets = function() {
      return vm.sprints.filter(function(sprint) {
        return sprint.status === 'Planned' && sprint.id !== vm.selectedSprintId;
      });
    };
    function planningError(error) {
      var code = error && error.data && error.data.error && error.data.error.code;
      if (code === 'SPRINT_ACTIVE_EXISTS') return 'Bu projede zaten aktif bir sprint var.';
      if (code === 'SPRINT_PLANNING_CLOSED') return 'Sprint başladığı için kapsam değiştirilemez.';
      if (code === 'CONCURRENCY_CONFLICT') return 'Plan başka bir kullanıcı tarafından değiştirildi. Güncel plan yüklendi.';
      return mobileActionError(error, 'Sprint işlemi tamamlanamadı.');
    }
    function runPlanning(request, successMessage, rollback) {
      if (vm.planningBusy) return $q.when(false);
      vm.planningBusy = true;
      vm.planningError = null;
      return request.then(function(result) {
        if (result && result.id) {
          var sprint = vm.sprints.find(function(item) { return item.id === result.id; });
          if (sprint) angular.extend(sprint, result);
          else vm.sprints.push(result);
          vm.selectedSprintId = result.id;
          vm.selectSprint();
        }
        vm.planningSuccess = successMessage;
        return vm.load().then(function() { return true; });
      }).catch(function(error) {
        if (rollback) rollback();
        vm.planningError = planningError(error);
        return vm.load().then(function() { return false; });
      }).finally(function() { vm.planningBusy = false; });
    }
    vm.openCreateSprint = function() {
      if (!vm.canEditTasks() || vm.planningBusy) return;
      var start = new Date();
      vm.sprintDraft = {
        name: '', goal: '', startDate: start,
        endDate: new Date(start.getFullYear(), start.getMonth(), start.getDate() + 13)
      };
      vm.sprintDraftError = null;
      return $ionicPopup.show({
        title: 'Sprint oluştur',
        templateUrl: 'templates/create-sprint.html',
        scope: $scope,
        buttons: [
          { text: 'İptal' },
          {
            text: 'Oluştur', type: 'button-positive',
            onTap: function(event) {
              var startDate = dateOnly(vm.sprintDraft.startDate);
              var endDate = dateOnly(vm.sprintDraft.endDate);
              if (!vm.sprintDraft.name || !startDate || !endDate || endDate < startDate) {
                event.preventDefault();
                vm.sprintDraftError = 'Ad ve geçerli tarih aralığı zorunludur.';
                return null;
              }
              return {
                name: vm.sprintDraft.name,
                goal: vm.sprintDraft.goal,
                startDate: startDate,
                endDate: endDate
              };
            }
          }
        ]
      }).then(function(draft) {
        if (!draft) return false;
        var project = sessionStore.state.project;
        return runPlanning(zumboApi.createSprint(project.id, draft), 'Sprint oluşturuldu.');
      });
    };
    vm.planBacklogItem = function(item) {
      var sprint = vm.selectedSprint();
      if (!item || !vm.canEditTasks() || !sprint || sprint.status !== 'Planned') return $q.when(false);
      var index = vm.backlogItems.indexOf(item);
      if (index >= 0) vm.backlogItems.splice(index, 1);
      return runPlanning(zumboApi.planSprintItem(sprint.id, item), 'İş sprint kapsamına alındı.', function() {
        if (index >= 0 && vm.backlogItems.indexOf(item) < 0) vm.backlogItems.splice(index, 0, item);
      });
    };
    vm.unplanSprintItem = function(item) {
      var sprint = vm.selectedSprint();
      if (!item || !vm.canEditTasks() || !sprint || sprint.status !== 'Planned') return $q.when(false);
      var previousSprintId = item.sprintId;
      item.sprintId = null;
      rebuildSprintItems();
      return runPlanning(zumboApi.unplanSprintItem(sprint.id, item), 'İş backlog alanına taşındı.', function() {
        item.sprintId = previousSprintId;
        rebuildSprintItems();
      });
    };
    vm.startSprint = function() {
      var sprint = vm.selectedSprint();
      if (!vm.canEditTasks() || !sprint || sprint.status !== 'Planned') return;
      return $ionicPopup.confirm({
        title: 'Sprint başlatılsın mı?',
        template: '<p class="mobile-confirm-copy">Kapsam başlangıç anında sabitlenir ve burndown takibi başlar.</p>',
        cancelText: 'Vazgeç', okText: 'Başlat', okType: 'button-positive'
      }).then(function(confirmed) {
        if (!confirmed) return false;
        return runPlanning(zumboApi.startSprint(sprint.id), 'Sprint başlatıldı.');
      });
    };
    vm.completeSprint = function() {
      var sprint = vm.selectedSprint();
      if (!vm.canEditTasks() || !sprint || sprint.status !== 'Active') return;
      vm.carryoverSprintId = '';
      return $ionicPopup.show({
        title: 'Sprint tamamla',
        templateUrl: 'templates/complete-sprint.html',
        scope: $scope,
        buttons: [
          { text: 'Vazgeç' },
          { text: 'Tamamla', type: 'button-positive', onTap: function() { return vm.carryoverSprintId; } }
        ]
      }).then(function(carryoverSprintId) {
        if (carryoverSprintId === undefined) return false;
        return runPlanning(zumboApi.completeSprint(sprint.id, carryoverSprintId), 'Sprint tamamlandı.');
      });
    };
    vm.loadMoreBacklog = function() {
      var project = sessionStore.state.project;
      if (!project || !vm.backlogNextCursor || vm.planningBusy) return;
      vm.planningBusy = true;
      return zumboApi.backlog(project.id, vm.backlogNextCursor).then(function(data) {
        (data.items || []).forEach(function(item) {
          if (!vm.backlogItems.some(function(existing) { return existing.id === item.id; })) vm.backlogItems.push(item);
        });
        vm.backlogNextCursor = data.nextCursor || null;
      }).catch(function(error) {
        vm.planningError = planningError(error);
      }).finally(function() { vm.planningBusy = false; });
    };
    vm.canMoveTask = function(task, direction) {
      var board = vm.boards.find(function(item) { return item.id === vm.selectedBoardId; });
      if (!vm.canEditTasks() || !board || vm.movingTaskId === task.id) return false;
      var index = board.columns.findIndex(function(column) {
        var statuses = column.statusNames && column.statusNames.length ? column.statusNames : [column.name];
        return column.id === task.columnId || statuses.indexOf(task.status) >= 0;
      });
      return index >= 0 && !!board.columns[index + direction];
    };
    vm.moveTask = function(task, direction) {
      if (!vm.canMoveTask(task, direction)) return $q.when();
      var board = vm.boards.find(function(item) { return item.id === vm.selectedBoardId; });
      var current = board.columns.findIndex(function(column) {
        var statuses = column.statusNames && column.statusNames.length ? column.statusNames : [column.name];
        return column.id === task.columnId || statuses.indexOf(task.status) >= 0;
      });
      var target = board.columns[current + direction];
      var previous = { status: task.status, columnId: task.columnId };
      task.status = target.name;
      task.columnId = target.id;
      vm.movingTaskId = task.id;
      vm.moveError = null;
      rebuildBoardLanes();
      return zumboApi.moveTask(task.id, target.name).then(vm.load).catch(function(error) {
        task.status = previous.status;
        task.columnId = previous.columnId;
        rebuildBoardLanes();
        var code = error.data && error.data.error && error.data.error.code;
        vm.moveError = code === 'BOARD_WIP_LIMIT_EXCEEDED' || code === 'WIP_LIMIT_EXCEEDED'
          ? 'Kolonun WIP limiti dolu; görev önceki kolonuna alındı.'
          : 'Görev taşınamadı; önceki kolonuna alındı.';
      }).finally(function() { vm.movingTaskId = null; });
    };
    vm.load = function(page, append) {
      page = Number.isInteger(page) && page > 0 ? page : 1;
      append = append === true;
      vm.loading = !append;
      vm.loadError = null;
      vm.projectMissing = false;
      var projectPromise = sessionStore.state.project
        ? $q.when(sessionStore.state.project)
        : zumboApi.projects().then(function(projects) {
            sessionStore.state.project = projects[0] || null;
            return sessionStore.state.project;
          });
      return projectPromise.then(function(project) {
        if (!project) {
          vm.tasks = [];
          vm.projectMissing = true;
          return [];
        }
        apiClient.transitionContext('project:' + project.id);
        return $q.all([
          realtimeService.connect(project.id).catch(angular.noop),
          loadWorkflowStatuses(project.id)
        ]).then(function() {
          if (vm.mode === 'backlog') {
            return $q.all([zumboApi.backlog(project.id), zumboApi.sprints(project.id)]).then(function(result) {
              vm.backlogItems = result[0].items || [];
              vm.backlogNextCursor = result[0].nextCursor || null;
              vm.sprints = result[1].items || [];
              vm.sprintNextCursor = result[1].nextCursor || null;
              if (!vm.selectedSprintId || !vm.sprints.some(function(sprint) { return sprint.id === vm.selectedSprintId; })) {
                var planned = vm.sprints.find(function(sprint) { return sprint.status === 'Planned'; });
                vm.selectedSprintId = planned ? planned.id : '';
              }
              vm.tasks = [];
              vm.hasMore = false;
              return vm.backlogItems;
            });
          }
          if (vm.mode !== 'my') {
            return $q.all([
              zumboApi.projectTasks(project.id, vm.status, 1, 100),
              zumboApi.boards(project.id),
              zumboApi.sprints(project.id)
            ]).then(function(result) {
              vm.tasks = result[0].items || [];
              vm.searchDegraded = result[0].degraded === true;
              vm.boards = result[1];
              vm.sprints = result[2].items || [];
              vm.sprintNextCursor = result[2].nextCursor || null;
              if (!vm.selectedBoardId || !vm.boards.some(function(board) { return board.id === vm.selectedBoardId; })) {
                vm.selectedBoardId = (sessionStore.state.board && sessionStore.state.board.id) || (vm.boards[0] && vm.boards[0].id) || '';
              }
              if (!vm.selectedSprintId || !vm.sprints.some(function(sprint) { return sprint.id === vm.selectedSprintId; })) {
                var active = vm.sprints.find(function(sprint) { return sprint.status === 'Active'; })
                  || vm.sprints.find(function(sprint) { return sprint.status === 'Planned'; })
                  || vm.sprints[0];
                vm.selectedSprintId = active ? active.id : '';
              }
              vm.selectBoard();
              vm.selectSprint();
              vm.hasMore = false;
              realtimeService.synchronize(vm.tasks);
              return vm.tasks;
            });
          }
          return zumboApi.tasks(project.id, vm.status, page, vm.pageSize).then(function(data) {
            var items = data.items || [];
            vm.searchDegraded = data.degraded === true;
            vm.page = page;
            vm.hasMore = items.length === vm.pageSize;
            vm.tasks = append ? vm.tasks.concat(items.filter(function(task) {
              return !vm.tasks.some(function(existing) { return existing.id === task.id; });
            })) : items;
            realtimeService.synchronize(vm.tasks);
            return items;
          });
        });
      }).catch(function(error) {
        vm.loadError = mobileActionError(error, 'İşler yüklenemedi.');
        return [];
      }).finally(function() {
        vm.loading = false;
      });
    };
    vm.loadMore = function() {
      if (!vm.hasMore) {
        $scope.$broadcast('scroll.infiniteScrollComplete');
        return;
      }
      vm.load(vm.page + 1, true).finally(function() {
        $scope.$broadcast('scroll.infiniteScrollComplete');
      });
    };
    vm.openTask = function(task) {
      $state.go('task-detail', { taskId: task.id });
    };
    vm.quickAdd = function() {
      var project = sessionStore.state.project;
      if (!project) {
        $ionicPopup.alert({ title: 'Önce proje seçin' });
        return;
      }
      $q.all([zumboApi.boards(project.id), zumboApi.workItemSchema(project.id)]).then(function(result) {
        vm.schema = result[1];
        var types = vm.schema.issueTypes.filter(function(type) { return type.active; });
        var taskType = types.find(function(type) { return type.key === 'Task'; });
        vm.createDraft = {
          title: '',
          type: (taskType || types[0] || { key: 'Task' }).key,
          priority: 'Medium',
          customFieldValues: {}
        };
        if (result[0].length) return result[0][0];
        return zumboApi.createBoard(project.id);
      }).then(function(board) {
        sessionStore.state.board = board;
        return $ionicPopup.show({
          title: 'Yeni iş',
          templateUrl: 'templates/create-task.html',
          scope: $scope,
          buttons: [
            { text: 'İptal' },
            {
              text: 'Oluştur',
              type: 'button-positive',
              onTap: function(event) {
                var missingRequired = vm.customFieldsFor(vm.createDraft.type).some(function(field) {
                  var value = vm.createDraft.customFieldValues[field.key];
                  return field.required && (value === undefined || value === null || value === '');
                });
                if (!vm.createDraft.title || missingRequired) {
                  event.preventDefault();
                  vm.createError = 'Zorunlu alanları tamamlayın.';
                  return null;
                }
                return vm.createDraft;
              }
            }
          ]
        }).then(function(draft) {
          if (!draft) return null;
          draft.customFields = customFieldRequests();
          return zumboApi.createTask(project.id, board.id, draft);
        });
      }).then(vm.load, function(error) {
        vm.createError = mobileActionError(error, 'Görev oluşturulamadı.');
        return $ionicPopup.alert({
          title: 'Görev oluşturulamadı',
          template: 'İşlem tamamlanamadı. Lütfen yeniden deneyin.'
        });
      });
    };
    $scope.$on('$ionicView.afterEnter', function() {
      if (!sessionStore.state.openCreateTask) return;
      delete sessionStore.state.openCreateTask;
      vm.load().then(vm.quickAdd);
    });
    vm.load();
  });
})();
