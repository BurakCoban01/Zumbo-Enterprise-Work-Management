(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopManagementFeature', function($q, $window, apiClient, realtimeService) {
      return {
        install: function(vm, helpers) {
          var updateLocation = helpers.updateLocation;
          var apiActionError = helpers.apiActionError;
    function rememberProject(project) {
      if (!project) return;
      vm.recentProjects = [project].concat(vm.recentProjects.filter(function(item) { return item.id !== project.id; })).slice(0, 5);
      window.localStorage.setItem('zumbo.recentProjects', JSON.stringify(vm.recentProjects));
    }

    function membershipFor(project) {
      if (!project || !vm.session.currentUser) return null;
      return (project.members || []).find(function(member) {
        return member.userId === vm.session.currentUser.id;
      }) || null;
    }

    function projectRole(roleName) {
      return (vm.projectRoles || []).find(function(role) { return role.name === roleName; }) || null;
    }

    function projectRoleHasPermission(roleName, permission) {
      var role = projectRole(roleName);
      return !!role && (role.permissions || []).some(function(value) {
        return value === '*' || value === permission;
      });
    }

    vm.projectRoleHasPermission = projectRoleHasPermission;
    vm.hasSystemPermission = function(permission) {
      var assigned = vm.session.currentUser && vm.session.currentUser.roles || [];
      return (vm.roles || []).some(function(role) {
        return role.isActive !== false && assigned.indexOf(role.name) >= 0
          && (role.permissions || []).some(function(value) {
            return value === '*' || value === permission;
          });
      });
    };

    vm.projectAssignableRoles = function() {
      return (vm.projectRoles || []).filter(function(role) { return role.isActive && !role.isProtected; });
    };

    vm.projectRoleLabel = function(roleName) {
      var role = projectRole(roleName);
      return role && role.displayName || roleName;
    };

    vm.projectRoleProtected = function(roleName) {
      return !!(projectRole(roleName) || {}).isProtected;
    };

    function resetProjectMemberDraft() {
      var defaultRole = (vm.projectRoles || []).find(function(role) { return role.isActive && role.isDefault; });
      vm.projectMemberDraft = { userId: '', role: defaultRole ? defaultRole.name : '' };
    }

    function firstAccessibleProject(projects) {
      return (projects || []).find(function(project) { return !!membershipFor(project); }) || null;
    }

    function setProjectState(project, preserveDraft) {
      var existingDraft = preserveDraft && vm.project && vm.project.id === project.id
        ? vm.projectDraft
        : null;
      apiClient.remember('/api/projects/' + project.id, project);
      vm.project = project;
      vm.projectDraft = existingDraft || { name: project.name, visibility: project.visibility };
      vm.projectMembership = membershipFor(project);
      vm.canManageProject = !!vm.projectMembership
        && projectRoleHasPermission(vm.projectMembership.role, 'BoardManage');
      vm.canArchiveProject = !!vm.projectMembership
        && !!(projectRole(vm.projectMembership.role) || {}).isProtected;
      var index = vm.projects.findIndex(function(item) { return item.id === project.id; });
      if (index >= 0) vm.projects[index] = project;
      if (vm.syncProjectCatalog) vm.syncProjectCatalog(project);
      if (vm.syncIntakeContext) vm.syncIntakeContext(project);
      if (vm.syncWorkAutomationContext) vm.syncWorkAutomationContext(project);
      return project;
    }

    function setTeamState(team, preserveDraft) {
      var existingDraft = preserveDraft && team && vm.selectedTeam && vm.selectedTeam.id === team.id
        ? vm.teamDraft
        : null;
      vm.selectedTeam = team;
      vm.teamDraft = team ? (existingDraft || { name: team.name }) : null;
      vm.teamMembership = team && (team.members || []).find(function(member) {
        return member.userId === vm.session.currentUser.id && member.status === 'Active';
      });
      vm.canManageTeam = !!vm.teamMembership && ['Owner', 'Admin'].indexOf(vm.teamMembership.role) >= 0;
      vm.isTeamOwner = !!vm.teamMembership && vm.teamMembership.role === 'Owner';
      if (team) {
        var index = vm.teams.findIndex(function(item) { return item.id === team.id; });
        if (index >= 0) vm.teams[index] = team;
      }
    }

    vm.loadProjectAudit = function() {
      if (!vm.project || !vm.projectMembership) return $q.when([]);
      return apiClient.get('/api/audit/entity/Project/' + vm.project.id)
        .then(function(audit) { vm.entityAudit = audit; return audit; })
        .catch(function() { vm.entityAudit = []; return []; });
    };

    vm.loadTeamAudit = function() {
      if (!vm.selectedTeam) return $q.when([]);
      return apiClient.get('/api/audit/entity/Team/' + vm.selectedTeam.id)
        .then(function(audit) { vm.entityAudit = audit; return audit; })
        .catch(function() { vm.entityAudit = []; return []; });
    };

    vm.reloadProjects = function() {
      if (!vm.session.currentUser) return;
      return $q.all([
        apiClient.get('/api/projects?organizationId=' + encodeURIComponent(vm.session.currentUser.organizationId)),
        apiClient.get('/api/auth/roles?scope=Project'),
        apiClient.get('/api/auth/roles')
      ]).then(function(result) {
        vm.projects = result[0];
        vm.projectRoles = result[1];
        vm.roles = result[2];
        resetProjectMemberDraft();
        return vm.projects;
      });
    };

    vm.loadUsers = function(search) {
      if (!vm.session.currentUser) return $q.when([]);
      var query = search ? '?search=' + encodeURIComponent(search) : '';
      return apiClient.get('/api/auth/users' + query).then(function(users) {
        vm.users = users;
        vm.userRoleDrafts = {};
        users.forEach(function(user) { vm.userRoleDrafts[user.id] = (user.roles || []).slice(); });
        return users;
      }).catch(function() {
        vm.users = [];
        return [];
      });
    };

    vm.loadTeams = function() {
      if (!vm.session.currentUser) return;
      return apiClient.get('/api/teams?organizationId=' + encodeURIComponent(vm.session.currentUser.organizationId))
        .then(function(teams) {
          vm.teams = teams;
          if (vm.selectedTeam) vm.selectedTeam = teams.find(function(team) { return team.id === vm.selectedTeam.id; }) || null;
          return teams;
        });
    };

    vm.selectTeam = function(team, skipLocation) {
      setTeamState(team);
      if (team && !skipLocation) updateLocation('teams', null, true);
      vm.entityAudit = [];
      if (team) vm.loadTeamAudit();
    };

    vm.saveTeam = function() {
      if (!vm.selectedTeam || !vm.teamDraft || !vm.canManageTeam || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/teams/' + vm.selectedTeam.id, vm.teamDraft)
        .then(function(team) { vm.selectTeam(team); vm.notify('success', 'Ekip kaydedildi.'); return vm.loadTeams(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.inviteTeamMember = function() {
      if (!vm.selectedTeam || !vm.teamInviteEmail || !vm.canManageTeam || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/teams/' + vm.selectedTeam.id + '/members', {
        email: vm.teamInviteEmail,
        role: vm.teamInviteRole || 'Member'
      }).then(function(team) {
        vm.teamInviteEmail = '';
        setTeamState(team, true);
        vm.notify('success', 'Ekip daveti oluşturuldu.');
        return vm.loadTeamAudit();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip daveti oluşturulamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.changeTeamMemberRole = function(member) {
      if (!vm.isTeamOwner || !member || !member.userId || member.role === 'Owner' || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.patch('/api/teams/' + vm.selectedTeam.id + '/members/' + member.userId + '/role', { role: member.role })
        .then(function(team) { setTeamState(team, true); vm.notify('success', 'Ekip rolü güncellendi.'); return vm.loadTeamAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip rolü güncellenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.removeTeamMember = function(member) {
      if (!vm.canManageTeam || !member || member.role === 'Owner' || vm.entitySaving) return;
      vm.entitySaving = true;
      var memberKey = member.userId || member.email;
      return apiClient.delete('/api/teams/' + vm.selectedTeam.id + '/members/' + encodeURIComponent(memberKey))
        .then(function(team) { setTeamState(team, true); vm.notify('success', 'Ekip üyesi veya daveti kaldırıldı.'); return vm.loadTeamAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip üyesi kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.transferTeamOwnership = function(member) {
      if (!vm.isTeamOwner || !member || !member.userId || member.status !== 'Active' || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/teams/' + vm.selectedTeam.id + '/ownership-transfer', { newOwnerUserId: member.userId })
        .then(function(team) { setTeamState(team, true); vm.notify('success', 'Ekip sahipliği devredildi.'); return vm.loadTeamAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip sahipliği devredilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.archiveTeam = function() {
      if (!vm.selectedTeam || !vm.isTeamOwner || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/teams/' + vm.selectedTeam.id).then(function() {
        vm.selectTeam(null);
        vm.notify('success', 'Ekip arşivlendi.');
        return vm.loadTeams();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip arşivlenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.loadBoards = function() {
      if (!vm.project || !membershipFor(vm.project)) {
        vm.boards = [];
        return $q.when([]);
      }
      return apiClient.get('/api/boards/by-project/' + vm.project.id).then(function(boards) {
        vm.boards = boards;
        return boards;
      });
    };

    vm.teamName = function(teamId) {
      var team = vm.teams.find(function(item) { return item.id === teamId; });
      return team ? team.name : teamId;
    };

    vm.projectTeams = function() {
      var ids = (vm.project && vm.project.teamIds) || [];
      return vm.teams.filter(function(team) { return ids.indexOf(team.id) >= 0; });
    };

    function setBoardState(board) {
      vm.board = board;
      vm.boardDraft = board ? { name: board.name, type: board.type } : null;
      vm.boardColumns = board
        ? angular.copy((board.columns || []).slice().sort(function(left, right) { return left.position - right.position; }))
        : [];
      if (board) {
        vm.swimlaneMode = board.swimlaneMode || 'None';
        var index = vm.boards.findIndex(function(item) { return item.id === board.id; });
        if (index >= 0) vm.boards[index] = board;
      }
      vm.refreshBoardModel();
      return board;
    }

    vm.loadBoardAudit = function() {
      if (!vm.board) {
        vm.boardAudit = [];
        return $q.when([]);
      }
      return apiClient.get('/api/audit/entity/Board/' + vm.board.id).then(function(audit) {
        vm.boardAudit = audit;
        return audit;
      }).catch(function() {
        vm.boardAudit = [];
        return [];
      });
    };

    vm.selectBoard = function(board, skipLocation) {
      if (!board || !vm.project) return;
      apiClient.transitionContext('project:' + vm.project.id + ':board:' + board.id);
      setBoardState(board);
      vm.loadBoardAudit();
      if (!skipLocation) updateLocation(vm.activeSection, null, true);
      vm.selectedTask = null;
      vm.tasks = [];
      return realtimeService.connect(vm.project.id).catch(angular.noop).then(vm.loadTasks);
    };

    vm.saveBoard = function() {
      if (!vm.board || !vm.boardDraft || !vm.canManageProject || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/boards/' + vm.board.id, vm.boardDraft).then(function(board) {
        setBoardState(board);
        vm.notify('success', 'Pano kaydedildi.');
        return vm.loadBoards().then(vm.loadBoardAudit);
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Pano kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.addBoardColumn = function() {
      if (!vm.board || !vm.canManageProject || !vm.boardColumnDraft.name || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/boards/' + vm.board.id + '/columns', {
        name: vm.boardColumnDraft.name,
        category: vm.boardColumnDraft.category || 'Custom',
        wipLimit: vm.boardColumnDraft.wipLimit || null
      }).then(function(board) {
        setBoardState(board);
        vm.boardColumnDraft = { name: '', category: 'Custom', wipLimit: null };
        vm.notify('success', 'Pano kolonu eklendi.');
        return vm.loadBoardAudit();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Pano kolonu eklenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.saveBoardColumn = function(column) {
      if (!vm.board || !vm.canManageProject || !column || !column.name || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/boards/' + vm.board.id + '/columns/' + column.id, {
        name: column.name,
        category: column.category,
        wipLimit: column.wipLimit || null
      }).then(function(board) { setBoardState(board); vm.notify('success', 'Kolon ayarları kaydedildi.'); return vm.loadBoardAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Kolon ayarları kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.deleteBoardColumn = function(column) {
      if (!vm.board || !vm.canManageProject || !column || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/boards/' + vm.board.id + '/columns/' + column.id)
        .then(function(board) { setBoardState(board); vm.notify('success', 'Pano kolonu kaldırıldı.'); return vm.loadBoardAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Pano kolonu kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.moveBoardColumn = function(column, direction) {
      if (!vm.board || !vm.canManageProject || !column || vm.entitySaving) return;
      var ordered = vm.boardColumns.slice();
      var index = ordered.findIndex(function(item) { return item.id === column.id; });
      var target = index + direction;
      if (index < 0 || target < 0 || target >= ordered.length) return;
      var moved = ordered.splice(index, 1)[0];
      ordered.splice(target, 0, moved);
      vm.entitySaving = true;
      return apiClient.put('/api/boards/' + vm.board.id + '/columns/reorder', {
        columnIds: ordered.map(function(item) { return item.id; })
      }).then(function(board) { setBoardState(board); vm.notify('success', 'Kolon sırası kaydedildi.'); return vm.loadBoardAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Kolon sırası kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.archiveBoard = function() {
      if (!vm.board || !vm.canManageProject || vm.entitySaving) return;
      var boardId = vm.board.id;
      vm.entitySaving = true;
      return apiClient.delete('/api/boards/' + boardId).then(function() {
        vm.notify('success', 'Pano arşivlendi.');
        return vm.loadBoards().then(function(boards) {
          vm.board = boards[0] || null;
          if (vm.board) return vm.selectBoard(vm.board);
          vm.tasks = [];
        });
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Pano arşivlenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.saveProject = function() {
      if (!vm.project || !vm.projectDraft || !vm.canManageProject || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/projects/' + vm.project.id, vm.projectDraft).then(function(project) {
        setProjectState(project);
        rememberProject(project);
        vm.notify('success', 'Proje kaydedildi.');
        return vm.reloadProjects().then(vm.loadProjectAudit);
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Proje kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.addProjectMember = function() {
      if (!vm.project || !vm.canManageProject || !vm.projectMemberDraft.userId || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/projects/' + vm.project.id + '/members', vm.projectMemberDraft)
        .then(function(project) {
          setProjectState(project, true);
          resetProjectMemberDraft();
          vm.notify('success', 'Proje üyesi eklendi.');
          return vm.loadProjectAudit();
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Proje üyesi eklenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.changeProjectMemberRole = function(member) {
      if (!vm.project || !vm.canManageProject || !member || vm.projectRoleProtected(member.role) || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.patch('/api/projects/' + vm.project.id + '/members/' + member.userId + '/role', { role: member.role })
        .then(function(project) { setProjectState(project, true); vm.notify('success', 'Proje rolü güncellendi.'); return vm.loadProjectAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Proje rolü güncellenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.removeProjectMember = function(member) {
      if (!vm.project || !vm.canManageProject || !member || vm.projectRoleProtected(member.role) || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/projects/' + vm.project.id + '/members/' + member.userId)
        .then(function(project) { setProjectState(project, true); vm.notify('success', 'Proje üyesi kaldırıldı.'); return vm.loadProjectAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Proje üyesi kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.addProjectTeam = function() {
      if (!vm.project || !vm.canManageProject || !vm.projectTeamId || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/projects/' + vm.project.id + '/teams', { teamId: vm.projectTeamId })
        .then(function(project) { setProjectState(project, true); vm.projectTeamId = ''; vm.notify('success', 'Ekip projeye bağlandı.'); return vm.loadProjectAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip projeye bağlanamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.removeProjectTeam = function(teamId) {
      if (!vm.project || !vm.canManageProject || !teamId || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/projects/' + vm.project.id + '/teams/' + teamId)
        .then(function(project) { setProjectState(project, true); vm.notify('success', 'Ekip proje bağlantısı kaldırıldı.'); return vm.loadProjectAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Ekip proje bağlantısı kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.archiveProject = function() {
      if (!vm.project || !vm.canArchiveProject || vm.entitySaving) return;
      var projectId = vm.project.id;
      vm.entitySaving = true;
      var pendingLoad = vm.activeTaskLoad || $q.when();
      return pendingLoad.catch(angular.noop)
        .then(function() { return realtimeService.stop(); })
        .then(function() { return apiClient.delete('/api/projects/' + projectId); })
        .then(function() {
        return vm.reloadProjects().then(function(projects) {
          vm.project = null;
          vm.board = null;
          vm.tasks = [];
          var fallbackProject = firstAccessibleProject(projects);
          if (fallbackProject) return vm.selectProject(fallbackProject);
        });
      }).then(function() { vm.notify('success', 'Proje arşivlendi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Proje arşivlenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.selectProject = function(project, skipLocation) {
      if (!project) return;
      apiClient.transitionContext('project:' + project.id);
      setProjectState(project);
      window.localStorage.setItem('zumbo.projectId', project.id);
      vm.board = null;
      vm.tasks = [];
      vm.archivedTasks = [];
      vm.timelineEntries = [];
      vm.timelineError = null;
      vm.summary = {};
      vm.statusDistribution = [];
      vm.workload = [];
      vm.dueDateRisks = [];
      vm.velocity = [];
      vm.sprints = [];
      vm.backlogItems = [];
      vm.selectedTask = null;
      vm.clearSelection();
      rememberProject(project);
      vm.entityAudit = [];
      if (vm.projectMembership) vm.loadProjectAudit();
      var workflowRequest = vm.projectMembership ? vm.loadWorkflow(project.id) : $q.when(null);
      var schemaRequest = vm.projectMembership ? vm.loadWorkItemSchema(project.id) : $q.when(null);
      return $q.all([vm.loadBoards(), workflowRequest, schemaRequest]).then(function(result) {
        var boards = result[0];
        var route = new URLSearchParams($window.location.hash.slice(1));
        var linkedBoardId = route.get('project') === project.id ? route.get('board') : null;
        var linkedBoard = boards.find(function(board) { return board.id === linkedBoardId; });
        setBoardState(linkedBoard || boards[0] || null);
        vm.loadBoardAudit();
        if (!skipLocation) updateLocation(vm.activeSection, null, true);
        if (!vm.board) return vm.loadTasks();
        return realtimeService.connect(project.id).catch(angular.noop).then(vm.loadTasks).then(function() {
          if (vm.activeSection === 'archive') return vm.loadArchivedTasks();
        });
      });
    };

          return {
            membershipFor: membershipFor,
            firstAccessibleProject: firstAccessibleProject,
            setBoardState: setBoardState,
            setProjectState: setProjectState,
            rememberProject: rememberProject
          };
        }
      };
    });
})();
