(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopProjectDirectoryFeature', function() {
      return {
        install: function(vm, helpers) {
          var storage = window.localStorage;
          vm.projectDirectoryMode = storage.getItem('zumbo.projects.mode') || 'mine';
          vm.projectDirectorySort = storage.getItem('zumbo.projects.sort') || 'name';
          vm.projectDirectoryQuery = '';
          vm.projectDirectoryPage = 1;
          vm.projectDirectoryPageSize = 12;

          vm.setProjectDirectoryMode = function(mode) {
            vm.projectDirectoryMode = mode;
            vm.projectDirectoryPage = 1;
            storage.setItem('zumbo.projects.mode', mode);
          };
          vm.setProjectDirectorySort = function(sort) {
            vm.projectDirectorySort = sort;
            vm.projectDirectoryPage = 1;
            storage.setItem('zumbo.projects.sort', sort);
          };
          vm.resetProjectDirectoryPage = function() { vm.projectDirectoryPage = 1; };
          vm.projectDirectoryItems = function() {
            var query = String(vm.projectDirectoryQuery || '').trim().toLocaleLowerCase('tr-TR');
            var recentOrder = new Map((vm.recentProjects || []).map(function(project, index) { return [project.id, index]; }));
            var items = (vm.projects || []).filter(function(project) {
              if (vm.projectDirectoryMode === 'mine' && !helpers.membershipFor(project)) return false;
              if (vm.projectDirectoryMode === 'favorites' && !vm.isFavoriteProject(project)) return false;
              if (vm.projectDirectoryMode === 'recent' && !recentOrder.has(project.id)) return false;
              return !query || (project.key + ' ' + project.name).toLocaleLowerCase('tr-TR').indexOf(query) >= 0;
            });
            return items.sort(function(left, right) {
              if (vm.projectDirectorySort === 'key') return left.key.localeCompare(right.key, 'tr');
              if (vm.projectDirectorySort === 'recent') {
                return (recentOrder.get(left.id) ?? Number.MAX_SAFE_INTEGER) - (recentOrder.get(right.id) ?? Number.MAX_SAFE_INTEGER);
              }
              return left.name.localeCompare(right.name, 'tr');
            });
          };
          vm.projectDirectoryPageItems = function() {
            var start = (vm.projectDirectoryPage - 1) * vm.projectDirectoryPageSize;
            return vm.projectDirectoryItems().slice(start, start + vm.projectDirectoryPageSize);
          };
          vm.projectDirectoryPageCount = function() {
            return Math.max(1, Math.ceil(vm.projectDirectoryItems().length / vm.projectDirectoryPageSize));
          };
          vm.changeProjectDirectoryPage = function(delta) {
            vm.projectDirectoryPage = Math.min(
              vm.projectDirectoryPageCount(), Math.max(1, vm.projectDirectoryPage + delta));
          };
          vm.projectDirectoryRole = function(project) {
            var membership = helpers.membershipFor(project);
            return membership ? vm.projectRoleLabel(membership.role) : 'Üye değil';
          };
          vm.projectDirectoryCanOpen = function(project) {
            return !!helpers.membershipFor(project);
          };
          vm.openProjectFromDirectory = function(project) {
            if (!helpers.membershipFor(project)) return vm.selectProject(project);
            return vm.selectProject(project, true).then(function() { return vm.setProjectView('overview'); });
          };
        }
      };
    });
})();
