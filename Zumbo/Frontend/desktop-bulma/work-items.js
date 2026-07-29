(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopWorkItemFeature', function($q, $window, $timeout, apiClient) {
      return {
        install: function(vm, helpers) {
          var updateLocation = helpers.updateLocation;
          var nextStatusFor = helpers.nextStatusFor;
          var apiActionError = helpers.apiActionError;
    var developmentCore = $window.ZumboDevelopmentIntegrationCore;
    var detailRequestId = 0;
    var taskCatalogProject = null;
    var taskCatalogCache = [];
    var streamPaths = {
      comments: 'comments',
      attachments: 'attachments',
      worklogs: 'worklogs',
      approvals: 'approvals',
      timeline: 'timeline',
      activity: 'activity'
    };
    var streamLoaders = {
      comments: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/comments?page=' + page + '&pageSize=50'); },
      attachments: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/attachments?page=' + page + '&pageSize=50'); },
      worklogs: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/worklogs?page=' + page + '&pageSize=50'); },
      approvals: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/approvals?page=' + page + '&pageSize=50'); },
      timeline: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/timeline?page=' + page + '&pageSize=50'); },
      activity: function(taskId, page) { return apiClient.get('/api/work-items/' + taskId + '/activity?page=' + page + '&pageSize=50'); }
    };
    vm.taskDetailMode = 'drawer';
    vm.taskActivityTab = 'all';
    vm.commentMentionIds = [];
    vm.commentMentionCandidate = '';
    resetTaskDetail();

    function emptyStream() {
      return { items: [], page: 0, pageSize: 50, totalCount: 0, loading: false, error: null };
    }

    function resetTaskDetail() {
      vm.taskDetail = {
        loading: false,
        error: null,
        partial: false,
        actionBusy: null,
        actionError: null,
        draftPreserved: false,
        collaboration: { watcherCount: 0, voteCount: 0, watching: false, voted: false, version: 0 }
      };
      vm.taskDevelopment = {
        links: [],
        mappings: [],
        loading: false,
        error: null,
        editorOpen: false,
        draft: developmentCore.emptyLinkDraft()
      };
      vm.taskStreams = {
        comments: emptyStream(),
        attachments: emptyStream(),
        worklogs: emptyStream(),
        approvals: emptyStream(),
        timeline: emptyStream(),
        activity: emptyStream()
      };
    }

    function currentRole() {
      return vm.projectMembership && vm.projectMembership.role;
    }

    function systemAdministrator() {
      var roles = vm.session.currentUser && vm.session.currentUser.roles || [];
      return roles.indexOf('SystemAdmin') >= 0;
    }

    function editableRole() {
      return systemAdministrator() || ['ProjectOwner', 'ProjectAdmin', 'Developer'].indexOf(currentRole()) >= 0;
    }

    function managerRole() {
      return systemAdministrator() || ['ProjectOwner', 'ProjectAdmin'].indexOf(currentRole()) >= 0;
    }

    function mutationsUnavailable() {
      return !!(vm.pwa && vm.pwa.offline);
    }

    vm.canEditTaskDetail = function() { return editableRole(); };
    vm.canMoveTaskDetail = function() { return editableRole(); };
    vm.canCommentOnTask = function() { return !!vm.projectMembership || systemAdministrator(); };
    vm.canUploadTaskAttachment = function() { return editableRole(); };
    vm.canDeleteTaskAttachment = function() { return editableRole(); };
    vm.canLogTaskWork = function() { return editableRole(); };
    vm.canLinkTask = function() { return editableRole(); };
    vm.canApproveTask = function() { return managerRole(); };
    vm.canArchiveTask = function() { return managerRole(); };
    vm.taskMutationsDisabled = mutationsUnavailable;

    function taskDraft(detail) {
      return {
        title: detail.title,
        description: detail.description || '',
        priority: detail.priority,
        dueDate: detail.dueDate ? new Date(detail.dueDate) : null,
        assigneeUserId: detail.assigneeUserId || '',
        teamId: detail.teamId || '',
        sprintId: detail.sprintId || '',
        estimatePoints: detail.estimatePoints,
        parentId: detail.parentId || '',
        customFieldValues: customFieldModel(detail.customFields)
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
      if (!vm.selectedTask || !vm.taskDraft) return false;
      return JSON.stringify(comparableDraft(vm.taskDraft)) !== JSON.stringify(comparableDraft(taskDraft(vm.selectedTask)));
    };

    vm.refreshSelectedTaskFromRealtime = function(task) {
      var preservedDraft = vm.taskDraftHasChanges() ? angular.copy(vm.taskDraft) : null;
      return vm.selectTask(task, true, preservedDraft).then(function(result) {
        if (preservedDraft) {
          vm.taskDetail.draftPreserved = true;
          vm.taskDetail.actionError = 'Görev güncellendi. Yerel form değişiklikleriniz korunuyor.';
        }
        return result;
      });
    };

    function syncTaskStreams() {
      if (!vm.selectedTask) return;
      vm.selectedTask.comments = vm.taskStreams.comments.items;
      vm.selectedTask.attachments = vm.taskStreams.attachments.items;
      vm.selectedTask.workLogs = vm.taskStreams.worklogs.items;
      vm.selectedTask.approvals = vm.taskStreams.approvals.items;
      vm.selectedTask.statusHistory = vm.taskStreams.timeline.items;
    }

    function loadTaskStream(name, reset) {
      if (!vm.selectedTask || !streamPaths[name]) return $q.when(null);
      var stream = vm.taskStreams[name];
      var page = reset ? 1 : stream.page + 1;
      if (stream.loading || (!reset && stream.items.length >= stream.totalCount)) return $q.when(stream);
      stream.loading = true;
      stream.error = null;
      return streamLoaders[name](vm.selectedTask.id, page).then(function(result) {
        stream.items = reset ? (result.items || []) : stream.items.concat(result.items || []);
        stream.page = result.page || page;
        stream.pageSize = result.pageSize || 50;
        stream.totalCount = Number(result.totalCount) || 0;
        syncTaskStreams();
        return stream;
      }).catch(function(error) {
        stream.error = apiActionError(error, 'Bu etkinlik bölümü yüklenemedi.');
        vm.taskDetail.partial = true;
        return stream;
      }).finally(function() { stream.loading = false; });
    }

    vm.loadMoreTaskStream = function(name) { return loadTaskStream(name, false); };
    vm.taskStreamHasMore = function(name) {
      var stream = vm.taskStreams[name];
      return !!stream && stream.items.length < stream.totalCount;
    };

    function loadTaskStreams() {
      return $q.all(Object.keys(streamPaths).map(function(name) { return loadTaskStream(name, true); }));
    }

    function loadCollaboration(taskId) {
      return apiClient.get('/api/work-items/' + taskId + '/collaboration').then(function(collaboration) {
        vm.taskDetail.collaboration = collaboration;
        return collaboration;
      }).catch(function(error) {
        vm.taskDetail.partial = true;
        vm.taskDetail.actionError = apiActionError(error, 'Takip ve oy bilgisi yüklenemedi.');
        return null;
      });
    }

    function loadTaskDevelopment(taskId) {
      vm.taskDevelopment.loading = true;
      vm.taskDevelopment.error = null;
      var mappingRequest = vm.canLinkTask()
        ? apiClient.get('/api/work-items/' + taskId + '/development-links/mappings')
            .catch(function(error) {
              vm.taskDevelopment.error = apiActionError(
                error,
                'Repository eşlemeleri yüklenemedi.'
              );
              vm.taskDetail.partial = true;
              return [];
            })
        : $q.when([]);
      return $q.all([
        apiClient.get('/api/work-items/' + taskId + '/development-links'),
        mappingRequest
      ]).then(function(results) {
        vm.taskDevelopment.links = results[0] || [];
        vm.taskDevelopment.mappings = results[1] || [];
        return vm.taskDevelopment;
      }).catch(function(error) {
        vm.taskDevelopment.error = apiActionError(
          error,
          'Geliştirme bağlantıları yüklenemedi.'
        );
        vm.taskDetail.partial = true;
        return vm.taskDevelopment;
      }).finally(function() {
        vm.taskDevelopment.loading = false;
      });
    }

    vm.selectTask = function(task, skipLocation) {
      var preservedDraft = arguments.length > 2 ? arguments[2] : null;
      if (!task) return $q.when(null);
      if (!skipLocation) vm.activeSection = 'board';
      var requestId = ++detailRequestId;
      resetTaskDetail();
      vm.taskDetail.loading = true;
      vm.selectedTask = { id: task.id, title: task.title || '' };
      vm.taskDraft = null;
      return apiClient.get('/api/work-items/' + task.id).then(function(detail) {
        if (requestId !== detailRequestId) return null;
        vm.selectedTask = detail;
        if (!skipLocation) updateLocation('board', detail.id, true);
        vm.taskDraft = preservedDraft || taskDraft(detail);
        vm.taskDetail.draftPreserved = !!preservedDraft;
        vm.nextStatus = nextStatusFor(detail.status);
        return $q.all([
          loadCollaboration(detail.id),
          loadTaskStreams(),
          loadTaskDevelopment(detail.id),
          apiClient.get('/api/audit/entity/WorkItem/' + detail.id).then(function(audit) {
            vm.audit = audit;
          }).catch(function() {
            vm.audit = [];
            vm.taskDetail.partial = true;
          })
        ]);
      }).catch(function(error) {
        if (requestId !== detailRequestId) return null;
        vm.taskDetail.error = apiActionError(error, 'Görev ayrıntıları yüklenemedi.');
        return null;
      }).finally(function() {
        if (requestId === detailRequestId) vm.taskDetail.loading = false;
      });
    };

    vm.openTaskDevelopmentEditor = function() {
      vm.taskDevelopment.draft = developmentCore.emptyLinkDraft();
      if (vm.taskDevelopment.mappings.length === 1) {
        vm.taskDevelopment.draft.mappingId = vm.taskDevelopment.mappings[0].id;
      }
      vm.taskDevelopment.editorOpen = true;
      vm.taskDevelopment.error = null;
    };

    vm.closeTaskDevelopmentEditor = function() {
      vm.taskDevelopment.editorOpen = false;
      vm.taskDevelopment.draft = developmentCore.emptyLinkDraft();
    };

    vm.createTaskDevelopmentLink = function() {
      if (!vm.selectedTask || !vm.canLinkTask() || mutationsUnavailable()
          || vm.taskDetail.actionBusy) return;
      var request;
      try {
        request = developmentCore.validateLinkDraft(
          vm.taskDevelopment.draft,
          vm.taskDevelopment.mappings
        );
      } catch (error) {
        vm.taskDevelopment.error = error.message;
        return;
      }
      vm.taskDetail.actionBusy = 'development-link';
      vm.taskDevelopment.error = null;
      return apiClient.post(
        '/api/work-items/' + vm.selectedTask.id + '/development-links',
        request
      ).then(function(link) {
        var found = vm.taskDevelopment.links.some(function(item) {
          return item.id === link.id;
        });
        if (!found) vm.taskDevelopment.links.unshift(link);
        vm.closeTaskDevelopmentEditor();
        vm.notify('success', 'Geliştirme bağlantısı eklendi.');
      }).catch(function(error) {
        vm.taskDevelopment.error = apiActionError(
          error,
          'Geliştirme bağlantısı eklenemedi.'
        );
      }).finally(function() {
        vm.taskDetail.actionBusy = null;
      });
    };

    vm.deleteTaskDevelopmentLink = function(link) {
      if (!vm.selectedTask || !link || !vm.canLinkTask()
          || mutationsUnavailable() || vm.taskDetail.actionBusy) return;
      if (!$window.confirm(link.title + ' bağlantısı kaldırılsın mı?')) return;
      vm.taskDetail.actionBusy = link.id;
      return apiClient.delete(
        '/api/work-items/' + vm.selectedTask.id + '/development-links/'
          + link.id + '?expectedVersion=' + link.version
      ).then(function() {
        vm.taskDevelopment.links = vm.taskDevelopment.links.filter(function(item) {
          return item.id !== link.id;
        });
        vm.notify('success', 'Geliştirme bağlantısı kaldırıldı.');
      }).catch(function(error) {
        vm.taskDevelopment.error = apiActionError(
          error,
          'Geliştirme bağlantısı kaldırılamadı.'
        );
      }).finally(function() {
        vm.taskDetail.actionBusy = null;
      });
    };

    vm.taskDevelopmentState = developmentCore.linkState;
    vm.taskDevelopmentKind = developmentCore.kindLabel;
    vm.taskDevelopmentUrl = developmentCore.safeUrlLabel;

    vm.retryTaskDetail = function() {
      if (!vm.selectedTask) return $q.when(null);
      return vm.selectTask({ id: vm.selectedTask.id, title: vm.selectedTask.title }, true);
    };

    vm.reloadSelectedTaskAfterConflict = function() {
      if (!vm.selectedTask || !vm.taskDraft) return $q.when(null);
      vm.taskConflictDraft = angular.copy(vm.taskDraft);
      vm.taskDetail.draftPreserved = true;
      return vm.selectTask({ id: vm.selectedTask.id, title: vm.selectedTask.title }, true, vm.taskConflictDraft)
        .then(function(result) {
          vm.taskDetail.draftPreserved = true;
          vm.taskDetail.actionError = 'Güncel kayıt yüklendi. Yerel değişiklikleriniz formda korunuyor.';
          return result;
        });
    };

    vm.openTaskPage = function() {
      vm.taskDetailMode = 'page';
      if (vm.selectedTask) updateLocation('board', vm.selectedTask.id, true);
    };

    vm.collapseTaskDetail = function() {
      vm.taskDetailMode = 'drawer';
      if (vm.selectedTask) updateLocation('board', vm.selectedTask.id, true);
    };

    vm.closeTask = function() {
      var taskId = vm.selectedTask && vm.selectedTask.id;
      vm.selectedTask = null;
      vm.taskDraft = null;
      vm.audit = [];
      vm.taskDetailMode = 'drawer';
      vm.commentMentionIds = [];
      vm.commentMentionCandidate = '';
      resetTaskDetail();
      updateLocation(vm.activeSection, null, true);
      $timeout(function() {
        var target = Array.prototype.find.call(
          $window.document.querySelectorAll('[data-work-item-id]'),
          function(element) { return element.getAttribute('data-work-item-id') === taskId; }
        );
        if (target) target.focus();
      });
    };

    function refreshStreams(names) {
      return $q.all(names.map(function(name) { return loadTaskStream(name, true); }));
    }

    function acceptTaskMutation(task) {
      if (task && task.id && vm.selectedTask && task.id === vm.selectedTask.id) {
        vm.selectedTask = angular.extend({}, vm.selectedTask, task);
        syncTaskStreams();
      }
      return task;
    }

    function toggleCollaboration(kind) {
      if (!vm.selectedTask || vm.taskDetail.actionBusy || mutationsUnavailable()) return $q.when(false);
      var collaboration = vm.taskDetail.collaboration;
      var snapshot = angular.copy(collaboration);
      var stateField = kind === 'watch' ? 'watching' : 'voted';
      var countField = kind === 'watch' ? 'watcherCount' : 'voteCount';
      var next = !collaboration[stateField];
      collaboration[stateField] = next;
      collaboration[countField] = Math.max(0, collaboration[countField] + (next ? 1 : -1));
      vm.taskDetail.actionBusy = kind;
      vm.taskDetail.actionError = null;
      var body = kind === 'watch' ? { watching: next } : { voted: next };
      return apiClient.put('/api/work-items/' + vm.selectedTask.id + '/' + kind, body).then(function(result) {
        vm.taskDetail.collaboration = result;
        vm.notify('success', kind === 'watch'
          ? (next ? 'Görev takip ediliyor.' : 'Görev takibinden çıkıldı.')
          : (next ? 'Göreve oy verildi.' : 'Görev oyu kaldırıldı.'));
        return loadTaskStream('activity', true).then(function() { return true; });
      }).catch(function(error) {
        vm.taskDetail.collaboration = snapshot;
        vm.taskDetail.actionError = apiActionError(error, 'İşbirliği tercihi kaydedilemedi.');
        return false;
      }).finally(function() { vm.taskDetail.actionBusy = null; });
    }

    vm.toggleTaskWatch = function() { return toggleCollaboration('watch'); };
    vm.toggleTaskVote = function() { return toggleCollaboration('vote'); };

    vm.commentMentionCandidates = function() {
      var selected = new Set(vm.commentMentionIds || []);
      var memberIds = new Set((vm.project && vm.project.members || []).map(function(member) { return member.userId; }));
      return (vm.users || []).filter(function(user) {
        return memberIds.has(user.id) && !selected.has(user.id);
      });
    };

    vm.addCommentMention = function() {
      if (!vm.commentMentionCandidate || vm.commentMentionIds.indexOf(vm.commentMentionCandidate) >= 0) return;
      vm.commentMentionIds.push(vm.commentMentionCandidate);
      vm.commentMentionCandidate = '';
    };

    vm.removeCommentMention = function(userId) {
      vm.commentMentionIds = vm.commentMentionIds.filter(function(id) { return id !== userId; });
    };

    vm.taskCatalogLinks = function() {
      if (!vm.project) return [];
      if (taskCatalogProject === vm.project) return taskCatalogCache;
      taskCatalogProject = vm.project;
      var result = [];
      (vm.project.components || []).filter(function(item) { return !item.archived; }).forEach(function(item) {
        result.push({ id: item.id, kind: 'Bileşen', name: item.name, meta: item.description || 'Aktif' });
      });
      (vm.project.versions || []).filter(function(item) { return !item.archived; }).forEach(function(item) {
        result.push({ id: item.id, kind: 'Sürüm', name: item.name, meta: item.status });
      });
      (vm.project.releases || []).forEach(function(item) {
        result.push({ id: item.id, kind: 'Yayın', name: item.name, meta: item.status });
      });
      (vm.project.milestones || []).forEach(function(item) {
        result.push({ id: item.id, kind: 'Kilometre taşı', name: item.name, meta: item.status });
      });
      taskCatalogCache = result.slice(0, 12);
      return taskCatalogCache;
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

    vm.taskActivityEntries = function() {
      if (vm.taskActivityTab === 'comments') return vm.taskStreams.comments.items;
      if (vm.taskActivityTab === 'history') return vm.taskStreams.timeline.items;
      if (vm.taskActivityTab === 'worklogs') return vm.taskStreams.worklogs.items;
      return vm.taskStreams.activity.items;
    };

    vm.taskActivityStreamName = function() {
      return vm.taskActivityTab === 'comments' ? 'comments'
        : vm.taskActivityTab === 'history' ? 'timeline'
          : vm.taskActivityTab === 'worklogs' ? 'worklogs' : 'activity';
    };

    vm.formatFileSize = function(bytes) {
      var size = Number(bytes) || 0;
      if (size < 1024) return size + ' B';
      if (size < 1024 * 1024) return Math.round(size / 1024) + ' KB';
      return (size / (1024 * 1024)).toFixed(1) + ' MB';
    };

    vm.saveSelectedTask = function() {
      if (!vm.selectedTask || !vm.taskDraft || vm.taskSaving || !vm.canEditTaskDetail() || mutationsUnavailable()) return;
      vm.taskSaving = true;
      var taskId = vm.selectedTask.id;
      var current = vm.selectedTask;
      var assigneeUserId = vm.taskDraft.assigneeUserId || null;
      var teamId = vm.taskDraft.teamId || null;
      var sprintId = vm.taskDraft.sprintId || null;
      var estimatePoints = vm.taskDraft.estimatePoints == null ? null : vm.taskDraft.estimatePoints;
      var parentId = vm.taskDraft.parentId || null;
      return apiClient.put('/api/work-items/' + taskId, {
        title: vm.taskDraft.title,
        description: vm.taskDraft.description,
        priority: vm.taskDraft.priority,
        dueDate: vm.taskDraft.dueDate || null
      }).then(function(task) {
        if (assigneeUserId && assigneeUserId !== (current.assigneeUserId || null)) {
          return apiClient.patch('/api/work-items/' + taskId + '/assignee', { assigneeUserId: assigneeUserId });
        }
        return task;
      }).then(function() {
        if (teamId !== (current.teamId || null)) {
          return apiClient.patch('/api/work-items/' + taskId + '/team', { teamId: teamId });
        }
        return null;
      }).then(function() {
        if (parentId !== (current.parentId || null)) {
          return apiClient.patch('/api/work-items/' + taskId + '/parent', { parentId: parentId });
        }
        return null;
      }).then(function() {
        if (sprintId !== (current.sprintId || null) || estimatePoints !== current.estimatePoints) {
          return apiClient.patch('/api/work-items/' + taskId + '/planning', {
            sprintId: sprintId,
            estimatePoints: estimatePoints
          });
        }
        return null;
      }).then(function() {
        return apiClient.put('/api/work-items/' + taskId + '/custom-fields', {
          values: vm.customFieldRequests(current.type, vm.taskDraft.customFieldValues)
        });
      }).then(function() {
        return apiClient.get('/api/work-items/' + taskId);
      }).then(function(task) {
        vm.selectedTask = task;
        vm.taskDetail.draftPreserved = false;
        vm.taskConflictDraft = null;
        vm.notify('success', 'Görev ayrıntıları kaydedildi.');
        return vm.loadTasks();
      }).catch(function(error) {
        vm.taskDetail.actionError = apiActionError(error, 'Görev kaydedilemedi; alanları kontrol edin.');
        vm.notify('error', vm.taskDetail.actionError);
      }).finally(function() { vm.taskSaving = false; });
    };

    vm.archiveSelectedTask = function() {
      if (!vm.selectedTask || vm.taskSaving || !vm.canArchiveTask() || mutationsUnavailable()) return;
      var id = vm.selectedTask.id;
      vm.taskSaving = true;
      return apiClient.delete('/api/work-items/' + id).then(function() {
        vm.closeTask();
        vm.notify('success', 'Görev arşive taşındı.');
        return vm.loadTasks();
      }).catch(function(error) {
        vm.notify('error', apiActionError(error, 'Görev arşivlenemedi.'));
      }).finally(function() { vm.taskSaving = false; });
    };

    vm.addComment = function() {
      if (!vm.selectedTask || !vm.commentBody.trim() || !vm.canCommentOnTask() || mutationsUnavailable()) return;
      vm.taskDetail.actionError = null;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/comments', {
        body: vm.commentBody,
        mentions: vm.commentMentionIds
      }).then(acceptTaskMutation).then(function() {
        vm.commentBody = '';
        vm.commentMentionIds = [];
        vm.commentMentionCandidate = '';
        vm.notify('success', 'Yorum eklendi.');
        return refreshStreams(['comments', 'activity']);
      }).catch(function(error) {
        vm.taskDetail.actionError = apiActionError(error, 'Yorum eklenemedi.');
      });
    };

    vm.editComment = function(comment) {
      if (!vm.canCommentOnTask() || mutationsUnavailable()) return;
      comment.editing = true;
      comment.draftBody = comment.body;
    };

    vm.saveComment = function(comment) {
      if (!comment || !comment.draftBody || !comment.draftBody.trim() || !vm.canCommentOnTask() || mutationsUnavailable()) return;
      return apiClient.put('/api/work-items/' + vm.selectedTask.id + '/comments/' + comment.id, { body: comment.draftBody })
        .then(acceptTaskMutation).then(function() {
          vm.notify('success', 'Yorum güncellendi.');
          return refreshStreams(['comments', 'activity']);
        }).catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Yorum güncellenemedi.'); });
    };

    vm.deleteComment = function(comment) {
      if (!comment || !vm.canCommentOnTask() || mutationsUnavailable()) return;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/comments/' + comment.id)
        .then(acceptTaskMutation).then(function() {
          vm.notify('success', 'Yorum silindi.');
          return refreshStreams(['comments', 'activity']);
        }).catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Yorum silinemedi.'); });
    };

    vm.addLabel = function() {
      if (!vm.selectedTask || !vm.labelText.trim() || !vm.canEditTaskDetail() || mutationsUnavailable()) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/labels', { label: vm.labelText })
        .then(acceptTaskMutation).then(function() { vm.labelText = ''; vm.notify('success', 'Etiket eklendi.'); })
        .catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Etiket eklenemedi.'); });
    };

    vm.removeLabel = function(label) {
      if (!vm.canEditTaskDetail() || mutationsUnavailable()) return;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/labels/' + encodeURIComponent(label))
        .then(acceptTaskMutation).then(function() { vm.notify('success', 'Etiket kaldırıldı.'); })
        .catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Etiket kaldırılamadı.'); });
    };

    vm.addWorkLog = function() {
      if (!vm.selectedTask || !vm.workLogDraft.hours || !vm.canLogTaskWork() || mutationsUnavailable()) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/worklogs', {
        userId: vm.session.currentUser.id,
        hours: vm.workLogDraft.hours,
        note: vm.workLogDraft.note || null
      }).then(acceptTaskMutation).then(function() {
        vm.workLogDraft = { hours: null, note: '' };
        vm.notify('success', 'İş günlüğü eklendi.');
        return refreshStreams(['worklogs', 'activity']);
      }).catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'İş günlüğü eklenemedi.'); });
    };

    vm.addChecklist = function() {
      if (!vm.selectedTask || !vm.checklistText.trim() || !vm.canEditTaskDetail() || mutationsUnavailable()) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/checklist', { text: vm.checklistText })
        .then(acceptTaskMutation).then(function() { vm.checklistText = ''; })
        .catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Kontrol listesi maddesi eklenemedi.'); });
    };

    vm.toggleChecklist = function(item) {
      if (!vm.canEditTaskDetail() || mutationsUnavailable()) return;
      return apiClient.patch('/api/work-items/' + vm.selectedTask.id + '/checklist/' + item.id, { completed: !item.completed })
        .then(acceptTaskMutation)
        .catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Kontrol listesi güncellenemedi.'); });
    };

    vm.uploadAttachment = function() {
      if (!vm.selectedTask || !vm.attachmentFile || !vm.canUploadTaskAttachment() || mutationsUnavailable()) return;
      vm.taskDetail.actionBusy = 'upload';
      return apiClient.upload('/api/work-items/' + vm.selectedTask.id + '/attachments/upload', vm.attachmentFile)
        .then(acceptTaskMutation).then(function() {
          vm.attachmentFile = null;
          vm.notify('success', 'Dosya yüklendi ve güvenlik kontrolüne alındı.');
          return refreshStreams(['attachments', 'activity']);
        }).catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Dosya yüklenemedi.'); })
        .finally(function() { vm.taskDetail.actionBusy = null; });
    };

    vm.deleteAttachment = function(attachment) {
      if (!vm.canDeleteTaskAttachment() || mutationsUnavailable()) return;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/attachments/' + attachment.id)
        .then(acceptTaskMutation).then(function() { return refreshStreams(['attachments', 'activity']); })
        .catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Dosya silinemedi.'); });
    };

    vm.downloadAttachment = function(attachment) {
      apiClient.download('/api/work-items/' + vm.selectedTask.id + '/attachments/' + attachment.id + '/download')
        .then(function(blob) {
          var url = $window.URL.createObjectURL(blob);
          var link = $window.document.createElement('a');
          link.href = url;
          link.download = attachment.fileName;
          link.click();
          $window.URL.revokeObjectURL(url);
        });
    };

    vm.moveSelected = function() {
      if (!vm.selectedTask || !vm.canMoveTaskDetail() || mutationsUnavailable()) {
        return;
      }

      apiClient.patch('/api/work-items/' + vm.selectedTask.id + '/status', { status: vm.nextStatus })
        .then(function(task) {
          acceptTaskMutation(task);
          vm.nextStatus = nextStatusFor(task.status);
          return $q.all([vm.loadTasks(), refreshStreams(['timeline', 'activity'])]);
        }).catch(function(error) { vm.taskDetail.actionError = apiActionError(error, 'Görev taşınamadı.'); });
    };

    vm.selectedTransition = function() {
      if (!vm.selectedTask || !vm.workflow) return null;
      return (vm.workflow.transitions || []).find(function(transition) {
        return transition.fromStatus === vm.selectedTask.status && transition.toStatus === vm.nextStatus;
      }) || null;
    };

    vm.taskTitle = function(taskId) {
      var task = vm.tasks.find(function(item) { return item.id === taskId; });
      return task ? task.title : taskId;
    };

    vm.taskLinkCandidates = function() {
      if (!vm.selectedTask) return [];
      return vm.tasks.filter(function(task) { return task.id !== vm.selectedTask.id && !task.archived; });
    };

    vm.loadWorkItemSchema = function(projectId) {
      projectId = projectId || (vm.project && vm.project.id);
      if (!projectId) return $q.when(null);
      return apiClient.get('/api/work-item-schemas/' + projectId).then(function(schema) {
        if (vm.project && vm.project.id === projectId) vm.workItemSchema = schema;
        return schema;
      }).catch(function(error) {
        vm.notify('error', apiActionError(error, 'İş türü şeması yüklenemedi.'));
        return null;
      });
    };

    vm.activeIssueTypes = function() {
      return (vm.workItemSchema.issueTypes || []).filter(function(type) { return type.active; });
    };

    vm.defaultIssueType = function() {
      var types = vm.activeIssueTypes();
      var task = types.find(function(type) { return type.key === 'Task'; });
      return (task || types[0] || { key: 'Task' }).key;
    };

    vm.issueTypeDefinition = function(typeKey) {
      return (vm.workItemSchema.issueTypes || []).find(function(type) {
        return type.key.toLowerCase() === String(typeKey || '').toLowerCase();
      }) || null;
    };

    vm.canHaveParent = function(typeKey) {
      var definition = vm.issueTypeDefinition(typeKey);
      return definition && definition.hierarchyLevel !== 'Epic';
    };

    vm.customFieldsFor = function(typeKey) {
      var layout = (vm.workItemSchema.layouts || []).find(function(item) {
        return item.issueTypeKey.toLowerCase() === String(typeKey || '').toLowerCase();
      });
      var keys = layout ? layout.fieldKeys : [];
      return keys.map(function(key) {
        return (vm.workItemSchema.customFields || []).find(function(field) { return field.key === key; });
      }).filter(Boolean);
    };

    function dateOnly(value) {
      if (!value) return null;
      if (typeof value === 'string') return value.slice(0, 10);
      var year = value.getFullYear();
      var month = String(value.getMonth() + 1).padStart(2, '0');
      var day = String(value.getDate()).padStart(2, '0');
      return year + '-' + month + '-' + day;
    }

    vm.customFieldRequests = function(typeKey, values) {
      values = values || {};
      return vm.customFieldsFor(typeKey).filter(function(field) {
        var value = values[field.key];
        return value !== undefined && value !== null && value !== '';
      }).map(function(field) {
        var request = { fieldKey: field.key };
        if (field.type === 'Text') request.textValue = values[field.key];
        if (field.type === 'Number') request.numberValue = values[field.key];
        if (field.type === 'Boolean') request.booleanValue = values[field.key];
        if (field.type === 'Date') request.dateValue = dateOnly(values[field.key]);
        if (field.type === 'Select') request.optionKey = values[field.key];
        return request;
      });
    };

    function customFieldModel(values) {
      var model = {};
      (values || []).forEach(function(value) {
        var fieldValue = value.textValue;
        if (value.type === 'Number') fieldValue = value.numberValue;
        if (value.type === 'Boolean') fieldValue = value.booleanValue;
        if (value.type === 'Date') fieldValue = value.dateValue ? new Date(value.dateValue + 'T00:00:00') : null;
        if (value.type === 'Select') fieldValue = value.optionKey;
        model[value.fieldKey] = fieldValue;
      });
      return model;
    }

    vm.parentCandidates = function(type) {
      var definition = vm.issueTypeDefinition(type);
      if (!definition || definition.hierarchyLevel === 'Epic') return [];
      var parentLevel = definition.hierarchyLevel === 'Subtask' ? 'Standard' : 'Epic';
      return vm.tasks.filter(function(task) {
        var taskDefinition = vm.issueTypeDefinition(task.type);
        return taskDefinition && taskDefinition.hierarchyLevel === parentLevel && !task.archived;
      });
    };

    vm.addRelation = function() {
      if (!vm.selectedTask || !vm.relationDraft.relatedWorkItemId || vm.taskSaving || !vm.canLinkTask() || mutationsUnavailable()) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/relations', vm.relationDraft)
        .then(acceptTaskMutation).then(function() {
          vm.relationDraft = { relatedWorkItemId: '', relationType: 'RelatesTo' };
          vm.notify('success', 'Görev ilişkisi eklendi.');
          return loadTaskStream('activity', true);
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Görev ilişkisi eklenemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.removeRelation = function(relation) {
      if (!vm.selectedTask || !relation || vm.taskSaving || !vm.canLinkTask() || mutationsUnavailable()) return;
      vm.taskSaving = true;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/relations/' + relation.relatedWorkItemId + '?relationType=' + encodeURIComponent(relation.relationType))
        .then(acceptTaskMutation).then(function() {
          vm.notify('success', 'Görev ilişkisi kaldırıldı.');
          return loadTaskStream('activity', true);
        })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Görev ilişkisi kaldırılamadı.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.requestApproval = function() {
      if (!vm.selectedTask || !vm.nextStatus || vm.taskSaving || !vm.canApproveTask() || mutationsUnavailable()) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/approvals', { targetStatus: vm.nextStatus })
        .then(acceptTaskMutation).then(function() {
          vm.notify('success', 'Geçiş onayı istendi.');
          return refreshStreams(['approvals', 'activity']);
        })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Geçiş onayı istenemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.decideApproval = function(approval, approved) {
      if (!vm.selectedTask || !approval || vm.taskSaving || !vm.canApproveTask() || mutationsUnavailable()) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/approvals/' + approval.id + '/decision', {
        approved: approved,
        note: vm.approvalNote || null
      }).then(acceptTaskMutation).then(function() {
        vm.approvalNote = '';
        vm.notify('success', approved ? 'Geçiş onaylandı.' : 'Geçiş reddedildi.');
        return refreshStreams(['approvals', 'timeline', 'activity']);
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Onay kararı kaydedilemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

        }
      };
    });
})();
