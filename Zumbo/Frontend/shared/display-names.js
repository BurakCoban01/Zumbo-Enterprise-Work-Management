(function() {
  'use strict';

  angular.module('zumbo.shared.displayNames', [])
    .factory('displayNameResolver', function() {
      function userName(userId, users, currentUser) {
        if (!userId) return 'Atanmamış';
        var user = (users || []).find(function(item) { return item.id === userId; });
        if (!user && currentUser && currentUser.id === userId) user = currentUser;
        return user
          ? user.displayName || user.fullName || user.username || user.email || 'Kullanıcı'
          : 'Kullanıcı';
      }

      function organizationName(organizationId, organizations, selected) {
        var organization = selected && (selected.id === organizationId || selected.tenantKey === organizationId)
          ? selected
          : (organizations || []).find(function(item) {
            return item.id === organizationId || item.tenantKey === organizationId;
          });
        return organization && organization.name ? organization.name : 'Çalışma alanı';
      }

      function sprintName(sprintId, sprints) {
        var sprint = (sprints || []).find(function(item) { return item.id === sprintId; });
        return sprint && sprint.name ? sprint.name : 'Sprint';
      }

      return Object.freeze({
        user: userName,
        organization: organizationName,
        sprint: sprintName
      });
    });
})();
