(function() {
  'use strict';

  angular.module('zumboMobile')
    .controller('ProjectPlanningController', function($state, $stateParams, $q, $window, zumboApi, sessionStore, apiClient, mobileActionError, mobilePwaService) {
      var vm = this;
      var core = $window.ZumboPlanningCore;
      var projectId = $stateParams.projectId;
      var modes = ['calendar', 'timeline', 'roadmap'];
      var zooms = ['week', 'month', 'quarter'];

      apiClient.transitionContext('project:' + projectId);
      vm.mode = modes.indexOf($stateParams.mode) >= 0 ? $stateParams.mode : 'calendar';
      vm.zoom = zooms.indexOf($stateParams.zoom) >= 0 ? $stateParams.zoom : 'month';
      vm.anchor = /^\d{4}-\d{2}-\d{2}$/.test($stateParams.anchor || '') ? core.inputDate($stateParams.anchor) : new Date();
      vm.filters = { query: $stateParams.query || '', type: $stateParams.type || '' };
      vm.project = sessionStore.state.project && sessionStore.state.project.id === projectId ? sessionStore.state.project : null;
      vm.tasks = [];
      vm.sprints = [];
      vm.model = core.buildModel({});
      vm.scopeComplete = false;
      vm.loading = true;
      vm.error = null;
      vm.notice = null;
      vm.mutationTaskId = null;
      vm.unscheduledOpen = false;
      vm.pwa = mobilePwaService.state;
      vm.timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

      vm.setMode = function(mode) {
        if (modes.indexOf(mode) < 0) return;
        vm.mode = mode;
        rebuild();
        syncRoute();
      };

      vm.setZoom = function(zoom) {
        if (zooms.indexOf(zoom) < 0) return;
        vm.zoom = zoom;
        rebuild();
        syncRoute();
      };

      vm.applyFilters = function() {
        rebuild();
        syncRoute();
      };

      vm.shift = function(direction) {
        var days = vm.mode === 'calendar' ? 28 : vm.zoom === 'quarter' ? 90 : vm.zoom === 'month' ? 28 : 14;
        vm.anchor = core.inputDate(core.addDays(core.dateKey(vm.anchor), days * direction));
        rebuild();
        syncRoute();
      };

      vm.today = function() {
        vm.anchor = new Date();
        rebuild();
        syncRoute();
      };

      vm.dateLabel = function(key) {
        return core.formatDate(key, 'tr-TR');
      };

      vm.windowLabel = function() {
        if (vm.mode === 'calendar') {
          var days = vm.model.calendarDays || [];
          if (days.length) return vm.dateLabel(days[0].key) + ' - ' + vm.dateLabel(days[days.length - 1].key);
        }
        return vm.dateLabel(vm.model.window.startKey) + ' - ' + vm.dateLabel(vm.model.window.endKey);
      };

      vm.calendarEvents = function() {
        var days = vm.model.calendarDays || [];
        if (!days.length) return [];
        return (vm.model.calendarEvents || []).filter(function(event) {
          return event.key >= days[0].key && event.key <= days[days.length - 1].key;
        });
      };

      vm.typeOptions = function() {
        return Array.from(new Set(vm.tasks.map(function(task) { return task.type; }).filter(Boolean))).sort();
      };

      vm.canEdit = function() {
        var currentUser = sessionStore.state.currentUser;
        var membership = vm.project && currentUser && (vm.project.members || []).find(function(member) {
          return member.userId === currentUser.id;
        });
        return !!membership && membership.role !== 'Viewer' && !vm.pwa.offline;
      };

      vm.openTask = function(task) {
        if (task) $state.go('task-detail', { taskId: task.id });
      };

      vm.reschedule = function(task, value) {
        if (!task || !vm.canEdit() || vm.mutationTaskId) return $q.when(false);
        var key = core.dateKey(value, null, true);
        if (!key) return $q.when(false);
        var previous = task.dueDate;
        apiClient.remember('/api/work-items/' + task.id, task);
        task.dueDate = key + 'T00:00:00.000Z';
        vm.mutationTaskId = task.id;
        vm.error = null;
        rebuild();
        return zumboApi.updateTask(task.id, {
          title: task.title,
          description: task.description || '',
          priority: task.priority,
          dueDate: task.dueDate
        }).then(function(updated) {
          angular.extend(task, updated);
          vm.notice = 'Bitiş tarihi ' + vm.dateLabel(key) + ' olarak kaydedildi.';
          rebuild();
          return true;
        }).catch(function(error) {
          task.dueDate = previous;
          vm.error = mobileActionError(error, 'Tarih kaydedilemedi; önceki değer geri yüklendi.');
          rebuild();
          return vm.load().then(function() { return false; });
        }).finally(function() {
          vm.mutationTaskId = null;
        });
      };

      vm.load = function() {
        vm.loading = true;
        vm.error = null;
        vm.scopeComplete = false;
        return zumboApi.project(projectId).then(function(project) {
          vm.project = project;
          sessionStore.state.project = project;
          return $q.all([settled(loadTaskPages(1, [])), settled(loadSprintPages(null, []))]);
        }).then(function(result) {
          if (result[0].ok) vm.tasks = result[0].value;
          if (result[1].ok) vm.sprints = result[1].value;
          vm.scopeComplete = result[0].ok && result[1].ok;
          if (!vm.scopeComplete) vm.error = 'Planın tüm kayıtları yüklenemedi. Kullanılabilen kapsam gösteriliyor.';
          rebuild();
        }).catch(function(error) {
          vm.error = mobileActionError(error, 'Proje planı yüklenemedi.');
          rebuild();
        }).finally(function() {
          vm.loading = false;
        });
      };

      function loadTaskPages(page, items) {
        return zumboApi.projectTasks(projectId, '', page, 100).then(function(result) {
          var next = items.concat((result.items || []).filter(function(task) {
            return !items.some(function(existing) { return existing.id === task.id; });
          }));
          return page * 100 < Number(result.totalCount || next.length) ? loadTaskPages(page + 1, next) : next;
        });
      }

      function loadSprintPages(cursor, items) {
        return zumboApi.sprints(projectId, cursor).then(function(result) {
          var next = items.concat((result.items || []).filter(function(sprint) {
            return !items.some(function(existing) { return existing.id === sprint.id; });
          }));
          return result.nextCursor ? loadSprintPages(result.nextCursor, next) : next;
        });
      }

      function settled(promise) {
        return promise.then(function(value) { return { ok: true, value: value }; }, function(error) { return { ok: false, error: error }; });
      }

      function rebuild() {
        vm.model = core.buildModel({
          tasks: vm.tasks,
          sprints: vm.sprints,
          project: vm.project || {},
          filters: vm.filters,
          anchorDate: vm.anchor,
          calendarMode: 'month',
          zoom: vm.zoom,
          timeZone: vm.timeZone
        });
      }

      function syncRoute() {
        $state.go('project-planning', {
          projectId: projectId,
          mode: vm.mode,
          zoom: vm.zoom,
          anchor: core.dateKey(vm.anchor),
          query: vm.filters.query || null,
          type: vm.filters.type || null
        }, { notify: false, location: 'replace' });
      }

      vm.load();
    });
})();
