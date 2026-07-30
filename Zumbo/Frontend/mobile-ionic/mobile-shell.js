(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('MobileMoreController', function(sessionStore) {
    var vm = this;
    vm.session = sessionStore.state;
  })
  .controller('MobileCreateController', function($state, zumboApi, sessionStore, mobileActionError) {
    var vm = this;
    vm.projects = [];
    vm.selectedProjectId = sessionStore.state.project ? sessionStore.state.project.id : '';
    vm.loading = false;
    vm.error = '';

    vm.load = function() {
      vm.loading = true;
      vm.error = '';
      return zumboApi.projects().then(function(projects) {
        vm.projects = projects || [];
        if (!vm.projects.some(function(project) { return project.id === vm.selectedProjectId; })) {
          vm.selectedProjectId = vm.projects[0] ? vm.projects[0].id : '';
        }
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Projeler yüklenemedi.');
      }).finally(function() {
        vm.loading = false;
      });
    };

    vm.continueToForm = function() {
      vm.error = '';
      var project = vm.projects.find(function(item) { return item.id === vm.selectedProjectId; });
      if (!project) {
        vm.error = 'Görevin ekleneceği projeyi seçin.';
        return;
      }
      if (window.navigator.onLine === false) {
        vm.error = 'Çevrimdışıyken yeni görev oluşturulamaz.';
        return;
      }
      sessionStore.state.project = project;
      sessionStore.state.taskMode = 'my';
      sessionStore.state.openCreateTask = true;
      $state.go('app.tasks');
    };

    vm.load();
  })
  .controller('MobileSearchController', function($state, $stateParams, zumboApi, sessionStore, mobileActionError) {
    var vm = this;
    vm.query = String($stateParams.q || '');
    vm.projects = [];
    vm.selectedProjectId = sessionStore.state.project ? sessionStore.state.project.id : '';
    vm.items = [];
    vm.page = 1;
    vm.pageSize = 50;
    vm.hasMore = false;
    vm.projectLoading = false;
    vm.loading = false;
    vm.searched = false;
    vm.degraded = false;
    vm.error = '';

    vm.search = function(page, append) {
      var query = String(vm.query || '').trim();
      if (query.length < 2) {
        vm.error = 'Aramak için en az 2 karakter yazın.';
        vm.items = [];
        vm.searched = false;
        return;
      }
      if (!vm.selectedProjectId) {
        vm.error = 'Arama kapsamı için bir proje seçin.';
        vm.items = [];
        vm.searched = false;
        return;
      }
      page = Number.isInteger(page) && page > 0 ? page : 1;
      append = append === true;
      vm.loading = true;
      vm.error = '';
      return zumboApi.searchWork(vm.selectedProjectId, query, page, vm.pageSize).then(function(result) {
        var items = result.items || [];
        vm.query = query;
        vm.page = page;
        vm.searched = true;
        vm.degraded = result.degraded === true;
        vm.hasMore = items.length === vm.pageSize;
        vm.items = append ? vm.items.concat(items.filter(function(item) {
          return !vm.items.some(function(existing) { return existing.id === item.id; });
        })) : items;
        $state.go('app.search', { q: query }, { location: 'replace', notify: false });
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Arama tamamlanamadı.');
      }).finally(function() {
        vm.loading = false;
      });
    };

    vm.loadMore = function() {
      if (!vm.hasMore || vm.loading) return;
      return vm.search(vm.page + 1, true);
    };
    vm.clear = function() {
      vm.query = '';
      vm.items = [];
      vm.searched = false;
      vm.degraded = false;
      vm.error = '';
      $state.go('app.search', { q: null }, { location: 'replace', notify: false });
    };
    vm.changeProject = function() {
      var project = vm.projects.find(function(item) { return item.id === vm.selectedProjectId; });
      sessionStore.state.project = project || null;
      vm.items = [];
      vm.searched = false;
      vm.degraded = false;
      vm.error = '';
      if (vm.query.trim().length >= 2) vm.search();
    };
    vm.openTask = function(task) {
      $state.go('task-detail', { taskId: task.id });
    };

    vm.loadProjects = function() {
      vm.projectLoading = true;
      return zumboApi.projects().then(function(projects) {
        vm.projects = projects || [];
        if (!vm.projects.some(function(project) { return project.id === vm.selectedProjectId; })) {
          vm.selectedProjectId = vm.projects[0] ? vm.projects[0].id : '';
        }
        if (vm.query.trim().length >= 2 && vm.selectedProjectId) return vm.search();
        return null;
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Arama kapsamı yüklenemedi.');
      }).finally(function() {
        vm.projectLoading = false;
      });
    };

    vm.loadProjects();
  });
})();
