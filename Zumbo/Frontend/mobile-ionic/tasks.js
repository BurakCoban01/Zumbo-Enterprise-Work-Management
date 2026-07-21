(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('TasksController', function($scope, $state, $ionicPopup, $q, $timeout, zumboApi, sessionStore, realtimeService, apiClient) {
    var vm = this;
    vm.mode = 'my';
    vm.status = '';
    vm.tasks = [];
    vm.backlogItems = [];
    vm.boards = [];
    vm.sprints = [];
    vm.boardLanes = [];
    vm.sprintItems = [];
    vm.selectedBoardId = '';
    vm.selectedSprintId = '';
    vm.page = 1;
    vm.pageSize = 50;
    vm.hasMore = false;
    vm.schema = { issueTypes: [], customFields: [], layouts: [] };
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
      vm.load();
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
    function rebuildBoardLanes() {
      var board = vm.boards.find(function(item) { return item.id === vm.selectedBoardId; });
      vm.boardLanes = (board ? board.columns : []).map(function(column) {
        var statuses = column.statusNames && column.statusNames.length ? column.statusNames : [column.name];
        return {
          id: column.id,
          name: column.name,
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
    vm.selectSprint = rebuildSprintItems;
    vm.load = function(page, append) {
      page = Number.isInteger(page) && page > 0 ? page : 1;
      append = append === true;
      var projectPromise = sessionStore.state.project
        ? $q.when(sessionStore.state.project)
        : zumboApi.projects().then(function(projects) {
            sessionStore.state.project = projects[0] || null;
            return sessionStore.state.project;
          });
      return projectPromise.then(function(project) {
        if (!project) {
          vm.tasks = [];
          return [];
        }
        apiClient.transitionContext('project:' + project.id);
        return realtimeService.connect(project.id).catch(angular.noop).then(function() {
          if (vm.mode === 'backlog') {
            return zumboApi.backlog(project.id).then(function(data) {
              vm.backlogItems = data.items || [];
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
              rebuildSprintItems();
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
      }).then(vm.load);
    };
    vm.load();
  });
})();
