(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('DashboardController', function($scope, $state, $ionicScrollDelegate, $q, zumboApi, sessionStore, realtimeService, apiClient) {
    var vm = this;
    vm.summary = {};
    vm.tasks = [];
    vm.searchDegraded = false;
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
  .controller('NotificationsController', function(zumboApi) {
    var vm = this;
    vm.notifications = [];
    vm.load = function() { return zumboApi.notifications().then(function(data) { vm.notifications = data; }); };
    vm.read = function(notification) { zumboApi.read(notification.id).then(vm.load); };
    vm.load();
  });
})();
