(function() {
  'use strict';

  angular.module('zumboDesktop')
    .factory('desktopTaskBoardFeature', function($q, apiClient, realtimeService) {
      function createDemoPassword() {
        var bytes = new Uint8Array(18);
        window.crypto.getRandomValues(bytes);
        return 'Z1!' + Array.prototype.map.call(bytes, function(value) {
          return ('0' + value.toString(16)).slice(-2);
        }).join('');
      }

      return {
        install: function(vm, helpers) {
          var acceptAuth = helpers.acceptAuth;
          var setProjectState = helpers.setProjectState;
          var rememberProject = helpers.rememberProject;
          var membershipFor = helpers.membershipFor;
    vm.seed = function() {
      var stamp = Date.now();
      var organizationId = 'demo-' + String(stamp).slice(-10);
      apiClient.post('/api/browser-auth/register', {
        username: 'desktop' + stamp,
        email: 'desktop' + stamp + '@zumbo.local',
        password: createDemoPassword(),
        organizationId: organizationId
      }).then(function(auth) {
        acceptAuth(auth);
        return apiClient.post('/api/organizations', {
          name: 'Zumbo Demo',
          tenantKey: auth.user.organizationId
        }).then(function() {
          return apiClient.post('/api/projects', {
            organizationId: auth.user.organizationId,
            key: 'DSK' + String(stamp).slice(-7),
            name: 'Zumbo Platform',
            ownerUserId: auth.user.id
          });
        });
      }).then(function(project) {
        vm.projects = [project];
        setProjectState(project);
        window.localStorage.setItem('zumbo.projectId', project.id);
        rememberProject(project);
        return apiClient.post('/api/boards', { projectId: project.id, name: 'Mühendislik Panosu', type: 'Kanban' });
      }).then(function(board) {
        vm.board = board;
        vm.swimlaneMode = board.swimlaneMode || 'None';
        vm.refreshBoardModel();
        vm.loadWorkflow();
        return realtimeService.connect(vm.project.id).catch(angular.noop).then(vm.createTask);
      }).then(vm.loadTasks);
    };

    vm.openEntityCreator = function(kind) {
      vm.createMenuOpen = false;
      if (kind === 'task' && !vm.canCreateTask()) {
        return vm.notify('error', 'Görev oluşturmak için projede düzenleme yetkisi ve seçili pano gerekir.');
      }
      if (kind === 'board' && !vm.canManageProject) {
        return vm.notify('error', 'Pano oluşturmak için proje yönetim yetkisi gerekir.');
      }
      vm.entityCreator = kind;
      vm.entityDraft = kind === 'task'
        ? {
            title: '',
            type: vm.defaultIssueType(),
            priority: 'Medium',
            dueDate: null,
            parentId: '',
            customFieldValues: {}
          }
        : kind === 'project'
          ? { key: '', name: '', visibility: 'Internal' }
          : kind === 'team'
            ? { name: '' }
            : { name: '', type: 'Kanban' };
    };
    vm.closeEntityCreator = function() { vm.entityCreator = null; vm.entityDraft = null; };
    vm.submitEntityCreator = function() {
      if (!vm.entityCreator || vm.entitySaving || !vm.session.currentUser) return;
      var kind = vm.entityCreator;
      var request;
      if (kind === 'task') {
        if (!vm.canCreateTask()) return vm.notify('error', 'Görev oluşturmak için projede düzenleme yetkisi ve seçili pano gerekir.');
        request = apiClient.post('/api/work-items', {
          projectId: vm.project.id,
          boardId: vm.board.id,
          title: vm.entityDraft.title,
          type: vm.entityDraft.type,
          priority: vm.entityDraft.priority,
          assigneeUserId: vm.session.currentUser.id,
          dueDate: vm.entityDraft.dueDate || null,
          parentId: vm.entityDraft.parentId || null,
          customFields: vm.customFieldRequests(vm.entityDraft.type, vm.entityDraft.customFieldValues)
        });
      } else if (kind === 'project') {
        var projectDraft = angular.copy(vm.entityDraft);
        request = apiClient.post('/api/projects', {
          organizationId: vm.session.currentUser.organizationId,
          key: projectDraft.key,
          name: projectDraft.name,
          ownerUserId: vm.session.currentUser.id
        }).then(function(createdProject) {
          return projectDraft.visibility === 'Internal'
            ? createdProject
            : apiClient.put('/api/projects/' + createdProject.id, {
                name: createdProject.name,
                visibility: projectDraft.visibility
              });
        });
      } else if (kind === 'team') {
        request = apiClient.post('/api/teams', {
          organizationId: vm.session.currentUser.organizationId,
          name: vm.entityDraft.name,
          ownerUserId: vm.session.currentUser.id
        });
      } else {
        if (!vm.project || !vm.canManageProject) {
          return vm.notify('error', 'Pano için yönetebildiğiniz bir proje seçin.');
        }
        request = apiClient.post('/api/boards', {
          projectId: vm.project.id,
          name: vm.entityDraft.name,
          type: vm.entityDraft.type
        });
      }
      vm.entitySaving = true;
      return request.then(function(created) {
        vm.closeEntityCreator();
        var followUp;
        if (kind === 'task') followUp = vm.loadTasks();
        else if (kind === 'team') followUp = vm.loadTeams().then(function() { vm.selectTeam(created); });
        if (kind === 'project') {
          vm.projects.push(created);
          followUp = vm.selectProject(created).then(function() { vm.showSection('projects'); });
        }
        if (kind === 'board') {
          vm.boards.push(created);
          followUp = vm.selectBoard(created);
        }
        return $q.when(followUp).then(function() {
          vm.notify('success', entityLabel(kind) + ' oluşturuldu.');
        });
      }).catch(function(error) {
        vm.notify('error', apiActionError(error, entityLabel(kind) + ' oluşturulamadı.'));
      }).finally(function() { vm.entitySaving = false; });
    };

    vm.createTask = function() {
      if (!vm.project || !vm.board || !vm.session.currentUser) {
        return vm.seed();
      }

      return apiClient.post('/api/work-items', {
        projectId: vm.project.id,
        boardId: vm.board.id,
        title: 'Servis sınırını gözden geçir ' + new Date().toLocaleTimeString(),
        type: 'Task',
        priority: 'High',
        assigneeUserId: vm.session.currentUser.id
      }).then(function() {
        return vm.loadTasks();
      });
    };

    function entityLabel(kind) {
      return { task: 'Görev', project: 'Proje', team: 'Ekip', board: 'Pano' }[kind] || 'Kayıt';
    }

    vm.selectedIds = function() {
      return Object.keys(vm.selectedTaskIds).filter(function(id) { return vm.selectedTaskIds[id]; });
    };
    vm.toggleTaskSelection = function(id) { vm.selectedTaskIds[id] = !vm.selectedTaskIds[id]; };
    vm.clearSelection = function() { vm.selectedTaskIds = {}; };
    vm.bulkMove = function(status) {
      var ids = vm.selectedIds();
      if (!ids.length) return;
      return apiClient.post('/api/work-items/bulk/move', { workItemIds: ids, status: status })
        .then(function() { vm.clearSelection(); return vm.loadTasks(); });
    };
    vm.bulkAssignToMe = function() {
      var ids = vm.selectedIds();
      if (!ids.length) return;
      return apiClient.post('/api/work-items/bulk/assign', { workItemIds: ids, assigneeUserId: vm.session.currentUser.id })
        .then(function() { vm.clearSelection(); return vm.loadTasks(); });
    };
    vm.bulkArchive = function() {
      var ids = vm.selectedIds();
      if (!ids.length) return;
      return apiClient.post('/api/work-items/bulk/archive', { workItemIds: ids })
        .then(function() { vm.clearSelection(); vm.selectedTask = null; return vm.loadTasks(); });
    };

    vm.dropTask = function(taskId, column) { return vm.moveTaskToColumn(taskId, column); };
    vm.dropTaskBefore = function(taskId, anchor) {
      if (!anchor || taskId === anchor.id) return;
      var task = vm.tasks.find(function(item) { return item.id === taskId; });
      if (!task || vm.pendingTaskIds[taskId]) return;
      var snapshot = angular.copy(task);
      var statusChanged = task.status !== anchor.status;
      task.status = anchor.status;
      task.columnId = anchor.columnId;
      task.rank = (anchor.rank || 0) - 1;
      vm.pendingTaskIds[taskId] = true;
      vm.refreshBoardModel();
      var move = !statusChanged
        ? $q.when()
        : apiClient.patch('/api/work-items/' + taskId + '/status', { status: anchor.status });
      return move
        .then(function() {
          return apiClient.patch('/api/work-items/' + taskId + '/rank', {
            beforeWorkItemId: anchor.id,
            afterWorkItemId: null
          });
        })
        .then(function() {
          vm.notify('success', 'Görev konumu kaydedildi.');
          return vm.loadTasks();
        })
        .catch(function(error) {
          var compensation = statusChanged
            ? apiClient.patch('/api/work-items/' + taskId + '/status', { status: snapshot.status }).catch(angular.noop)
            : $q.when();
          return compensation.then(function() {
            angular.extend(task, snapshot);
            vm.refreshBoardModel();
            vm.notify('error', movementError(error));
            return vm.loadTasks();
          });
        })
        .finally(function() { delete vm.pendingTaskIds[taskId]; });
    };
    vm.moveTaskToColumn = function(taskId, column) {
      var task = vm.tasks.find(function(item) { return item.id === taskId; });
      if (!task || task.status === column.name || vm.pendingTaskIds[taskId]) return;
      var snapshot = angular.copy(task);
      task.status = column.name;
      task.columnId = column.id;
      task.rank = Number.MAX_SAFE_INTEGER;
      vm.pendingTaskIds[taskId] = true;
      vm.refreshBoardModel();
      return apiClient.patch('/api/work-items/' + taskId + '/status', { status: column.name })
        .then(function() {
          vm.notify('success', 'Görev ' + column.name + ' kolonuna taşındı.');
          return vm.loadTasks();
        })
        .catch(function(error) {
          angular.extend(task, snapshot);
          vm.refreshBoardModel();
          vm.notify('error', movementError(error));
        })
        .finally(function() { delete vm.pendingTaskIds[taskId]; });
    };

    function movementError(error) {
      var code = error.data && error.data.error && error.data.error.code;
      if (code === 'BOARD_WIP_LIMIT_EXCEEDED' || code === 'WIP_LIMIT_EXCEEDED') return 'Kolonun WIP limiti dolu; görev önceki konumuna alındı.';
      if (code === 'WORKFLOW_TRANSITION_FORBIDDEN') return 'Bu durum geçişine izin verilmiyor; görev geri alındı.';
      if (code === 'WORK_ITEM_RANK_EXHAUSTED') return 'Kart sıralaması çakıştı; pano sunucudaki sırayla yenilendi.';
      if (code === 'RESOURCE_BUSY') return 'Görev başka bir işlem tarafından güncelleniyor; değişiklik geri alındı.';
      return 'Görev taşınamadı; önceki konum geri yüklendi.';
    }

    function apiActionError(error, fallback) {
      var apiError = error.data && error.data.error;
      if (!apiError) return fallback;
      if (apiError.code === 'CONCURRENCY_CONFLICT') {
        return 'Bu kayıt başka bir kullanıcı tarafından değiştirildi. Güncel veriler yüklendi; değişikliğinizi yeniden uygulayın.';
      }
      var messages = {
        TEAM_NAME_EXISTS: 'Bu ekip adı zaten kullanılıyor.',
        PROJECT_KEY_EXISTS: 'Bu proje anahtarı zaten kullanılıyor.',
        BOARD_NAME_EXISTS: 'Bu pano adı zaten kullanılıyor.',
        BOARD_IN_USE: 'Aktif görevleri olan pano arşivlenemez.',
        WORK_ITEM_HAS_ACTIVE_CHILDREN: 'Aktif alt görevi olan kayıt arşivlenemez.',
        FORBIDDEN: 'Bu işlem için yetkiniz yok.',
        VALIDATION_ERROR: 'Form alanlarını kontrol edin.'
      };
      return messages[apiError.code] || apiError.message || fallback;
    }
    vm.handleTaskKey = function(event, task) {
      if (event.key === 'Enter') { event.preventDefault(); vm.selectTask(task); return; }
      if (event.key === ' ' && !event.altKey) { event.preventDefault(); vm.toggleTaskSelection(task.id); return; }
      if (!event.altKey || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) return;
      event.preventDefault();
      var columns = vm.board.columns;
      var currentIndex = columns.findIndex(function(column) { return column.id === task.columnId || column.name === task.status; });
      var direction = event.key === 'ArrowLeft' ? -1 : 1;
      var target = columns[currentIndex + direction];
      if (target) vm.moveTaskToColumn(task.id, target);
    };

    vm.loadTasks = function(page, append) {
      if (!vm.project) {
        return $q.when();
      }

      var projectId = vm.project.id;
      var requestId = ++vm.taskLoadRequestId;
      function isCurrentLoad() {
        return vm.project && vm.project.id === projectId && vm.taskLoadRequestId === requestId;
      }
      page = Number.isInteger(page) && page > 0 ? page : 1;
      append = append === true;
      vm.loading = true;
      vm.loadError = null;
      vm.searchDegraded = false;
      var pageSize = 100;
      var loadPromise = apiClient.post(
        '/api/work-items/search',
        {
          projectId: projectId,
          text: vm.search || null,
          page: page,
          pageSize: pageSize
        },
        { scope: 'desktop-task-load', replace: true }
      ).then(function(result) {
        if (!isCurrentLoad()) return $q.reject({ staleTaskLoad: true });
        var tasks = result.items || [];
        vm.searchDegraded = result.degraded === true;
        vm.taskPage = page;
        vm.taskTotalCount = Number.isInteger(result.totalCount) ? result.totalCount : tasks.length;
        vm.hasMoreTasks = page * pageSize < vm.taskTotalCount;
        vm.tasks = append ? vm.tasks.concat(tasks.filter(function(task) {
          return !vm.tasks.some(function(existing) { return existing.id === task.id; });
        })) : tasks;
        realtimeService.synchronize(vm.tasks);
        vm.refreshBoardModel();
        if (append) return null;
        return apiClient.get('/api/work-items/reports/project-summary/' + projectId);
      }).then(function(summary) {
        if (!isCurrentLoad()) return $q.reject({ staleTaskLoad: true });
        if (append) return null;
        vm.summary = summary;
        return $q.all([vm.loadReports(projectId), vm.loadWorkflow(projectId)]);
      }).catch(function(error) {
        if (!isCurrentLoad() || error.staleTaskLoad) return;
        var code = error.data && error.data.error && error.data.error.code;
        vm.loadError = code === 'FORBIDDEN' ? 'Bu pano için görüntüleme yetkiniz yok.' : 'Pano verileri yüklenemedi.';
      }).finally(function() {
        if (isCurrentLoad()) vm.loading = false;
      });
      vm.activeTaskLoad = loadPromise;
      return loadPromise.finally(function() {
        if (vm.activeTaskLoad === loadPromise) vm.activeTaskLoad = null;
      });
    };
    vm.loadMoreTasks = function() {
      if (vm.loading || !vm.hasMoreTasks) return;
      return vm.loadTasks((vm.taskPage || 1) + 1, true);
    };

    vm.loadArchivedTasks = function() {
      if (!vm.session.currentUser) return $q.when();
      vm.archiveLoading = true;
      var organizationId = encodeURIComponent(vm.session.currentUser.organizationId);
      var canReadProjectResources = !!membershipFor(vm.project);
      var taskRequest = canReadProjectResources
        ? apiClient.get('/api/work-items?projectId=' + vm.project.id + '&archived=true&page=1&pageSize=100')
        : $q.when([]);
      var boardRequest = canReadProjectResources
        ? apiClient.get('/api/boards/by-project/' + vm.project.id + '?archived=true')
        : $q.when([]);

      function settled(request) {
        return request.then(
          function(data) { return { ok: true, data: data }; },
          function(error) { return { ok: false, error: error }; }
        );
      }

      return $q.all([
        settled(taskRequest),
        settled(apiClient.get('/api/projects?organizationId=' + organizationId + '&archived=true')),
        settled(apiClient.get('/api/teams?organizationId=' + organizationId + '&archived=true')),
        settled(boardRequest)
      ]).then(function(result) {
        vm.archivedTasks = result[0].ok ? result[0].data : [];
        vm.archivedProjects = result[1].ok ? result[1].data : [];
        vm.archivedTeams = result[2].ok ? result[2].data : [];
        vm.archivedBoards = result[3].ok ? result[3].data : [];
        if (result.some(function(item) { return !item.ok; })) {
          vm.notify('error', 'Arşivin bazı bölümleri yüklenemedi.');
        }
      })
        .finally(function() { vm.archiveLoading = false; });
    };

    vm.restoreTask = function(task) {
      if (!task || vm.archiveActionId) return;
      vm.archiveActionId = task.id;
      return apiClient.post('/api/work-items/' + task.id + '/restore', {})
        .then(function() {
          return $q.all([vm.loadArchivedTasks(), vm.loadTasks()]);
        })
        .then(function() { vm.notify('success', 'Görev panoya geri yüklendi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Görev geri yüklenemedi.')); })
        .finally(function() { vm.archiveActionId = null; });
    };

    vm.restoreLifecycleEntity = function(kind, entity) {
      if (!entity || vm.archiveActionId) return;
      vm.archiveActionId = entity.id;
      return apiClient.post('/api/' + kind + '/' + entity.id + '/restore', {})
        .then(function() {
          if (kind === 'projects') return vm.reloadProjects();
          if (kind === 'teams') return vm.loadTeams();
          return vm.loadBoards();
        }).then(vm.loadArchivedTasks)
        .then(function() {
          vm.notify('success', entityLabel(kind === 'projects' ? 'project' : kind === 'teams' ? 'team' : 'board') + ' geri yüklendi.');
        })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Kayıt geri yüklenemedi.')); })
        .finally(function() { vm.archiveActionId = null; });
    };

    vm.loadNotifications = function() {
      if (!vm.session.currentUser) return;
      return apiClient.get('/api/notifications?page=1&pageSize=20').then(function(notifications) {
        vm.notifications = notifications;
        vm.unreadCount = notifications.filter(function(notification) { return !notification.read; }).length;
      });
    };
    vm.readNotification = function(notification) {
      if (notification.read) return;
      return apiClient.patch('/api/notifications/' + notification.id + '/read', {}).then(function() {
        notification.read = true;
        vm.unreadCount = Math.max(0, vm.unreadCount - 1);
      });
    };

          return { apiActionError: apiActionError };
        }
      };
    });
})();
