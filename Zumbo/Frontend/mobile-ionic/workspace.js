(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('DashboardController', function($scope, $state, $ionicScrollDelegate, $q, zumboApi, sessionStore, realtimeService, apiClient) {
    var vm = this;
    vm.summary = {};
    vm.tasks = [];
    vm.visibleTaskItems = [];
    vm.mode = 'assigned';
    vm.searchDegraded = false;
    function isDone(task) {
      return task.completedAt || ['done', 'completed', 'tamamlandı'].indexOf(String(task.status || '').toLocaleLowerCase('tr-TR')) >= 0;
    }
    function isBlocked(task) {
      if (String(task.status || '').toLowerCase().indexOf('block') >= 0) return true;
      return (task.relations || []).some(function(relation) {
        return ['blockedby', 'dependson'].indexOf(String(relation.relationType || '').toLowerCase()) >= 0;
      });
    }
    vm.setMode = function(mode) {
      vm.mode = mode;
      rebuildVisibleTasks();
    };
    vm.visibleTasks = function() {
      return vm.visibleTaskItems;
    };
    function rebuildVisibleTasks() {
      var open = vm.tasks.filter(function(task) { return !isDone(task); });
      if (vm.mode === 'due') vm.visibleTaskItems = open.filter(function(task) { return task.dueDate; }).sort(function(left, right) { return new Date(left.dueDate) - new Date(right.dueDate); });
      else if (vm.mode === 'blocked') vm.visibleTaskItems = open.filter(isBlocked);
      else if (vm.mode === 'recent') vm.visibleTaskItems = vm.tasks.slice().reverse();
      else vm.visibleTaskItems = open;
      return vm.visibleTaskItems;
    }
    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (change.eventType === 'resyncRequired') {
        if (sessionStore.state.project && change.projectId === sessionStore.state.project.id) vm.refresh();
        return;
      }
      if (!sessionStore.state.project || change.projectId !== sessionStore.state.project.id) { return; }
      var index = vm.tasks.findIndex(function(task) { return task.id === change.workItemId; });
      var visible = change.eventType !== 'archived'
        && change.workItem.assigneeUserId === sessionStore.state.currentUser.id;
      if (!visible && index >= 0) { vm.tasks.splice(index, 1); }
      else if (visible && index >= 0) { vm.tasks[index] = change.workItem; }
      else if (visible) { vm.tasks.unshift(change.workItem); }
      vm.tasks.sort(function(left, right) { return (left.rank || 0) - (right.rank || 0); });
      rebuildVisibleTasks();
    });
    $scope.$on('$destroy', unsubscribeRealtime);
    vm.refresh = function() {
      return zumboApi.projects().then(function(projects) {
        var selectedId = sessionStore.state.project && sessionStore.state.project.id;
        sessionStore.state.project = projects.filter(function(project) { return project.id === selectedId; })[0]
          || projects.filter(function(project) {
            return project.members.some(function(member) {
              return member.userId === sessionStore.state.currentUser.id;
            });
          })[0]
          || null;
        if (!sessionStore.state.project) { return []; }
        apiClient.transitionContext('project:' + sessionStore.state.project.id);
        return realtimeService.connect(sessionStore.state.project.id).catch(angular.noop).then(function() {
          return $q.all([
            zumboApi.summary(sessionStore.state.project.id),
            zumboApi.tasks(sessionStore.state.project.id, '')
          ]);
        });
      }).then(function(result) {
        if (result && result.length) {
          vm.summary = result[0];
          vm.tasks = result[1].items || [];
          vm.searchDegraded = result[1].degraded === true;
          realtimeService.synchronize(vm.tasks);
          rebuildVisibleTasks();
        }
      }).finally(function() {
        $ionicScrollDelegate.$getByHandle('dashboardScroll').resize();
      });
    };
    vm.openTask = function(task) {
      $state.go('task-detail', { taskId: task.id });
    };
    vm.refresh();
  })
  .controller('ProjectsController', function($scope, $state, $q, zumboApi, sessionStore, apiClient, mobileActionError) {
    var vm = this;
    vm.mode = 'projects';
    vm.projects = [];
    vm.teams = [];
    vm.archivedProjects = [];
    vm.archivedTeams = [];
    vm.projectDraft = { key: '', name: '' };
    vm.teamDraft = { name: '' };
    vm.load = function() {
      vm.loading = true;
      return $q.all([zumboApi.projects(), zumboApi.teams(), zumboApi.projects(true), zumboApi.teams(true)])
        .then(function(result) {
          vm.projects = result[0];
          vm.teams = result[1];
          vm.archivedProjects = result[2];
          vm.archivedTeams = result[3];
        }).catch(function(error) { vm.error = mobileActionError(error, 'Çalışma alanları yüklenemedi.'); })
        .finally(function() { vm.loading = false; });
    };
    $scope.$on('zumbo:concurrency-conflict', function() {
      vm.notice = null;
      vm.error = mobileActionError({ data: { error: { code: 'CONCURRENCY_CONFLICT' } } });
      vm.load();
    });
    vm.setMode = function(mode) { vm.mode = mode; vm.error = null; };
    vm.select = function(project) {
      apiClient.transitionContext('project:' + project.id);
      sessionStore.state.project = project;
      $state.go('project-detail', { projectId: project.id });
    };
    vm.selectTeam = function(team) {
      apiClient.transitionContext('team:' + team.id);
      sessionStore.state.team = team;
      $state.go('team-detail', { teamId: team.id });
    };
    vm.createProject = function() {
      if (!vm.projectDraft.key || !vm.projectDraft.name || vm.saving) return;
      vm.saving = true;
      zumboApi.createProject(vm.projectDraft).then(function(project) {
        vm.projectDraft = { key: '', name: '' };
        vm.notice = 'Proje oluşturuldu.';
        vm.projects.unshift(project);
      }).catch(function(error) { vm.error = mobileActionError(error, 'Proje oluşturulamadı.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.createTeam = function() {
      if (!vm.teamDraft.name || vm.saving) return;
      vm.saving = true;
      zumboApi.createTeam(vm.teamDraft.name).then(function(team) {
        vm.teamDraft = { name: '' };
        vm.notice = 'Ekip oluşturuldu.';
        vm.teams.unshift(team);
      }).catch(function(error) { vm.error = mobileActionError(error, 'Ekip oluşturulamadı.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.restoreProject = function(project) { return zumboApi.restoreProject(project.id).then(vm.load); };
    vm.restoreTeam = function(team) { return zumboApi.restoreTeam(team.id).then(vm.load); };
    $scope.$on('$ionicView.beforeEnter', vm.load);
    vm.load();
  })
  .controller('NotificationsController', function($scope, zumboApi, mobileActionError) {
    var vm = this;
    vm.mode = 'unread';
    vm.notifications = [];
    vm.loading = false;
    vm.error = '';
    vm.load = function() {
      vm.loading = true;
      vm.error = '';
      return zumboApi.notifications().then(function(data) {
        vm.notifications = data;
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Gelen kutusu yüklenemedi.');
      }).finally(function() {
        vm.loading = false;
      });
    };
    vm.refresh = function() {
      return vm.load().finally(function() {
        $scope.$broadcast('scroll.refreshComplete');
      });
    };
    vm.read = function(notification) {
      if (notification.reading) return;
      notification.reading = true;
      vm.error = '';
      return zumboApi.read(notification.id).then(vm.load).catch(function(error) {
        notification.reading = false;
        vm.error = mobileActionError(error, 'Bildirim güncellenemedi.');
      });
    };
    vm.setMode = function(mode) { vm.mode = mode; };
    vm.visibleNotifications = function() {
      if (vm.mode === 'all') return vm.notifications;
      if (vm.mode === 'actions') return vm.notifications.filter(function(item) { return /approval|mention|onay|bahset/i.test(item.type + ' ' + item.message); });
      return vm.notifications.filter(function(item) { return !item.read; });
    };
    vm.load();
  });
})();
