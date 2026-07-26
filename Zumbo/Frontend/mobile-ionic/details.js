(function() {
  'use strict';

  angular.module('zumboMobile')
  .controller('ProjectDetailController', function($scope, $state, $stateParams, $q, zumboApi, sessionStore, apiClient, mobileActionError) {
    var vm = this;
    apiClient.transitionContext('project:' + $stateParams.projectId);
    vm.project = sessionStore.state.project;
    vm.boards = [];
    vm.archivedBoards = [];
    vm.summary = {};
    vm.sprints = [];
    vm.users = [];
    vm.projectMemberDraft = { userId: '', role: 'Developer' };
    vm.boardDraft = { name: '', type: 'Kanban' };
    vm.load = function() {
      return zumboApi.projects().then(function(projects) {
        vm.project = projects.filter(function(project) { return project.id === $stateParams.projectId; })[0];
        sessionStore.state.project = vm.project;
        if (!vm.project) return [[], [], [], [], {}, { items: [] }];
        vm.projectDraft = { name: vm.project.name, visibility: vm.project.visibility };
        var membership = vm.project.members.filter(function(member) { return member.userId === sessionStore.state.currentUser.id; })[0];
        vm.membership = membership;
        vm.canManage = membership && ['ProjectOwner', 'ProjectAdmin'].indexOf(membership.role) >= 0;
        vm.canArchive = membership && membership.role === 'ProjectOwner';
        return $q.all([
          zumboApi.boards(vm.project.id),
          zumboApi.boards(vm.project.id, true),
          zumboApi.audit('Project', vm.project.id),
          vm.canManage ? zumboApi.users() : $q.when([]),
          zumboApi.summary(vm.project.id),
          zumboApi.sprints(vm.project.id)
        ]);
      }).then(function(result) {
        vm.boards = result[0];
        vm.archivedBoards = result[1];
        vm.audit = result[2];
        vm.users = result[3];
        vm.summary = result[4] || {};
        vm.sprints = result[5].items || result[5] || [];
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
    vm.openProjectWork = function(mode) {
      if (mode === 'catalog') {
        $state.go('project-catalog', { projectId: vm.project.id, tab: 'releases' });
        return;
      }
      if (mode === 'intake') {
        $state.go('project-intake', { projectId: vm.project.id, tab: 'forms' });
        return;
      }
      if (mode === 'automation') {
        $state.go('project-automation', { projectId: vm.project.id, tab: 'schedules' });
        return;
      }
      if (mode === 'jobs') {
        $state.go('project-jobs', { projectId: vm.project.id, mode: 'launch' });
        return;
      }
      if (mode === 'plan') {
        $state.go('project-planning', { projectId: vm.project.id, mode: 'calendar' });
        return;
      }
      if (mode === 'reports') {
        $state.go('project-reporting', { projectId: vm.project.id, mode: 'workload', range: 30 });
        return;
      }
      sessionStore.state.taskMode = mode;
      if ((mode === 'board' || mode === 'list') && vm.boards.length) sessionStore.state.board = vm.boards[0];
      $state.go('app.tasks');
    };
    vm.activeSprint = function() {
      return vm.sprints.filter(function(sprint) { return sprint.status === 'Active'; })[0] || null;
    };
    vm.nextMilestone = function() {
      return (vm.project.milestones || []).filter(function(item) { return item.status !== 'Completed'; })
        .sort(function(left, right) { return new Date(left.dueAt) - new Date(right.dueAt); })[0] || null;
    };
    vm.nextRelease = function() {
      return (vm.project.releases || []).filter(function(item) { return item.status !== 'Published'; })
        .sort(function(left, right) {
          return (left.scheduledAt ? new Date(left.scheduledAt).getTime() : Number.MAX_SAFE_INTEGER)
            - (right.scheduledAt ? new Date(right.scheduledAt).getTime() : Number.MAX_SAFE_INTEGER);
        })[0] || null;
    };
    vm.health = function() {
      if ((vm.summary.overdue || 0) > 0) return { level: 'danger', label: 'Takip gerekli' };
      if (vm.activeSprint()) return { level: 'healthy', label: 'Plan üzerinde' };
      return { level: 'neutral', label: 'Planlama açık' };
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
  .controller('TaskDetailController', function($scope, $stateParams, $window, $q, zumboApi, realtimeService, apiClient, mobileActionError, sessionStore, displayNameResolver, mobilePwaService) {
    var vm = this;
    apiClient.transitionContext('task:' + $stateParams.taskId);
    vm.task = null;
    vm.project = null;
    vm.membership = null;
    vm.transitions = [];
    vm.loading = true;
    vm.loadError = null;
    vm.partial = false;
    vm.detailView = 'detail';
    vm.activityTab = 'all';
    vm.actionBusy = null;
    vm.collaboration = { watcherCount: 0, voteCount: 0, watching: false, voted: false, version: 0 };
    vm.commentBody = '';
    vm.commentMentionIds = [];
    vm.commentMentionCandidate = '';
    vm.checklistText = '';
    vm.labelText = '';
    vm.workLogDraft = { hours: null, note: '' };
    vm.taskDraft = { title: '', description: '', priority: 'Medium', dueDate: null };
    vm.relationDraft = { relatedWorkItemId: '', relationType: 'RelatesTo' };
    vm.approvalNote = '';
    vm.schema = { customFields: [] };
    vm.users = [];
    vm.teams = [];
    vm.sprints = [];
    vm.relationCandidates = [];
    vm.relationCandidateTotal = 0;
    var catalogProject = null;
    var catalogCache = [];
    vm.streams = {
      comments: emptyStream(), attachments: emptyStream(), worklogs: emptyStream(),
      approvals: emptyStream(), timeline: emptyStream(), activity: emptyStream()
    };
    var streamLoaders = {
      comments: zumboApi.taskComments,
      attachments: zumboApi.taskAttachments,
      worklogs: zumboApi.taskWorkLogs,
      approvals: zumboApi.taskApprovals,
      timeline: zumboApi.taskTimeline,
      activity: zumboApi.taskActivity
    };

    function emptyStream() {
      return { items: [], page: 0, pageSize: 50, totalCount: 0, loading: false, error: null };
    }

    function role() { return vm.membership && vm.membership.role; }
    function systemAdministrator() {
      var roles = sessionStore.state.currentUser && sessionStore.state.currentUser.roles || [];
      return roles.indexOf('SystemAdmin') >= 0;
    }
    function editableRole() { return systemAdministrator() || ['ProjectOwner', 'ProjectAdmin', 'Developer'].indexOf(role()) >= 0; }
    function managerRole() { return systemAdministrator() || ['ProjectOwner', 'ProjectAdmin'].indexOf(role()) >= 0; }
    vm.offline = function() { return !!mobilePwaService.state.offline; };
    vm.canEditTask = editableRole;
    vm.canComment = function() { return !!vm.membership || systemAdministrator(); };
    vm.canUpload = editableRole;
    vm.canLogWork = editableRole;
    vm.canLink = editableRole;
    vm.canApprove = managerRole;

    vm.userName = function(userId) {
      return displayNameResolver.user(userId, vm.users, sessionStore.state.currentUser);
    };
    vm.fieldDefinition = function(value) {
      return (vm.schema.customFields || []).find(function(field) { return field.key === value.fieldKey; })
        || { name: 'Özel alan' };
    };
    vm.customFieldValue = function(value) {
      if (value.type === 'Text') return value.textValue;
      if (value.type === 'Number') return value.numberValue;
      if (value.type === 'Boolean') return value.booleanValue ? 'Evet' : 'Hayır';
      if (value.type === 'Date') return value.dateValue;
      return value.optionKey;
    };
    vm.customFieldsForTask = function() {
      var layout = (vm.schema.layouts || []).find(function(item) {
        return item.issueTypeKey.toLowerCase() === String(vm.task && vm.task.type || '').toLowerCase();
      });
      var keys = layout ? layout.fieldKeys : [];
      return keys.map(function(key) {
        return (vm.schema.customFields || []).find(function(field) { return field.key === key; });
      }).filter(Boolean);
    };
    vm.relationName = function(relation) {
      var related = vm.relationCandidates.find(function(item) { return item.id === relation.relatedWorkItemId; });
      return relation.relatedWorkItemKey || (related && related.title) || 'Bağlı görev';
    };
    function customFieldModel(values) {
      var model = {};
      (values || []).forEach(function(value) {
        var current = value.textValue;
        if (value.type === 'Number') current = value.numberValue;
        if (value.type === 'Boolean') current = value.booleanValue;
        if (value.type === 'Date') current = value.dateValue ? new Date(value.dateValue + 'T00:00:00') : null;
        if (value.type === 'Select') current = value.optionKey;
        model[value.fieldKey] = current;
      });
      return model;
    }
    function dateOnly(value) {
      if (!value) return null;
      if (typeof value === 'string') return value.slice(0, 10);
      return value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0');
    }
    function customFieldRequests() {
      var values = vm.taskDraft.customFieldValues || {};
      return vm.customFieldsForTask().filter(function(field) {
        return values[field.key] !== undefined && values[field.key] !== null && values[field.key] !== '';
      }).map(function(field) {
        var request = { fieldKey: field.key };
        if (field.type === 'Text') request.textValue = values[field.key];
        if (field.type === 'Number') request.numberValue = values[field.key];
        if (field.type === 'Boolean') request.booleanValue = values[field.key];
        if (field.type === 'Date') request.dateValue = dateOnly(values[field.key]);
        if (field.type === 'Select') request.optionKey = values[field.key];
        return request;
      });
    }

    function buildDraft(task) {
      return {
        title: task.title,
        description: task.description || '',
        priority: task.priority,
        dueDate: task.dueDate ? new Date(task.dueDate) : null,
        assigneeUserId: task.assigneeUserId || '',
        teamId: task.teamId || '',
        sprintId: task.sprintId || '',
        estimatePoints: task.estimatePoints,
        parentId: task.parentId || '',
        customFieldValues: customFieldModel(task.customFields)
      };
    }

    function comparableDraft(draft) {
      if (!draft) return null;
      return {
        title: draft.title || '', description: draft.description || '', priority: draft.priority || '',
        dueDate: dateOnly(draft.dueDate), assigneeUserId: draft.assigneeUserId || '', teamId: draft.teamId || '',
        sprintId: draft.sprintId || '', estimatePoints: draft.estimatePoints == null ? null : Number(draft.estimatePoints),
        parentId: draft.parentId || '', customFieldValues: draft.customFieldValues || {}
      };
    }
    vm.taskDraftHasChanges = function() {
      if (!vm.task || !vm.taskDraft) return false;
      return JSON.stringify(comparableDraft(vm.taskDraft)) !== JSON.stringify(comparableDraft(buildDraft(vm.task)));
    };

    function syncStreams() {
      if (!vm.task) return;
      vm.task.comments = vm.streams.comments.items;
      vm.task.attachments = vm.streams.attachments.items;
      vm.task.workLogs = vm.streams.worklogs.items;
      vm.task.approvals = vm.streams.approvals.items;
      vm.task.statusHistory = vm.streams.timeline.items;
    }

    function loadStream(name, reset) {
      var stream = vm.streams[name];
      if (!vm.task || !stream || stream.loading) return $q.when(stream);
      var page = reset ? 1 : stream.page + 1;
      if (!reset && stream.items.length >= stream.totalCount) return $q.when(stream);
      stream.loading = true;
      stream.error = null;
      return streamLoaders[name](vm.task.id, page).then(function(result) {
        stream.items = reset ? (result.items || []) : stream.items.concat(result.items || []);
        stream.page = result.page || page;
        stream.pageSize = result.pageSize || 50;
        stream.totalCount = Number(result.totalCount) || 0;
        syncStreams();
        return stream;
      }).catch(function(error) {
        stream.error = mobileActionError(error, 'Bu etkinlik bölümü yüklenemedi.');
        vm.partial = true;
        return stream;
      }).finally(function() { stream.loading = false; });
    }
    function loadStreams() { return $q.all(Object.keys(streamLoaders).map(function(name) { return loadStream(name, true); })); }
    vm.loadMoreStream = function(name) { return loadStream(name, false); };
    vm.streamHasMore = function(name) { return vm.streams[name].items.length < vm.streams[name].totalCount; };
    vm.activityStreamName = function() {
      return vm.activityTab === 'comments' ? 'comments' : vm.activityTab === 'history' ? 'timeline' : vm.activityTab === 'worklogs' ? 'worklogs' : 'activity';
    };
    vm.activityEntries = function() { return vm.streams[vm.activityStreamName()].items; };

    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (change.eventType === 'resyncRequired') {
        if (vm.task && change.projectId === vm.task.projectId) vm.load(vm.taskDraftHasChanges() ? angular.copy(vm.taskDraft) : null);
        return;
      }
      if (change.workItemId === $stateParams.taskId && change.eventType !== 'archived') {
        vm.load(vm.taskDraftHasChanges() ? angular.copy(vm.taskDraft) : null);
      }
    });
    $scope.$on('$destroy', unsubscribeRealtime);
    vm.load = function(preservedDraft) {
      vm.loading = true;
      vm.loadError = null;
      vm.partial = false;
      return zumboApi.task($stateParams.taskId).then(function(task) {
        vm.task = task;
        vm.taskDraft = preservedDraft || buildDraft(task);
        return realtimeService.connect(task.projectId).catch(angular.noop).then(function() {
          return $q.all([
            zumboApi.project(task.projectId),
            zumboApi.workflow(task.projectId),
            zumboApi.workItemSchema(task.projectId),
            zumboApi.users().catch(function() { vm.partial = true; return []; }),
            zumboApi.teams().catch(function() { vm.partial = true; return []; }),
            zumboApi.sprints(task.projectId).catch(function() { vm.partial = true; return { items: [] }; }),
            zumboApi.projectTasks(task.projectId, null, 1, 100).catch(function() { vm.partial = true; return { items: [], totalCount: 0 }; }),
            zumboApi.taskCollaboration(task.id).catch(function(error) {
              vm.error = mobileActionError(error, 'Takip ve oy bilgisi yüklenemedi.');
              vm.partial = true;
              return vm.collaboration;
            })
          ]);
        });
      }).then(function(result) {
        vm.project = result[0];
        vm.membership = (vm.project.members || []).find(function(member) {
          return member.userId === sessionStore.state.currentUser.id;
        }) || null;
        var workflow = result[1];
        vm.schema = result[2];
        vm.users = result[3];
        vm.teams = result[4].filter(function(team) { return (vm.project.teamIds || []).indexOf(team.id) >= 0; });
        vm.sprints = result[5].items || result[5] || [];
        vm.relationCandidates = (result[6].items || []).filter(function(item) { return item.id !== vm.task.id; });
        vm.relationCandidateTotal = Number(result[6].totalCount) || vm.relationCandidates.length;
        vm.collaboration = result[7];
        vm.transitions = workflow.transitions.filter(function(transition) {
          return transition.fromStatus === vm.task.status;
        });
        return loadStreams();
      }).catch(function(error) {
        vm.loadError = mobileActionError(error, 'Görev ayrıntıları yüklenemedi.');
      }).finally(function() { vm.loading = false; });
    };
    $scope.$on('zumbo:concurrency-conflict', function() {
      var preservedDraft = angular.copy(vm.taskDraft);
      vm.load(preservedDraft).then(function() {
        vm.error = 'Güncel kayıt yüklendi. Yerel form değişiklikleriniz korunuyor.';
      });
    });
    function mutation(promise, fallback) {
      vm.error = null;
      return promise.catch(function(error) { vm.error = mobileActionError(error, fallback); });
    }
    function refresh(names) { return $q.all(names.map(function(name) { return loadStream(name, true); })); }
    vm.move = function(status) {
      if (!vm.canEditTask() || vm.offline()) return;
      return mutation(zumboApi.moveTask(vm.task.id, status).then(function(task) {
        vm.task = angular.extend({}, vm.task, task);
        return vm.load(vm.taskDraft);
      }), 'Görev taşınamadı.');
    };
    vm.saveTask = function() {
      if (!vm.taskDraft.title || vm.saving || !vm.canEditTask() || vm.offline()) return;
      vm.saving = true;
      var current = vm.task;
      return mutation(zumboApi.updateTask(vm.task.id, {
        title: vm.taskDraft.title,
        description: vm.taskDraft.description,
        priority: vm.taskDraft.priority,
        dueDate: vm.taskDraft.dueDate || null
      }).then(function() {
        return vm.taskDraft.assigneeUserId && vm.taskDraft.assigneeUserId !== (current.assigneeUserId || '')
          ? zumboApi.assignTask(vm.task.id, vm.taskDraft.assigneeUserId) : null;
      }).then(function() {
        return vm.taskDraft.teamId !== (current.teamId || '') ? zumboApi.setTaskTeam(vm.task.id, vm.taskDraft.teamId) : null;
      }).then(function() {
        return vm.taskDraft.parentId !== (current.parentId || '') ? zumboApi.setTaskParent(vm.task.id, vm.taskDraft.parentId) : null;
      }).then(function() {
        return vm.taskDraft.sprintId !== (current.sprintId || '') || vm.taskDraft.estimatePoints !== current.estimatePoints
          ? zumboApi.setTaskPlanning(vm.task.id, vm.taskDraft.sprintId, vm.taskDraft.estimatePoints) : null;
      }).then(function() {
        return zumboApi.setTaskCustomFields(vm.task.id, customFieldRequests());
      }).then(function() { return vm.load(); }), 'Görev kaydedilemedi.')
        .finally(function() { vm.saving = false; });
    };
    vm.addComment = function() {
      if (!vm.commentBody.trim() || !vm.canComment() || vm.offline()) { return; }
      return mutation(zumboApi.addComment(vm.task.id, vm.commentBody, vm.commentMentionIds).then(function() {
        vm.commentBody = '';
        vm.commentMentionIds = [];
        vm.commentMentionCandidate = '';
        return refresh(['comments', 'activity']);
      }), 'Yorum eklenemedi.');
    };
    vm.commentMentionCandidates = function() {
      var selected = new Set(vm.commentMentionIds);
      var members = new Set(vm.project && vm.project.members.map(function(member) { return member.userId; }) || []);
      return vm.users.filter(function(user) { return members.has(user.id) && !selected.has(user.id); });
    };
    vm.addCommentMention = function() {
      if (!vm.commentMentionCandidate || vm.commentMentionIds.indexOf(vm.commentMentionCandidate) >= 0) return;
      vm.commentMentionIds.push(vm.commentMentionCandidate);
      vm.commentMentionCandidate = '';
    };
    vm.removeCommentMention = function(userId) { vm.commentMentionIds = vm.commentMentionIds.filter(function(id) { return id !== userId; }); };
    vm.addChecklist = function() {
      if (!vm.checklistText.trim() || !vm.canEditTask() || vm.offline()) { return; }
      return mutation(zumboApi.addChecklist(vm.task.id, vm.checklistText).then(function() { vm.checklistText = ''; return vm.load(); }), 'Kontrol listesi maddesi eklenemedi.');
    };
    vm.toggleChecklist = function(item) {
      if (!vm.canEditTask() || vm.offline()) return;
      return mutation(zumboApi.completeChecklist(vm.task.id, item.id, !item.completed).then(vm.load), 'Kontrol listesi güncellenemedi.');
    };
    vm.addLabel = function() {
      if (!vm.labelText.trim() || !vm.canEditTask() || vm.offline()) { return; }
      return mutation(zumboApi.addLabel(vm.task.id, vm.labelText).then(function() { vm.labelText = ''; return vm.load(); }), 'Etiket eklenemedi.');
    };
    vm.addWorkLog = function() {
      if (!(vm.workLogDraft.hours > 0) || !vm.canLogWork() || vm.offline()) return;
      return mutation(zumboApi.addWorkLog(vm.task.id, vm.workLogDraft.hours, vm.workLogDraft.note).then(function() {
        vm.workLogDraft = { hours: null, note: '' };
        return refresh(['worklogs', 'activity']);
      }), 'Çalışma kaydı eklenemedi.');
    };
    vm.upload = function() {
      if (!vm.attachmentFile || !vm.canUpload() || vm.offline()) { return; }
      vm.actionBusy = 'upload';
      return mutation(zumboApi.uploadAttachment(vm.task.id, vm.attachmentFile).then(function() {
        vm.attachmentFile = null;
        return refresh(['attachments', 'activity']);
      }), 'Dosya yüklenemedi.').finally(function() { vm.actionBusy = null; });
    };
    vm.removeAttachment = function(attachment) {
      if (!vm.canUpload() || vm.offline()) return;
      return mutation(zumboApi.deleteAttachment(vm.task.id, attachment.id).then(function() { return refresh(['attachments', 'activity']); }), 'Dosya silinemedi.');
    };
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
    function toggleCollaboration(kind) {
      if (vm.actionBusy || vm.offline()) return;
      var snapshot = angular.copy(vm.collaboration);
      var stateField = kind === 'watch' ? 'watching' : 'voted';
      var countField = kind === 'watch' ? 'watcherCount' : 'voteCount';
      var next = !vm.collaboration[stateField];
      vm.collaboration[stateField] = next;
      vm.collaboration[countField] = Math.max(0, vm.collaboration[countField] + (next ? 1 : -1));
      vm.actionBusy = kind;
      var request = kind === 'watch' ? zumboApi.setTaskWatch(vm.task.id, next) : zumboApi.setTaskVote(vm.task.id, next);
      return mutation(request.then(function(result) {
        vm.collaboration = result;
        return loadStream('activity', true);
      }), 'İşbirliği tercihi kaydedilemedi.').then(function(result) {
        if (!result) vm.collaboration = snapshot;
      }).finally(function() { vm.actionBusy = null; });
    }
    vm.toggleWatch = function() { return toggleCollaboration('watch'); };
    vm.toggleVote = function() { return toggleCollaboration('vote'); };
    vm.addRelation = function() {
      if (!vm.canLink() || !vm.relationDraft.relatedWorkItemId || vm.offline()) return;
      return mutation(zumboApi.addTaskRelation(vm.task.id, vm.relationDraft).then(function(task) {
        vm.task = angular.extend({}, vm.task, task);
        vm.relationDraft = { relatedWorkItemId: '', relationType: 'RelatesTo' };
        return loadStream('activity', true);
      }), 'Görev ilişkisi eklenemedi.');
    };
    vm.removeRelation = function(relation) {
      if (!vm.canLink() || vm.offline()) return;
      return mutation(zumboApi.removeTaskRelation(vm.task.id, relation).then(function(task) {
        vm.task = angular.extend({}, vm.task, task);
        return loadStream('activity', true);
      }), 'Görev ilişkisi kaldırılamadı.');
    };
    vm.decideApproval = function(approval, approved) {
      if (!vm.canApprove() || vm.offline()) return;
      return mutation(zumboApi.decideTaskApproval(vm.task.id, approval.id, approved, vm.approvalNote).then(function() {
        vm.approvalNote = '';
        return refresh(['approvals', 'timeline', 'activity']);
      }), 'Onay kararı kaydedilemedi.');
    };
    vm.catalogLinks = function() {
      if (!vm.project) return [];
      if (catalogProject === vm.project) return catalogCache;
      catalogProject = vm.project;
      var result = [];
      (vm.project.components || []).filter(function(item) { return !item.archived; }).forEach(function(item) { result.push({ id: item.id, kind: 'Bileşen', name: item.name, meta: item.description || 'Aktif' }); });
      (vm.project.versions || []).filter(function(item) { return !item.archived; }).forEach(function(item) { result.push({ id: item.id, kind: 'Sürüm', name: item.name, meta: item.status }); });
      (vm.project.releases || []).forEach(function(item) { result.push({ id: item.id, kind: 'Yayın', name: item.name, meta: item.status }); });
      (vm.project.milestones || []).forEach(function(item) { result.push({ id: item.id, kind: 'Kilometre taşı', name: item.name, meta: item.status }); });
      catalogCache = result.slice(0, 8);
      return catalogCache;
    };
    vm.activityLabel = function(type) {
      var labels = {
        WorkItemCreated: 'Görev oluşturuldu',
        WorkItemUpdated: 'Ayrıntılar güncellendi',
        WorkItemMoved: 'Durum değişti',
        WorkItemCommentAdded: 'Yorum eklendi',
        WorkItemCommentEdited: 'Yorum düzenlendi',
        WorkItemCommentDeleted: 'Yorum silindi',
        WorkItemAttachmentUploaded: 'Dosya yüklendi',
        WorkItemAttachmentDeleted: 'Dosya kaldırıldı',
        WorkItemWatched: 'Takip başladı',
        WorkItemUnwatched: 'Takip sona erdi',
        WorkItemVoted: 'Oy eklendi',
        WorkItemVoteRemoved: 'Oy kaldırıldı'
      };
      return labels[type] || String(type || 'Etkinlik').replace(/([a-z])([A-Z])/g, '$1 $2');
    };
    vm.load();
  });
})();
