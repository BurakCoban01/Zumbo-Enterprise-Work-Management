(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('ProjectDetailController', function($scope, $state, $stateParams, $q, zumboApi, sessionStore, apiClient, mobileActionError) {
    var vm = this;
    apiClient.transitionContext('project:' + $stateParams.projectId);
    vm.project = sessionStore.state.project;
    vm.boards = [];
    vm.archivedBoards = [];
    vm.users = [];
    vm.projectMemberDraft = { userId: '', role: 'Developer' };
    vm.boardDraft = { name: '', type: 'Kanban' };
    vm.load = function() {
      return zumboApi.projects().then(function(projects) {
        vm.project = projects.filter(function(project) { return project.id === $stateParams.projectId; })[0];
        sessionStore.state.project = vm.project;
        if (!vm.project) return [[], [], []];
        vm.projectDraft = { name: vm.project.name, visibility: vm.project.visibility };
        var membership = vm.project.members.filter(function(member) { return member.userId === sessionStore.state.currentUser.id; })[0];
        vm.canManage = membership && ['ProjectOwner', 'ProjectAdmin'].indexOf(membership.role) >= 0;
        vm.canArchive = membership && membership.role === 'ProjectOwner';
        return $q.all([
          zumboApi.boards(vm.project.id),
          zumboApi.boards(vm.project.id, true),
          zumboApi.audit('Project', vm.project.id),
          vm.canManage ? zumboApi.users() : $q.when([])
        ]);
      }).then(function(result) {
        vm.boards = result[0];
        vm.archivedBoards = result[1];
        vm.audit = result[2];
        vm.users = result[3];
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Proje yüklenemedi.');
      });
    };
    $scope.$on('zumbo:concurrency-conflict', function() {
      vm.notice = null;
      vm.error = mobileActionError({ data: { error: { code: 'CONCURRENCY_CONFLICT' } } });
      vm.load();
    });
    vm.selectBoard = function(board) {
      sessionStore.state.board = board;
      $state.go('app.tasks');
    };
    vm.createBoard = function() {
      if (!vm.project || !vm.canManage || !vm.boardDraft.name || vm.saving) return;
      vm.saving = true;
      zumboApi.createBoard(vm.project.id, vm.boardDraft).then(function() {
        vm.boardDraft = { name: '', type: 'Kanban' };
        vm.notice = 'Pano oluşturuldu.';
        return vm.load();
      }).catch(function(error) { vm.error = mobileActionError(error, 'Pano oluşturulamadı.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.saveProject = function() {
      if (!vm.canManage || vm.saving) return;
      vm.saving = true;
      zumboApi.updateProject(vm.project.id, vm.projectDraft).then(function() { vm.notice = 'Proje kaydedildi.'; return vm.load(); })
        .catch(function(error) { vm.error = mobileActionError(error, 'Proje kaydedilemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.archiveProject = function() {
      if (!vm.canArchive || vm.saving) return;
      vm.saving = true;
      zumboApi.archiveProject(vm.project.id).then(function() { $state.go('app.projects'); })
        .catch(function(error) { vm.error = mobileActionError(error, 'Proje arşivlenemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.saveBoard = function(board) {
      if (!vm.canManage || vm.saving) return;
      vm.saving = true;
      zumboApi.updateBoard(board.id, { name: board.name, type: board.type }).then(function() { vm.notice = 'Pano kaydedildi.'; return vm.load(); })
        .catch(function(error) { vm.error = mobileActionError(error, 'Pano kaydedilemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.archiveBoard = function(board) {
      if (!vm.canManage || vm.saving) return;
      vm.saving = true;
      zumboApi.archiveBoard(board.id).then(vm.load)
        .catch(function(error) { vm.error = mobileActionError(error, 'Pano arşivlenemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.restoreBoard = function(board) { return zumboApi.restoreBoard(board.id).then(vm.load); };
    vm.memberLabel = function(member) {
      var user = vm.users.find(function(item) { return item.id === member.userId; });
      return user ? user.username + ' · ' + user.email : member.userId;
    };
    vm.addProjectMember = function() {
      if (!vm.canManage || !vm.projectMemberDraft.userId || vm.saving) return;
      vm.saving = true;
      zumboApi.addProjectMember(vm.project.id, vm.projectMemberDraft).then(function() {
        vm.projectMemberDraft = { userId: '', role: 'Developer' };
        vm.notice = 'Proje üyesi eklendi.';
        return vm.load();
      }).catch(function(error) { vm.error = mobileActionError(error, 'Proje üyesi eklenemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.saveProjectMember = function(member) {
      if (!vm.canManage || member.role === 'ProjectOwner' || vm.saving) return;
      vm.saving = true;
      zumboApi.changeProjectMemberRole(vm.project.id, member.userId, member.role).then(vm.load)
        .catch(function(error) { vm.error = mobileActionError(error, 'Proje üyesi güncellenemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.removeProjectMember = function(member) {
      if (!vm.canManage || member.role === 'ProjectOwner' || vm.saving) return;
      vm.saving = true;
      zumboApi.removeProjectMember(vm.project.id, member.userId).then(vm.load)
        .catch(function(error) { vm.error = mobileActionError(error, 'Proje üyesi kaldırılamadı.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.load();
  })
  .controller('TeamDetailController', function($scope, $state, $stateParams, $q, zumboApi, sessionStore, apiClient, mobileActionError) {
    var vm = this;
    apiClient.transitionContext('team:' + $stateParams.teamId);
    vm.team = sessionStore.state.team;
    vm.inviteDraft = { email: '', role: 'Member' };
    vm.load = function() {
      return zumboApi.teams().then(function(teams) {
        vm.team = teams.filter(function(team) { return team.id === $stateParams.teamId; })[0];
        if (!vm.team) return [];
        sessionStore.state.team = vm.team;
        vm.teamDraft = { name: vm.team.name };
        vm.membership = vm.team.members.filter(function(member) { return member.userId === sessionStore.state.currentUser.id && member.status === 'Active'; })[0];
        vm.canManage = vm.membership && ['Owner', 'Admin'].indexOf(vm.membership.role) >= 0;
        vm.canArchive = vm.membership && vm.membership.role === 'Owner';
        return zumboApi.audit('Team', vm.team.id);
      }).then(function(audit) { vm.audit = audit; })
        .catch(function(error) { vm.error = mobileActionError(error, 'Ekip yüklenemedi.'); });
    };
    $scope.$on('zumbo:concurrency-conflict', function() {
      vm.notice = null;
      vm.error = mobileActionError({ data: { error: { code: 'CONCURRENCY_CONFLICT' } } });
      vm.load();
    });
    vm.save = function() {
      if (!vm.canManage || !vm.teamDraft.name || vm.saving) return;
      vm.saving = true;
      zumboApi.updateTeam(vm.team.id, vm.teamDraft.name).then(function() { vm.notice = 'Ekip kaydedildi.'; return vm.load(); })
        .catch(function(error) { vm.error = mobileActionError(error, 'Ekip kaydedilemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.invite = function() {
      if (!vm.canManage || !vm.inviteDraft.email || vm.saving) return;
      vm.saving = true;
      zumboApi.inviteTeamMember(vm.team.id, vm.inviteDraft.email, vm.inviteDraft.role).then(function() {
        vm.inviteDraft = { email: '', role: 'Member' };
        vm.notice = 'Ekip daveti gönderildi.';
        return vm.load();
      }).catch(function(error) { vm.error = mobileActionError(error, 'Ekip daveti gönderilemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.removeMember = function(member) {
      if (!vm.canManage || member.role === 'Owner' || vm.saving) return;
      vm.saving = true;
      zumboApi.removeTeamMember(vm.team.id, member.userId || member.email).then(vm.load)
        .catch(function(error) { vm.error = mobileActionError(error, 'Ekip üyesi kaldırılamadı.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.archive = function() {
      if (!vm.canArchive || vm.saving) return;
      vm.saving = true;
      zumboApi.archiveTeam(vm.team.id).then(function() { $state.go('app.projects'); })
        .catch(function(error) { vm.error = mobileActionError(error, 'Ekip arşivlenemedi.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.load();
  })
  .controller('TaskDetailController', function($scope, $stateParams, $window, $q, zumboApi, realtimeService, apiClient, mobileActionError, sessionStore, displayNameResolver) {
    var vm = this;
    apiClient.transitionContext('task:' + $stateParams.taskId);
    vm.task = null;
    vm.transitions = [];
    vm.commentBody = '';
    vm.checklistText = '';
    vm.labelText = '';
    vm.workLogDraft = { hours: null, note: '' };
    vm.taskDraft = { title: '', description: '', priority: 'Medium', dueDate: null };
    vm.schema = { customFields: [] };
    vm.users = [];
    vm.userName = function(userId) {
      return displayNameResolver.user(userId, vm.users, sessionStore.state.currentUser);
    };
    vm.fieldDefinition = function(value) {
      return (vm.schema.customFields || []).find(function(field) { return field.key === value.fieldKey; })
        || { name: value.fieldKey };
    };
    vm.customFieldValue = function(value) {
      if (value.type === 'Text') return value.textValue;
      if (value.type === 'Number') return value.numberValue;
      if (value.type === 'Boolean') return value.booleanValue ? 'Evet' : 'Hayır';
      if (value.type === 'Date') return value.dateValue;
      return value.optionKey;
    };
    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (change.eventType === 'resyncRequired') {
        if (vm.task && change.projectId === vm.task.projectId) vm.load();
        return;
      }
      if (change.workItemId === $stateParams.taskId && change.eventType !== 'archived') {
        vm.load();
      }
    });
    $scope.$on('$destroy', unsubscribeRealtime);
    vm.load = function() {
      return zumboApi.task($stateParams.taskId).then(function(task) {
        vm.task = task;
        vm.taskDraft = {
          title: task.title,
          description: task.description || '',
          priority: task.priority,
          dueDate: task.dueDate ? new Date(task.dueDate) : null
        };
        return realtimeService.connect(task.projectId).catch(angular.noop).then(function() {
          return $q.all([
            zumboApi.workflow(task.projectId),
            zumboApi.workItemSchema(task.projectId),
            zumboApi.users().catch(function() { return []; })
          ]);
        });
      }).then(function(result) {
        var workflow = result[0];
        vm.schema = result[1];
        vm.users = result[2];
        vm.transitions = workflow.transitions.filter(function(transition) {
          return transition.fromStatus === vm.task.status;
        });
      });
    };
    $scope.$on('zumbo:concurrency-conflict', function() {
      vm.error = mobileActionError({ data: { error: { code: 'CONCURRENCY_CONFLICT' } } });
      vm.load();
    });
    function mutation(promise, fallback) {
      vm.error = null;
      return promise.catch(function(error) { vm.error = mobileActionError(error, fallback); });
    }
    vm.move = function(status) { return mutation(zumboApi.moveTask(vm.task.id, status).then(vm.load), 'Görev taşınamadı.'); };
    vm.saveTask = function() {
      if (!vm.taskDraft.title || vm.saving) return;
      vm.saving = true;
      return mutation(zumboApi.updateTask(vm.task.id, vm.taskDraft).then(vm.load), 'Görev kaydedilemedi.')
        .finally(function() { vm.saving = false; });
    };
    vm.addComment = function() {
      if (!vm.commentBody.trim()) { return; }
      return mutation(zumboApi.addComment(vm.task.id, vm.commentBody).then(function() { vm.commentBody = ''; return vm.load(); }), 'Yorum eklenemedi.');
    };
    vm.addChecklist = function() {
      if (!vm.checklistText.trim()) { return; }
      return mutation(zumboApi.addChecklist(vm.task.id, vm.checklistText).then(function() { vm.checklistText = ''; return vm.load(); }), 'Kontrol listesi maddesi eklenemedi.');
    };
    vm.toggleChecklist = function(item) { return mutation(zumboApi.completeChecklist(vm.task.id, item.id, !item.completed).then(vm.load), 'Kontrol listesi güncellenemedi.'); };
    vm.addLabel = function() {
      if (!vm.labelText.trim()) { return; }
      return mutation(zumboApi.addLabel(vm.task.id, vm.labelText).then(function() { vm.labelText = ''; return vm.load(); }), 'Etiket eklenemedi.');
    };
    vm.addWorkLog = function() {
      if (!(vm.workLogDraft.hours > 0)) return;
      return mutation(zumboApi.addWorkLog(vm.task.id, vm.workLogDraft.hours, vm.workLogDraft.note).then(function() {
        vm.workLogDraft = { hours: null, note: '' };
        return vm.load();
      }), 'Çalışma kaydı eklenemedi.');
    };
    vm.upload = function() {
      if (!vm.attachmentFile) { return; }
      return mutation(zumboApi.uploadAttachment(vm.task.id, vm.attachmentFile).then(function() { vm.attachmentFile = null; return vm.load(); }), 'Dosya yüklenemedi.');
    };
    vm.removeAttachment = function(attachment) { return mutation(zumboApi.deleteAttachment(vm.task.id, attachment.id).then(vm.load), 'Dosya silinemedi.'); };
    vm.download = function(attachment) {
      zumboApi.downloadAttachment(vm.task.id, attachment.id).then(function(blob) {
        var url = $window.URL.createObjectURL(blob);
        var link = $window.document.createElement('a');
        link.href = url;
        link.download = attachment.fileName;
        link.click();
        $window.URL.revokeObjectURL(url);
      });
    };
    vm.load();
  });
})();
