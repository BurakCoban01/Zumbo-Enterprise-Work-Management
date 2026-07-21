(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopBoardViewFeature', function(apiClient) {
      return {
        install: function(vm, helpers) {
          var setBoardState = helpers.setBoardState;
          var apiActionError = helpers.apiActionError;
    vm.selectView = function() {
      var views = (vm.board && vm.board.views) || [];
      vm.activeView = views.find(function(view) { return view.id === vm.activeViewId; }) || null;
      if (vm.activeView) {
        vm.swimlaneMode = vm.activeView.swimlaneMode;
        vm.priorityFilter = (vm.activeView.filter.priorities || [])[0] || '';
        vm.search = vm.activeView.filter.text || '';
        vm.viewDraftName = vm.activeView.name;
      } else {
        vm.viewDraftName = '';
      }
      vm.refreshBoardModel();
    };

    vm.updateSwimlane = function() {
      if (!vm.board) return;
      return apiClient.patch('/api/boards/' + vm.board.id + '/swimlane', { mode: vm.swimlaneMode })
        .then(function(board) {
          vm.board = board;
          vm.activeViewId = '';
          vm.activeView = null;
          vm.refreshBoardModel();
          return vm.loadBoardAudit();
        });
    };

    vm.saveCurrentView = function() {
      if (!vm.board || !vm.viewDraftName) return;
      var payload = {
        name: vm.viewDraftName,
        isShared: vm.activeView ? vm.activeView.isShared : false,
        swimlaneMode: vm.swimlaneMode || 'None',
        filter: {
          assigneeUserId: null,
          teamId: null,
          statuses: [],
          priorities: vm.priorityFilter ? [vm.priorityFilter] : [],
          labels: [],
          text: vm.search || null
        }
      };
      var request = vm.activeView
        ? apiClient.put('/api/boards/' + vm.board.id + '/views/' + vm.activeView.id, payload)
        : apiClient.post('/api/boards/' + vm.board.id + '/views', payload);
      return request.then(function(board) {
        vm.board = board;
        var created = board.views.find(function(view) { return view.name === vm.viewDraftName; });
        vm.activeViewId = created ? created.id : '';
        vm.selectView();
        vm.notify('success', 'Kayıtlı görünüm kaydedildi.');
        return vm.loadBoardAudit();
      });
    };

    vm.deleteCurrentView = function() {
      if (!vm.board || !vm.activeView || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/boards/' + vm.board.id + '/views/' + vm.activeView.id)
        .then(function(board) {
          setBoardState(board);
          vm.activeViewId = '';
          vm.activeView = null;
          vm.viewDraftName = '';
          vm.notify('success', 'Kayıtlı görünüm silindi.');
          return vm.loadBoardAudit();
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Kayıtlı görünüm silinemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.refreshBoardModel = function() {
      if (!vm.board) {
        vm.boardRows = [];
        return;
      }

      var view = vm.activeView;
      var filter = view ? view.filter : null;
      var tasks = (vm.tasks || []).filter(function(task) {
        if (vm.priorityFilter && task.priority !== vm.priorityFilter) return false;
        if (!filter) return true;
        if (filter.assigneeUserId && task.assigneeUserId !== filter.assigneeUserId) return false;
        if (filter.teamId && task.teamId !== filter.teamId) return false;
        if (filter.statuses.length && filter.statuses.indexOf(task.status) < 0) return false;
        if (filter.priorities.length && filter.priorities.indexOf(task.priority) < 0) return false;
        if (filter.labels.length && !filter.labels.some(function(label) { return task.labels.indexOf(label) >= 0; })) return false;
        if (filter.text) {
          var haystack = [task.title, task.description, (task.labels || []).join(' ')].join(' ').toLowerCase();
          if (haystack.indexOf(filter.text.toLowerCase()) < 0) return false;
        }
        return true;
      }).sort(function(left, right) { return (left.rank || 0) - (right.rank || 0); });
      var mode = (view && view.swimlaneMode) || vm.swimlaneMode || vm.board.swimlaneMode || 'None';
      var groups = {};
      tasks.forEach(function(task) {
        var key = swimlaneKey(task, mode);
        groups[key] = groups[key] || [];
        groups[key].push(task);
      });
      if (!tasks.length || mode === 'None') groups['Tüm işler'] = tasks;
      vm.boardRows = Object.keys(groups).sort().map(function(label) {
        return {
          label: label,
          columns: vm.board.columns.map(function(column) {
            var columnTasks = groups[label].filter(function(task) { return task.columnId === column.id || task.status === column.name; });
            return {
              id: column.id,
              name: column.name,
              wipLimit: column.wipLimit,
              tasks: columnTasks,
              count: columnTasks.length,
              atWipLimit: !!column.wipLimit && columnTasks.length >= column.wipLimit,
              collapsed: !!vm.collapsedColumns[vm.board.id + ':' + column.id]
            };
          })
        };
      });
    };

    function swimlaneKey(task, mode) {
      if (mode === 'Assignee') return task.assigneeUserId || 'Atanmamış';
      if (mode === 'Priority') return task.priority || 'Öncelik yok';
      if (mode === 'Team') return task.teamId || 'Takım yok';
      if (mode === 'Epic') return task.parentId || 'Epic yok';
      return 'Tüm işler';
    }

    function nextStatusFor(status) {
      if (status === 'To Do') return 'In Progress';
      if (status === 'In Progress') return 'Code Review';
      if (status === 'Code Review') return 'Test';
      return 'Done';
    }

          return { nextStatusFor: nextStatusFor };
        }
      };
    });
})();
