(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopShellFeature', function() {
      function normalizeSearch(value) {
        return String(value || '').toLocaleLowerCase('tr-TR').trim();
      }

      return {
        install: function(vm) {
          vm.canCreateTask = function() {
            return !!vm.board && !!vm.projectMembership
              && vm.projectRoleHasPermission(vm.projectMembership.role, 'WorkItemCreate');
          };
          vm.openCommandPalette = function() {
            vm.commandOpen = true;
            vm.commandQuery = '';
            vm.activeCommandIndex = 0;
          };
          vm.closeCommandPalette = function() { vm.commandOpen = false; };
          vm.commandQueryChanged = function() { vm.activeCommandIndex = 0; };
          vm.commandAvailable = function(command) {
            if (command.requires === 'audit') return vm.canViewAuditCenter && vm.canViewAuditCenter();
            return command.action !== 'create' || vm.canCreateTask();
          };
          vm.filteredCommands = function() {
            var query = normalizeSearch(vm.commandQuery);
            return vm.commands.filter(function(command) {
              return vm.commandAvailable(command)
                && (!query || normalizeSearch(command.label + ' ' + command.group).indexOf(query) >= 0);
            });
          };
          vm.filteredCommandTasks = function() {
            var query = normalizeSearch(vm.commandQuery);
            return vm.tasks.filter(function(task) {
              return !query || normalizeSearch(task.title + ' ' + task.status + ' ' + task.priority).indexOf(query) >= 0;
            }).slice(0, 6);
          };
          vm.commandResultCount = function() {
            return vm.filteredCommands().length + vm.filteredCommandTasks().length;
          };
          vm.setActiveCommand = function(index) { vm.activeCommandIndex = index; };
          vm.handleCommandKeydown = function(event) {
            var count = vm.commandResultCount();
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault();
              if (!count) return;
              var delta = event.key === 'ArrowDown' ? 1 : -1;
              vm.activeCommandIndex = (vm.activeCommandIndex + delta + count) % count;
            } else if (event.key === 'Enter' && count) {
              event.preventDefault();
              var commands = vm.filteredCommands();
              var selected = vm.activeCommandIndex < commands.length
                ? commands[vm.activeCommandIndex]
                : { action: 'task', task: vm.filteredCommandTasks()[vm.activeCommandIndex - commands.length] };
              if (selected.task || selected.action !== 'task') vm.runCommand(selected);
            }
          };
          vm.runCommand = function(command) {
            if (command.action === 'section') vm.showSection(command.value);
            if (command.action === 'projectView') vm.setProjectView(command.value);
            if (command.action === 'create') vm.openEntityCreator('task');
            if (command.action === 'theme') vm.toggleTheme();
            if (command.action === 'density') vm.setDensity(vm.density === 'compact' ? 'comfortable' : 'compact');
            if (command.action === 'task') vm.selectTask(command.task);
            vm.closeCommandPalette();
          };
        }
      };
    });
})();
