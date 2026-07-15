function createDemoPassword() {
  var bytes = new Uint8Array(18);
  window.crypto.getRandomValues(bytes);
  return 'Z1!' + Array.prototype.map.call(bytes, function(value) {
    return ('0' + value.toString(16)).slice(-2);
  }).join('');
}

angular.module('zumboDesktop', [])
  .constant('API_BASE_URL', window.localStorage.getItem('zumbo.apiBaseUrl') || 'http://localhost:5088')
  .factory('sessionStore', function() {
    var currentUser = JSON.parse(window.localStorage.getItem('zumbo.currentUser') || 'null');
    var accessToken = window.localStorage.getItem('zumbo.accessToken');
    var refreshToken = window.localStorage.getItem('zumbo.refreshToken');
    return { currentUser: currentUser, accessToken: accessToken, refreshToken: refreshToken };
  })
  .factory('apiClient', function($http, $q, API_BASE_URL) {
    var refreshPromise = null;
    function unwrap(promise) {
      return promise.then(function(response) { return response.data.data; });
    }
    function config() {
      var token = window.localStorage.getItem('zumbo.accessToken');
      return token ? { headers: { Authorization: 'Bearer ' + token } } : {};
    }
    function request(httpConfig) {
      return $http(httpConfig).catch(function(error) {
        var refreshToken = window.localStorage.getItem('zumbo.refreshToken');
        var publicAuthCall = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh']
          .some(function(path) { return httpConfig.url === API_BASE_URL + path; });
        if (publicAuthCall || error.status !== 401 || !refreshToken) { return $q.reject(error); }
        if (!refreshPromise) {
          refreshPromise = $http.post(API_BASE_URL + '/api/auth/refresh', { refreshToken: refreshToken })
            .then(function(response) {
              var auth = response.data.data;
              window.localStorage.setItem('zumbo.accessToken', auth.accessToken);
              window.localStorage.setItem('zumbo.refreshToken', auth.refreshToken);
            }).catch(function(refreshError) {
              window.localStorage.removeItem('zumbo.accessToken');
              window.localStorage.removeItem('zumbo.refreshToken');
              return $q.reject(refreshError);
            }).finally(function() { refreshPromise = null; });
        }
        return refreshPromise.then(function() {
          httpConfig.headers = httpConfig.headers || {};
          httpConfig.headers.Authorization = 'Bearer ' + window.localStorage.getItem('zumbo.accessToken');
          return $http(httpConfig);
        });
      });
    }
    return {
      get: function(url) { return unwrap(request(angular.extend(config(), { method: 'GET', url: API_BASE_URL + url }))); },
      post: function(url, data) { return unwrap(request(angular.extend(config(), { method: 'POST', url: API_BASE_URL + url, data: data }))); },
      put: function(url, data) { return unwrap(request(angular.extend(config(), { method: 'PUT', url: API_BASE_URL + url, data: data }))); },
      patch: function(url, data) { return unwrap(request(angular.extend(config(), { method: 'PATCH', url: API_BASE_URL + url, data: data }))); },
      delete: function(url) { return unwrap(request(angular.extend(config(), { method: 'DELETE', url: API_BASE_URL + url }))); },
      upload: function(url, file) {
        var form = new FormData();
        form.append('file', file);
        var requestConfig = config();
        requestConfig.headers = requestConfig.headers || {};
        requestConfig.headers['Content-Type'] = undefined;
        requestConfig.transformRequest = angular.identity;
        requestConfig.method = 'POST';
        requestConfig.url = API_BASE_URL + url;
        requestConfig.data = form;
        return unwrap(request(requestConfig));
      },
      download: function(url) {
        var requestConfig = config();
        requestConfig.responseType = 'blob';
        requestConfig.method = 'GET';
        requestConfig.url = API_BASE_URL + url;
        return request(requestConfig).then(function(response) { return response.data; });
      }
    };
  })
  .factory('realtimeService', function($q, $rootScope, API_BASE_URL) {
    var connection = null;
    var projectId = null;
    var listeners = [];

    function notify(change) {
      $rootScope.$evalAsync(function() {
        listeners.slice().forEach(function(listener) { listener(change); });
      });
    }

    function connect(nextProjectId) {
      if (!window.signalR) { return $q.reject(new Error('Realtime client is unavailable.')); }
      if (connection && projectId === nextProjectId) { return $q.when(); }
      var stop = connection ? connection.stop() : $q.when();
      return $q.when(stop).then(function() {
        projectId = nextProjectId;
        connection = new signalR.HubConnectionBuilder()
          .withUrl(API_BASE_URL + '/hubs/work-items', {
            accessTokenFactory: function() { return window.localStorage.getItem('zumbo.accessToken') || ''; },
            withCredentials: false
          })
          .withAutomaticReconnect([0, 2000, 5000, 10000])
          .build();
        connection.on('workItemChanged', notify);
        connection.onreconnected(function() {
          if (projectId) { connection.invoke('SubscribeProject', projectId); }
        });
        return connection.start().then(function() {
          return connection.invoke('SubscribeProject', projectId);
        });
      });
    }

    return {
      connect: connect,
      subscribe: function(listener) {
        listeners.push(listener);
        return function() {
          var index = listeners.indexOf(listener);
          if (index >= 0) { listeners.splice(index, 1); }
        };
      },
      stop: function() {
        projectId = null;
        if (!connection) { return $q.when(); }
        var active = connection;
        connection = null;
        return active.stop();
      }
    };
  })
  .controller('WorkspaceController', function($scope, $window, $document, $timeout, $q, apiClient, sessionStore, realtimeService) {
    var vm = this;
    vm.session = sessionStore;
    vm.tasks = [];
    vm.audit = [];
    vm.boardAudit = [];
    vm.organizationAudit = [];
    vm.roles = [];
    vm.roleDraft = { name: '', permissions: ['WorkItemView'] };
    vm.userRoleDrafts = {};
    vm.permissionCatalog = [
      'UserRoleManage', 'AuditReadAll', 'OrganizationManage',
      'BoardView', 'BoardManage', 'WorkItemView', 'WorkItemCreate',
      'WorkItemUpdate', 'WorkItemMove', 'WorkItemAssign', 'WorkItemApprove',
      'WorkItemDelete', 'CommentCreate', 'AttachmentCreate',
      'AttachmentDelete', 'WorkLogCreate'
    ];
    vm.summary = {};
    vm.statusDistribution = [];
    vm.workload = [];
    vm.dueDateRisks = [];
    vm.velocity = [];
    vm.burndown = [];
    vm.workflow = { transitions: [] };
    vm.nextStatus = 'In Progress';
    vm.activeViewId = '';
    vm.priorityFilter = '';
    vm.viewDraftName = '';
    vm.boardRows = [];
    vm.activeSection = 'board';
    vm.commentBody = '';
    vm.checklistText = '';
    vm.labelText = '';
    vm.workLogDraft = { hours: null, note: '' };
    vm.loginForm = { usernameOrEmail: '', password: '', mfaCode: '' };
    vm.projects = [];
    vm.teams = [];
    vm.users = [];
    vm.boards = [];
    vm.notifications = [];
    vm.unreadCount = 0;
    vm.selectedTaskIds = {};
    vm.archivedTasks = [];
    vm.archivedProjects = [];
    vm.archivedTeams = [];
    vm.archivedBoards = [];
    vm.pendingTaskIds = {};
    vm.taskLoadRequestId = 0;
    vm.activeTaskLoad = null;
    vm.projectMemberDraft = { userId: '', role: 'Developer' };
    vm.projectTeamId = '';
    vm.boardColumnDraft = { name: '', category: 'Custom', wipLimit: null };
    vm.workflowDraft = { statuses: [], transitions: [] };
    vm.settingsTab = 'account';
    vm.organizations = [];
    vm.organization = null;
    vm.organizationDraft = { name: '', tenantKey: '' };
    vm.departmentDraft = { name: '', parentDepartmentId: null };
    vm.departmentMemberDraft = { departmentId: '', userId: '', position: '' };
    vm.relationDraft = { relatedWorkItemId: '', relationType: 'RelatesTo' };
    vm.approvalNote = '';
    vm.passwordDraft = { currentPassword: '', newPassword: '' };
    vm.mfaStatus = { enabled: false, remainingRecoveryCodes: 0 };
    vm.mfaDraft = { password: '', code: '' };
    vm.mfaSetup = null;
    vm.recoveryCodes = [];
    vm.apiKeys = [];
    vm.apiKeyDraft = { name: '', password: '', mfaCode: '', expiresInDays: 90 };
    vm.createdApiKey = null;
    vm.notificationPreferences = { inAppEnabled: true, emailEnabled: false, mutedTypes: [] };
    vm.privacyDraft = { password: '', confirmation: '' };
    vm.collapsedColumns = JSON.parse(window.localStorage.getItem('zumbo.collapsedColumns') || '{}');
    vm.cardFields = angular.extend({ type: true, priority: true, assignee: true, dueDate: true, labels: true },
      JSON.parse(window.localStorage.getItem('zumbo.cardFields') || '{}'));
    vm.favoriteProjects = JSON.parse(window.localStorage.getItem('zumbo.favoriteProjects') || '[]');
    vm.theme = window.localStorage.getItem('zumbo.theme') || 'light';
    vm.density = window.localStorage.getItem('zumbo.density') || 'comfortable';
    vm.navCollapsed = window.localStorage.getItem('zumbo.navCollapsed') === 'true';
    vm.recentProjects = JSON.parse(window.localStorage.getItem('zumbo.recentProjects') || '[]');
    vm.commands = [
      { label: 'Panoyu aç', group: 'Navigasyon', icon: 'kanban', action: 'section', value: 'board' },
      { label: 'Projeleri aç', group: 'Navigasyon', icon: 'folder-kanban', action: 'section', value: 'projects' },
      { label: 'Ekipleri aç', group: 'Navigasyon', icon: 'users-round', action: 'section', value: 'teams' },
      { label: 'Raporları aç', group: 'Navigasyon', icon: 'chart-no-axes-combined', action: 'section', value: 'reports' },
      { label: 'Ayarları aç', group: 'Navigasyon', icon: 'settings', action: 'section', value: 'settings' },
      { label: 'Arşivi aç', group: 'Navigasyon', icon: 'archive-restore', action: 'section', value: 'archive' },
      { label: 'Yeni görev oluştur', group: 'Eylem', icon: 'plus', action: 'create' },
      { label: 'Temayı değiştir', group: 'Görünüm', icon: 'sun-moon', action: 'theme' },
      { label: 'Kart yoğunluğunu değiştir', group: 'Görünüm', icon: 'rows-3', action: 'density' }
    ];
    vm.showSection = function(section) {
      vm.activeSection = section;
      vm.closeCommandPalette();
      updateLocation(section, null, false);
      if (section === 'archive') vm.loadArchivedTasks();
      if (section === 'teams') vm.loadTeams();
      if (section === 'projects') vm.loadUsers();
      if (section === 'settings') vm.loadSettings();
    };

    vm.notify = function(kind, message) {
      vm.feedback = { kind: kind, message: message };
      $timeout.cancel(vm.feedbackTimer);
      vm.feedbackTimer = $timeout(function() { vm.feedback = null; }, 5000);
    };

    vm.toggleTheme = function() {
      vm.theme = vm.theme === 'dark' ? 'light' : 'dark';
      window.localStorage.setItem('zumbo.theme', vm.theme);
    };
    vm.toggleNav = function() {
      vm.navCollapsed = !vm.navCollapsed;
      window.localStorage.setItem('zumbo.navCollapsed', String(vm.navCollapsed));
    };
    vm.setDensity = function(density) {
      vm.density = density;
      window.localStorage.setItem('zumbo.density', density);
    };
    vm.saveCardFields = function() {
      window.localStorage.setItem('zumbo.cardFields', JSON.stringify(vm.cardFields));
    };
    vm.toggleColumn = function(column) {
      if (!vm.board || !column) return;
      var key = vm.board.id + ':' + column.id;
      vm.collapsedColumns[key] = !vm.collapsedColumns[key];
      window.localStorage.setItem('zumbo.collapsedColumns', JSON.stringify(vm.collapsedColumns));
      vm.refreshBoardModel();
    };
    vm.isFavoriteProject = function(project) {
      return !!project && vm.favoriteProjects.some(function(item) { return item.id === project.id; });
    };
    vm.toggleFavoriteProject = function(project) {
      if (!project) return;
      vm.favoriteProjects = vm.isFavoriteProject(project)
        ? vm.favoriteProjects.filter(function(item) { return item.id !== project.id; })
        : [project].concat(vm.favoriteProjects).slice(0, 12);
      window.localStorage.setItem('zumbo.favoriteProjects', JSON.stringify(vm.favoriteProjects));
    };
    vm.openCommandPalette = function() { vm.commandOpen = true; vm.commandQuery = ''; };
    vm.closeCommandPalette = function() { vm.commandOpen = false; };
    vm.filteredCommands = function() {
      var query = (vm.commandQuery || '').toLowerCase();
      return vm.commands.filter(function(command) { return !query || command.label.toLowerCase().indexOf(query) >= 0; });
    };
    vm.runCommand = function(command) {
      if (command.action === 'section') vm.showSection(command.value);
      if (command.action === 'create') vm.openEntityCreator('task');
      if (command.action === 'theme') vm.toggleTheme();
      if (command.action === 'density') vm.setDensity(vm.density === 'compact' ? 'comfortable' : 'compact');
      if (command.action === 'task') vm.selectTask(command.task);
      vm.closeCommandPalette();
    };

    function onGlobalKeydown(event) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        $scope.$applyAsync(vm.openCommandPalette);
      } else if (event.key === 'Escape' && vm.commandOpen) {
        $scope.$applyAsync(vm.closeCommandPalette);
      }
    }
    function updateLocation(section, taskId, push) {
      var params = new URLSearchParams();
      params.set('section', section || 'board');
      if (vm.project) params.set('project', vm.project.id);
      if (vm.board && (section === 'board' || section === 'projects')) params.set('board', vm.board.id);
      if (vm.selectedTeam && section === 'teams') params.set('team', vm.selectedTeam.id);
      if (taskId) params.set('task', taskId);
      var next = '#' + params.toString();
      if ($window.location.hash === next) return;
      $window.history[push ? 'pushState' : 'replaceState'](null, '', next);
    }
    function applyLocation() {
      var params = new URLSearchParams($window.location.hash.slice(1));
      var section = params.get('section');
      if (['board', 'projects', 'teams', 'reports', 'audit', 'archive', 'settings'].indexOf(section) >= 0) {
        vm.activeSection = section;
      }
      var projectId = params.get('project');
      if (projectId && vm.projects.length && (!vm.project || vm.project.id !== projectId)) {
        var linkedProject = vm.projects.find(function(project) { return project.id === projectId; });
        if (linkedProject && membershipFor(linkedProject)) {
          vm.selectProject(linkedProject, true).then(applyLocation);
          return;
        }
      }
      var taskId = params.get('task');
      if (taskId && vm.session.currentUser) vm.selectTask({ id: taskId }, true);
      else if (!taskId) { vm.selectedTask = null; vm.taskDraft = null; }
      var teamId = params.get('team');
      if (teamId && vm.teams.length) {
        var linkedTeam = vm.teams.find(function(team) { return team.id === teamId; });
        if (linkedTeam && (!vm.selectedTeam || vm.selectedTeam.id !== linkedTeam.id)) vm.selectTeam(linkedTeam, true);
      }
      if (vm.activeSection === 'archive' && vm.project) vm.loadArchivedTasks();
    }
    function onPopState() { $scope.$applyAsync(applyLocation); }
    $document.on('keydown', onGlobalKeydown);
    $window.addEventListener('popstate', onPopState);

    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (!vm.board || change.boardId !== vm.board.id) { return; }
      if (vm.pendingTaskIds[change.workItemId]) { return; }
      var index = vm.tasks.findIndex(function(task) { return task.id === change.workItemId; });
      if (change.eventType === 'archived') {
        if (index >= 0) { vm.tasks.splice(index, 1); }
      } else if (index >= 0) {
        vm.tasks[index] = angular.extend({}, vm.tasks[index], change.workItem);
      } else {
        vm.tasks.push(angular.extend({ description: '', labels: [] }, change.workItem));
      }
      vm.tasks.sort(function(left, right) { return (left.rank || 0) - (right.rank || 0); });
      if (vm.selectedTask && vm.selectedTask.id === change.workItemId) {
        if (change.eventType === 'archived') { vm.closeTask(); }
        else { vm.selectTask(change.workItem); }
      }
      vm.refreshBoardModel();
    });
    $scope.$on('$destroy', function() {
      unsubscribeRealtime();
      realtimeService.stop();
      $document.off('keydown', onGlobalKeydown);
      $window.removeEventListener('popstate', onPopState);
    });

    function acceptAuth(auth) {
      vm.session.currentUser = auth.user;
      vm.session.accessToken = auth.accessToken;
      vm.session.refreshToken = auth.refreshToken;
      window.localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
      window.localStorage.setItem('zumbo.accessToken', auth.accessToken);
      window.localStorage.setItem('zumbo.refreshToken', auth.refreshToken);
      return auth;
    }

    vm.login = function() {
      vm.loginError = null;
      return apiClient.post('/api/auth/login', vm.loginForm)
        .then(acceptAuth)
        .then(vm.restore)
        .catch(function(error) {
          var code = error.data && error.data.error && error.data.error.code;
          vm.mfaRequired = code === 'MFA_REQUIRED' || code === 'MFA_INVALID';
          vm.loginError = vm.mfaRequired ? 'Doğrulama kodunu kontrol edin.' : 'Giriş başarısız.';
        });
    };

    vm.logout = function() {
      var refreshToken = window.localStorage.getItem('zumbo.refreshToken');
      var request = refreshToken
        ? apiClient.post('/api/auth/logout', { refreshToken: refreshToken, allSessions: false })
        : $q.when();
      return request.finally(function() {
        realtimeService.stop();
        vm.session.currentUser = null;
        vm.session.accessToken = null;
        vm.session.refreshToken = null;
        vm.project = null;
        vm.board = null;
        vm.tasks = [];
        window.localStorage.removeItem('zumbo.currentUser');
        window.localStorage.removeItem('zumbo.accessToken');
        window.localStorage.removeItem('zumbo.refreshToken');
      });
    };

    vm.seed = function() {
      var stamp = Date.now();
      var organizationId = 'demo-' + String(stamp).slice(-10);
      apiClient.post('/api/auth/register', {
        username: 'desktop' + stamp,
        email: 'desktop' + stamp + '@zumbo.local',
        password: createDemoPassword(),
        organizationId: organizationId
      }).then(function(auth) {
        acceptAuth(auth);
        return apiClient.post('/api/projects', {
          organizationId: auth.user.organizationId,
          key: 'DSK' + String(stamp).slice(-7),
          name: 'Zumbo Platform',
          ownerUserId: auth.user.id
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
      if (kind === 'board' && !vm.canManageProject) {
        return vm.notify('error', 'Pano oluşturmak için proje yönetim yetkisi gerekir.');
      }
      vm.entityCreator = kind;
      vm.entityDraft = kind === 'task'
        ? { title: '', type: 'Task', priority: 'Medium', dueDate: null, parentId: '' }
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
        if (!vm.project || !vm.board) return vm.notify('error', 'Görev için proje ve pano seçin.');
        request = apiClient.post('/api/work-items', {
          projectId: vm.project.id,
          boardId: vm.board.id,
          title: vm.entityDraft.title,
          type: vm.entityDraft.type,
          priority: vm.entityDraft.priority,
          assigneeUserId: vm.session.currentUser.id,
          dueDate: vm.entityDraft.dueDate || null,
          parentId: vm.entityDraft.parentId || null
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
      }).then(function(task) {
        return apiClient.patch('/api/work-items/' + task.id + '/planning', {
          sprintId: currentSprintId(),
          estimatePoints: 5
        });
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
      var text = vm.search ? '&text=' + encodeURIComponent(vm.search) : '';
      var pageSize = 100;
      var loadPromise = apiClient.get('/api/work-items?projectId=' + projectId + text + '&page=' + page + '&pageSize=' + pageSize).then(function(tasks) {
        if (!isCurrentLoad()) return $q.reject({ staleTaskLoad: true });
        vm.taskPage = page;
        vm.hasMoreTasks = tasks.length === pageSize;
        vm.tasks = append ? vm.tasks.concat(tasks.filter(function(task) {
          return !vm.tasks.some(function(existing) { return existing.id === task.id; });
        })) : tasks;
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

    vm.loadSettings = function() {
      if (!vm.session.currentUser) return $q.when();
      vm.settingsLoading = true;
      return $q.all([
        apiClient.get('/api/organizations').then(function(organizations) {
          vm.organizations = organizations;
          vm.organization = organizations.find(function(item) {
            return item.tenantKey === vm.session.currentUser.organizationId || item.id === vm.session.currentUser.organizationId;
          }) || organizations[0] || null;
          vm.organizationDraft = vm.organization
            ? { name: vm.organization.name, tenantKey: vm.organization.tenantKey }
            : { name: '', tenantKey: vm.session.currentUser.organizationId };
          return vm.loadOrganizationAudit();
        }).catch(function() { vm.organizations = []; vm.organization = null; }),
        apiClient.get('/api/auth/mfa').then(function(status) { vm.mfaStatus = status; }).catch(angular.noop),
        apiClient.get('/api/auth/api-keys').then(function(keys) { vm.apiKeys = keys; }).catch(function() { vm.apiKeys = []; }),
        apiClient.get('/api/notifications/preferences/me').then(function(preferences) {
          vm.notificationPreferences = preferences;
          vm.mutedTypesText = (preferences.mutedTypes || []).join(', ');
        }).catch(angular.noop),
        vm.loadUsers(),
        vm.loadRoleAdministration()
      ]).finally(function() { vm.settingsLoading = false; });
    };

    function currentUserHasPermission(permission) {
      var currentRoles = (vm.session.currentUser && vm.session.currentUser.roles) || [];
      if (currentRoles.some(function(role) { return role === 'SystemAdmin'; })) return true;
      if (permission === 'UserRoleManage'
          && currentRoles.some(function(role) { return role === 'OrganizationAdmin'; })) return true;
      return vm.roles.some(function(role) {
        return currentRoles.indexOf(role.name) >= 0
          && (role.permissions || []).some(function(value) { return value === '*' || value === permission; });
      });
    }

    function refreshManagementCapabilities() {
      var currentRoles = (vm.session.currentUser && vm.session.currentUser.roles) || [];
      vm.isSystemAdmin = currentRoles.indexOf('SystemAdmin') >= 0;
      vm.canManageRoles = currentUserHasPermission('UserRoleManage');
      vm.canManageOrganization = vm.isSystemAdmin
        || currentRoles.indexOf('OrganizationAdmin') >= 0
        || currentUserHasPermission('OrganizationManage')
        || !!(vm.organization && vm.organization.ownerUserId === vm.session.currentUser.id);
      if (!vm.canManageRoles && vm.settingsTab === 'access') vm.settingsTab = 'account';
    }

    function prepareRoleAdministration() {
      vm.roles.forEach(function(role) {
        role.editName = role.name;
        role.editPermissions = (role.permissions || []).slice();
      });
      vm.userRoleDrafts = {};
      vm.users.forEach(function(user) { vm.userRoleDrafts[user.id] = (user.roles || []).slice(); });
      refreshManagementCapabilities();
    }

    vm.loadRoleAdministration = function() {
      if (!vm.session.currentUser) return $q.when([]);
      vm.roleAdminLoading = true;
      return apiClient.get('/api/auth/roles').then(function(roles) {
        vm.roles = roles;
        prepareRoleAdministration();
        return roles;
      }).catch(function() {
        vm.roles = [];
        refreshManagementCapabilities();
        return [];
      }).finally(function() { vm.roleAdminLoading = false; });
    };

    vm.permissionSelected = function(role, permission) {
      return ((role && role.permissions) || []).indexOf(permission) >= 0;
    };

    vm.togglePermission = function(role, permission) {
      var permissions = role.permissions || (role.permissions = []);
      var index = permissions.indexOf(permission);
      if (index >= 0) permissions.splice(index, 1);
      else permissions.push(permission);
    };

    vm.createRole = function() {
      if (!vm.canManageRoles || !vm.roleDraft.name || !vm.roleDraft.permissions.length || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/roles', {
        name: vm.roleDraft.name,
        organizationId: vm.session.currentUser.organizationId,
        permissions: vm.roleDraft.permissions
      }).then(function() {
        vm.roleDraft = { name: '', permissions: ['WorkItemView'] };
        vm.notify('success', 'Özel rol oluşturuldu.');
        return vm.loadRoleAdministration();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Rol oluşturulamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.saveRole = function(role) {
      if (!vm.canManageRoles || !role || role.isSystem || !role.editName || !role.editPermissions.length || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/auth/roles/' + role.id, {
        name: role.editName,
        permissions: role.editPermissions
      }).then(function() {
        vm.notify('success', 'Rol güncellendi.');
        return $q.all([vm.loadRoleAdministration(), vm.loadUsers()]).then(prepareRoleAdministration);
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Rol güncellenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.deleteRole = function(role) {
      if (!vm.canManageRoles || !role || role.isSystem || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/auth/roles/' + role.id).then(function() {
        vm.notify('success', 'Rol kaldırıldı.');
        return vm.loadRoleAdministration();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Rol kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.userHasDraftRole = function(user, roleName) {
      return ((user && vm.userRoleDrafts[user.id]) || []).indexOf(roleName) >= 0;
    };

    vm.toggleUserRole = function(user, roleName) {
      if (!user || user.id === vm.session.currentUser.id) return;
      var roles = vm.userRoleDrafts[user.id] || (vm.userRoleDrafts[user.id] = ['User']);
      var index = roles.indexOf(roleName);
      if (index >= 0) roles.splice(index, 1);
      else roles.push(roleName);
      if (roles.indexOf('User') < 0) roles.unshift('User');
    };

    vm.canAssignRole = function(role) {
      if (!role || vm.isSystemAdmin) return true;
      return !role.isSystem || ['SystemAdmin', 'OrganizationAdmin', 'AuditReader'].indexOf(role.name) < 0;
    };

    vm.saveUserRoles = function(user) {
      if (!vm.canManageRoles || !user || user.id === vm.session.currentUser.id || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/auth/users/' + user.id + '/roles', {
        roles: vm.userRoleDrafts[user.id] || ['User']
      }).then(function(updated) {
        var index = vm.users.findIndex(function(item) { return item.id === updated.id; });
        if (index >= 0) vm.users[index] = updated;
        vm.userRoleDrafts[updated.id] = (updated.roles || []).slice();
        vm.notify('success', 'Kullanıcı rolleri güncellendi.');
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Kullanıcı rolleri güncellenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.loadOrganizationAudit = function() {
      if (!vm.organization) return $q.when([]);
      return apiClient.get('/api/audit/entity/Organization/' + vm.organization.id)
        .then(function(audit) { vm.organizationAudit = audit; refreshManagementCapabilities(); return audit; })
        .catch(function() { vm.organizationAudit = []; return []; });
    };

    vm.saveOrganization = function() {
      if (!vm.organizationDraft.name || vm.entitySaving) return;
      vm.entitySaving = true;
      var request = vm.organization
        ? apiClient.put('/api/organizations/' + vm.organization.id, { name: vm.organizationDraft.name })
        : apiClient.post('/api/organizations', vm.organizationDraft);
      return request.then(function(organization) {
        vm.organization = organization;
        vm.organizationDraft = { name: organization.name, tenantKey: organization.tenantKey };
        vm.notify('success', 'Organizasyon kaydedildi.');
        return vm.loadOrganizationAudit();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Organizasyon kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.addDepartment = function() {
      if (!vm.organization || !vm.departmentDraft.name || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/organizations/' + vm.organization.id + '/departments', vm.departmentDraft)
        .then(function(organization) {
          vm.organization = organization;
          vm.departmentDraft = { name: '', parentDepartmentId: null };
          vm.notify('success', 'Departman eklendi.');
          return vm.loadOrganizationAudit();
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Departman eklenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.saveDepartment = function(department) {
      if (!vm.organization || !department || !department.name || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.put('/api/organizations/' + vm.organization.id + '/departments/' + department.id, {
        name: department.name,
        parentDepartmentId: department.parentDepartmentId || null
      }).then(function(organization) { vm.organization = organization; vm.notify('success', 'Departman kaydedildi.'); return vm.loadOrganizationAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Departman kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.deleteDepartment = function(department) {
      if (!vm.organization || !department || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/organizations/' + vm.organization.id + '/departments/' + department.id)
        .then(function(organization) { vm.organization = organization; vm.notify('success', 'Departman kaldırıldı.'); return vm.loadOrganizationAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Departman kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.assignDepartmentMember = function() {
      var draft = vm.departmentMemberDraft;
      if (!vm.organization || !draft.departmentId || !draft.userId || !draft.position || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/organizations/' + vm.organization.id + '/departments/' + draft.departmentId + '/members', {
        userId: draft.userId,
        position: draft.position
      }).then(function(organization) {
        vm.organization = organization;
        vm.departmentMemberDraft = { departmentId: '', userId: '', position: '' };
        vm.notify('success', 'Departman üyesi atandı.');
        return vm.loadOrganizationAudit();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Departman üyesi atanamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.removeDepartmentMember = function(department, member) {
      if (!vm.organization || !department || !member || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/organizations/' + vm.organization.id + '/departments/' + department.id + '/members/' + member.userId)
        .then(function(organization) { vm.organization = organization; vm.notify('success', 'Departman üyesi kaldırıldı.'); return vm.loadOrganizationAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Departman üyesi kaldırılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.changePassword = function() {
      if (!vm.passwordDraft.currentPassword || !vm.passwordDraft.newPassword || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/change-password', vm.passwordDraft)
        .then(function() { vm.passwordDraft = { currentPassword: '', newPassword: '' }; vm.notify('success', 'Parola değiştirildi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Parola değiştirilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.beginMfaSetup = function() {
      if (!vm.mfaDraft.password || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/mfa/setup', { password: vm.mfaDraft.password })
        .then(function(setup) { vm.mfaSetup = setup; vm.notify('success', 'MFA sırrı oluşturuldu.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'MFA kurulumu başlatılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.confirmMfaSetup = function() {
      if (!vm.mfaDraft.code || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/mfa/confirm', { code: vm.mfaDraft.code })
        .then(function(result) {
          vm.mfaStatus = { enabled: result.enabled, remainingRecoveryCodes: result.recoveryCodes.length };
          vm.recoveryCodes = result.recoveryCodes;
          vm.mfaSetup = null;
          vm.mfaDraft = { password: '', code: '' };
          vm.notify('success', 'MFA etkinleştirildi; kurtarma kodlarını güvenli yerde saklayın.');
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'MFA doğrulanamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.disableMfa = function() {
      if (!vm.mfaDraft.password || !vm.mfaDraft.code || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/mfa/disable', { password: vm.mfaDraft.password, code: vm.mfaDraft.code })
        .then(function(status) { vm.mfaStatus = status; vm.mfaDraft = { password: '', code: '' }; vm.notify('success', 'MFA devre dışı bırakıldı.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'MFA devre dışı bırakılamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.createApiKey = function() {
      if (!vm.apiKeyDraft.name || !vm.apiKeyDraft.password || vm.entitySaving) return;
      vm.entitySaving = true;
      var expiresAt = new Date();
      expiresAt.setDate(expiresAt.getDate() + Number(vm.apiKeyDraft.expiresInDays || 90));
      return apiClient.post('/api/auth/api-keys', {
        name: vm.apiKeyDraft.name,
        password: vm.apiKeyDraft.password,
        mfaCode: vm.apiKeyDraft.mfaCode || null,
        expiresAt: expiresAt.toISOString(),
        scopes: ['api:full']
      }).then(function(created) {
        vm.createdApiKey = created;
        vm.apiKeyDraft = { name: '', password: '', mfaCode: '', expiresInDays: 90 };
        return apiClient.get('/api/auth/api-keys').then(function(keys) { vm.apiKeys = keys; });
      }).then(function() { vm.notify('success', 'API anahtarı oluşturuldu; tam değer yalnızca bir kez gösterilir.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'API anahtarı oluşturulamadı.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.revokeApiKey = function(key) {
      if (!key || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/auth/api-keys/' + key.id)
        .then(function() { return apiClient.get('/api/auth/api-keys'); })
        .then(function(keys) {
          vm.apiKeys = keys;
          vm.createdApiKey = null;
          vm.notify('success', 'API anahtarı iptal edildi.');
        })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'API anahtarı iptal edilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.copyText = function(value) {
      if (!value || !$window.navigator.clipboard) return;
      return $window.navigator.clipboard.writeText(value).then(function() { vm.notify('success', 'Değer panoya kopyalandı.'); });
    };

    vm.saveNotificationPreferences = function() {
      if (vm.entitySaving) return;
      vm.entitySaving = true;
      var mutedTypes = (vm.mutedTypesText || '').split(',').map(function(item) { return item.trim(); }).filter(Boolean);
      return apiClient.put('/api/notifications/preferences/me', {
        inAppEnabled: vm.notificationPreferences.inAppEnabled,
        emailEnabled: vm.notificationPreferences.emailEnabled,
        mutedTypes: mutedTypes
      }).then(function(preferences) { vm.notificationPreferences = preferences; vm.notify('success', 'Bildirim tercihleri kaydedildi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Bildirim tercihleri kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.exportPrivacyData = function() {
      return apiClient.get('/api/auth/privacy/export').then(function(data) {
        var blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
        var url = URL.createObjectURL(blob);
        var link = $document[0].createElement('a');
        link.href = url;
        link.download = 'zumbo-privacy-export.json';
        link.click();
        URL.revokeObjectURL(url);
        vm.notify('success', 'Gizlilik dışa aktarımı hazırlandı.');
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Gizlilik verileri aktarılamadı.')); });
    };

    vm.anonymizeAccount = function() {
      if (!vm.privacyDraft.password || vm.privacyDraft.confirmation !== 'ANONYMIZE' || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.post('/api/auth/privacy/anonymize', vm.privacyDraft)
        .then(function() { vm.notify('success', 'Hesap anonimleştirildi.'); return vm.logout(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Hesap anonimleştirilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.loadReports = function(projectId) {
      if (!vm.project) return $q.when();
      projectId = projectId || vm.project.id;
      function assignIfCurrent(assign) {
        return function(data) {
          if (vm.project && vm.project.id === projectId) assign(data);
        };
      }
      return $q.all([
        apiClient.get('/api/work-items/reports/status-distribution/' + projectId)
          .then(assignIfCurrent(function(data) { vm.statusDistribution = data; })),
        apiClient.get('/api/work-items/reports/user-workload/' + projectId)
          .then(assignIfCurrent(function(data) { vm.workload = data; })),
        apiClient.get('/api/work-items/reports/due-date-risks/' + projectId + '?days=14')
          .then(assignIfCurrent(function(data) { vm.dueDateRisks = data; })),
        apiClient.get('/api/work-items/reports/sprint-velocity/' + projectId + '?sprintCount=3')
          .then(assignIfCurrent(function(data) { vm.velocity = data; })),
        apiClient.get('/api/work-items/reports/sprint-burndown/' + projectId + '/' + currentSprintId() + '?startDate=' + formatDate(-6) + '&endDate=' + formatDate(0))
          .then(assignIfCurrent(function(data) { vm.burndown = data; }))
      ]);
    };

    vm.loadWorkflow = function(projectId) {
      if (!vm.project) return $q.when();
      projectId = projectId || vm.project.id;
      vm.workflowLoading = true;
      return apiClient.get('/api/workflows/' + projectId).then(function(workflow) {
        if (vm.project && vm.project.id === projectId) {
          vm.workflow = workflow;
          vm.workflowDraft = angular.copy(workflow);
        }
      }).finally(function() {
        if (vm.project && vm.project.id === projectId) vm.workflowLoading = false;
      });
    };

    vm.addWorkflowStatus = function() {
      vm.workflowDraft.statuses = vm.workflowDraft.statuses || [];
      vm.workflowDraft.statuses.push({ name: '', category: 'InProgress' });
    };

    vm.removeWorkflowStatus = function(index) {
      if (!vm.workflowDraft.statuses || vm.workflowDraft.statuses.length <= 1) return;
      vm.workflowDraft.statuses.splice(index, 1);
    };

    vm.addWorkflowTransition = function() {
      vm.workflowDraft.transitions = vm.workflowDraft.transitions || [];
      vm.workflowDraft.transitions.push({
        fromStatus: '',
        toStatus: '',
        requiresAssignee: false,
        requiresCompletedChecklist: false,
        requiresApproval: false,
        automations: []
      });
    };

    vm.removeWorkflowTransition = function(index) {
      vm.workflowDraft.transitions.splice(index, 1);
    };

    vm.saveWorkflow = function() {
      if (!vm.project || !vm.canManageProject || vm.entitySaving) return;
      var invalidStatus = (vm.workflowDraft.statuses || []).some(function(status) { return !status.name || !status.category; });
      var invalidTransition = (vm.workflowDraft.transitions || []).some(function(transition) {
        return !transition.fromStatus || !transition.toStatus;
      });
      if (invalidStatus || invalidTransition) return vm.notify('error', 'Workflow durum ve geçiş alanlarını tamamlayın.');
      vm.entitySaving = true;
      return apiClient.put('/api/workflows/' + vm.project.id, {
        projectId: vm.project.id,
        statuses: vm.workflowDraft.statuses,
        transitions: vm.workflowDraft.transitions
      }).then(function(workflow) {
        vm.workflow = workflow;
        vm.workflowDraft = angular.copy(workflow);
        vm.notify('success', 'Workflow kaydedildi.');
        return vm.loadProjectAudit();
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Workflow kaydedilemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.selectTask = function(task, skipLocation) {
      if (!task) return;
      apiClient.get('/api/work-items/' + task.id).then(function(detail) {
        vm.selectedTask = detail;
        vm.taskDraft = {
          title: detail.title,
          description: detail.description || '',
          priority: detail.priority,
          dueDate: detail.dueDate ? new Date(detail.dueDate) : null,
          assigneeUserId: detail.assigneeUserId || '',
          teamId: detail.teamId || '',
          sprintId: detail.sprintId || '',
          estimatePoints: detail.estimatePoints,
          parentId: detail.parentId || ''
        };
        vm.nextStatus = nextStatusFor(detail.status);
        if (!skipLocation) updateLocation('board', detail.id, true);
      });
      apiClient.get('/api/audit/entity/WorkItem/' + task.id).then(function(audit) {
        vm.audit = audit;
      });
    };

    vm.closeTask = function() {
      vm.selectedTask = null;
      vm.taskDraft = null;
      vm.audit = [];
      updateLocation(vm.activeSection, null, false);
    };

    vm.saveSelectedTask = function() {
      if (!vm.selectedTask || !vm.taskDraft || vm.taskSaving) return;
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
        return apiClient.get('/api/work-items/' + taskId);
      }).then(function(task) {
        vm.selectedTask = task;
        vm.notify('success', 'Görev ayrıntıları kaydedildi.');
        return vm.loadTasks();
      }).catch(function() {
        vm.notify('error', 'Görev kaydedilemedi; alanları kontrol edin.');
      }).finally(function() { vm.taskSaving = false; });
    };

    vm.archiveSelectedTask = function() {
      if (!vm.selectedTask || vm.taskSaving) return;
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
      if (!vm.selectedTask || !vm.commentBody.trim()) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/comments', { body: vm.commentBody, mentions: [] })
        .then(function(task) { vm.selectedTask = task; vm.commentBody = ''; vm.notify('success', 'Yorum eklendi.'); });
    };

    vm.editComment = function(comment) {
      comment.editing = true;
      comment.draftBody = comment.body;
    };

    vm.saveComment = function(comment) {
      if (!comment || !comment.draftBody || !comment.draftBody.trim()) return;
      return apiClient.put('/api/work-items/' + vm.selectedTask.id + '/comments/' + comment.id, { body: comment.draftBody })
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Yorum güncellendi.'); });
    };

    vm.deleteComment = function(comment) {
      if (!comment) return;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/comments/' + comment.id)
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Yorum silindi.'); });
    };

    vm.addLabel = function() {
      if (!vm.selectedTask || !vm.labelText.trim()) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/labels', { label: vm.labelText })
        .then(function(task) { vm.selectedTask = task; vm.labelText = ''; vm.notify('success', 'Etiket eklendi.'); });
    };

    vm.removeLabel = function(label) {
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/labels/' + encodeURIComponent(label))
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Etiket kaldırıldı.'); });
    };

    vm.addWorkLog = function() {
      if (!vm.selectedTask || !vm.workLogDraft.hours) return;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/worklogs', {
        userId: vm.session.currentUser.id,
        hours: vm.workLogDraft.hours,
        note: vm.workLogDraft.note || null
      }).then(function(task) {
        vm.selectedTask = task;
        vm.workLogDraft = { hours: null, note: '' };
        vm.notify('success', 'İş günlüğü eklendi.');
      });
    };

    vm.addChecklist = function() {
      if (!vm.selectedTask || !vm.checklistText.trim()) return;
      apiClient.post('/api/work-items/' + vm.selectedTask.id + '/checklist', { text: vm.checklistText })
        .then(function(task) { vm.selectedTask = task; vm.checklistText = ''; });
    };

    vm.toggleChecklist = function(item) {
      apiClient.patch('/api/work-items/' + vm.selectedTask.id + '/checklist/' + item.id, { completed: !item.completed })
        .then(function(task) { vm.selectedTask = task; });
    };

    vm.uploadAttachment = function() {
      if (!vm.selectedTask || !vm.attachmentFile) return;
      apiClient.upload('/api/work-items/' + vm.selectedTask.id + '/attachments/upload', vm.attachmentFile)
        .then(function(task) { vm.selectedTask = task; vm.attachmentFile = null; });
    };

    vm.deleteAttachment = function(attachment) {
      apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/attachments/' + attachment.id)
        .then(function(task) { vm.selectedTask = task; });
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
      if (!vm.selectedTask) {
        return;
      }

      apiClient.patch('/api/work-items/' + vm.selectedTask.id + '/status', { status: vm.nextStatus })
        .then(function(task) {
          vm.selectedTask = task;
          vm.nextStatus = nextStatusFor(task.status);
          return vm.loadTasks();
        });
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

    vm.parentCandidates = function(type) {
      if (type === 'Epic') return [];
      var allowed = type === 'Subtask' ? ['Story', 'Task', 'Bug'] : ['Epic'];
      return vm.tasks.filter(function(task) { return allowed.indexOf(task.type) >= 0 && !task.archived; });
    };

    vm.addRelation = function() {
      if (!vm.selectedTask || !vm.relationDraft.relatedWorkItemId || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/relations', vm.relationDraft)
        .then(function(task) {
          vm.selectedTask = task;
          vm.relationDraft = { relatedWorkItemId: '', relationType: 'RelatesTo' };
          vm.notify('success', 'Görev ilişkisi eklendi.');
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Görev ilişkisi eklenemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.removeRelation = function(relation) {
      if (!vm.selectedTask || !relation || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.delete('/api/work-items/' + vm.selectedTask.id + '/relations/' + relation.relatedWorkItemId + '?relationType=' + encodeURIComponent(relation.relationType))
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Görev ilişkisi kaldırıldı.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Görev ilişkisi kaldırılamadı.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.requestApproval = function() {
      if (!vm.selectedTask || !vm.nextStatus || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/approvals', { targetStatus: vm.nextStatus })
        .then(function(task) { vm.selectedTask = task; vm.notify('success', 'Geçiş onayı istendi.'); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Geçiş onayı istenemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.decideApproval = function(approval, approved) {
      if (!vm.selectedTask || !approval || vm.taskSaving) return;
      vm.taskSaving = true;
      return apiClient.post('/api/work-items/' + vm.selectedTask.id + '/approvals/' + approval.id + '/decision', {
        approved: approved,
        note: vm.approvalNote || null
      }).then(function(task) {
        vm.selectedTask = task;
        vm.approvalNote = '';
        vm.notify('success', approved ? 'Geçiş onaylandı.' : 'Geçiş reddedildi.');
      }).catch(function(error) { vm.notify('error', apiActionError(error, 'Onay kararı kaydedilemedi.')); })
        .finally(function() { vm.taskSaving = false; });
    };

    vm.selectView = function() {
      var views = (vm.board && vm.board.views) || [];
      vm.activeView = views.find(function(view) { return view.id === vm.activeViewId; }) || null;
      if (vm.activeView) {
        vm.swimlaneMode = vm.activeView.swimlaneMode;
        vm.priorityFilter = (vm.activeView.filter.priorities || [])[0] || '';
        vm.search = vm.activeView.filter.text || '';
        vm.viewDraftName = vm.activeView.name;
      } else {
        vm.viewDraftName = '';
      }
      vm.refreshBoardModel();
    };

    vm.updateSwimlane = function() {
      if (!vm.board) return;
      return apiClient.patch('/api/boards/' + vm.board.id + '/swimlane', { mode: vm.swimlaneMode })
        .then(function(board) {
          vm.board = board;
          vm.activeViewId = '';
          vm.activeView = null;
          vm.refreshBoardModel();
          return vm.loadBoardAudit();
        });
    };

    vm.saveCurrentView = function() {
      if (!vm.board || !vm.viewDraftName) return;
      var payload = {
        name: vm.viewDraftName,
        isShared: vm.activeView ? vm.activeView.isShared : false,
        swimlaneMode: vm.swimlaneMode || 'None',
        filter: {
          assigneeUserId: null,
          teamId: null,
          statuses: [],
          priorities: vm.priorityFilter ? [vm.priorityFilter] : [],
          labels: [],
          text: vm.search || null
        }
      };
      var request = vm.activeView
        ? apiClient.put('/api/boards/' + vm.board.id + '/views/' + vm.activeView.id, payload)
        : apiClient.post('/api/boards/' + vm.board.id + '/views', payload);
      return request.then(function(board) {
        vm.board = board;
        var created = board.views.find(function(view) { return view.name === vm.viewDraftName; });
        vm.activeViewId = created ? created.id : '';
        vm.selectView();
        vm.notify('success', 'Kayıtlı görünüm kaydedildi.');
        return vm.loadBoardAudit();
      });
    };

    vm.deleteCurrentView = function() {
      if (!vm.board || !vm.activeView || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.delete('/api/boards/' + vm.board.id + '/views/' + vm.activeView.id)
        .then(function(board) {
          setBoardState(board);
          vm.activeViewId = '';
          vm.activeView = null;
          vm.viewDraftName = '';
          vm.notify('success', 'Kayıtlı görünüm silindi.');
          return vm.loadBoardAudit();
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Kayıtlı görünüm silinemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.refreshBoardModel = function() {
      if (!vm.board) {
        vm.boardRows = [];
        return;
      }

      var view = vm.activeView;
      var filter = view ? view.filter : null;
      var tasks = (vm.tasks || []).filter(function(task) {
        if (vm.priorityFilter && task.priority !== vm.priorityFilter) return false;
        if (!filter) return true;
        if (filter.assigneeUserId && task.assigneeUserId !== filter.assigneeUserId) return false;
        if (filter.teamId && task.teamId !== filter.teamId) return false;
        if (filter.statuses.length && filter.statuses.indexOf(task.status) < 0) return false;
        if (filter.priorities.length && filter.priorities.indexOf(task.priority) < 0) return false;
        if (filter.labels.length && !filter.labels.some(function(label) { return task.labels.indexOf(label) >= 0; })) return false;
        if (filter.text) {
          var haystack = [task.title, task.description, (task.labels || []).join(' ')].join(' ').toLowerCase();
          if (haystack.indexOf(filter.text.toLowerCase()) < 0) return false;
        }
        return true;
      }).sort(function(left, right) { return (left.rank || 0) - (right.rank || 0); });
      var mode = (view && view.swimlaneMode) || vm.swimlaneMode || vm.board.swimlaneMode || 'None';
      var groups = {};
      tasks.forEach(function(task) {
        var key = swimlaneKey(task, mode);
        groups[key] = groups[key] || [];
        groups[key].push(task);
      });
      if (!tasks.length || mode === 'None') groups['Tüm işler'] = tasks;
      vm.boardRows = Object.keys(groups).sort().map(function(label) {
        return {
          label: label,
          columns: vm.board.columns.map(function(column) {
            var columnTasks = groups[label].filter(function(task) { return task.columnId === column.id || task.status === column.name; });
            return {
              id: column.id,
              name: column.name,
              wipLimit: column.wipLimit,
              tasks: columnTasks,
              count: columnTasks.length,
              atWipLimit: !!column.wipLimit && columnTasks.length >= column.wipLimit,
              collapsed: !!vm.collapsedColumns[vm.board.id + ':' + column.id]
            };
          })
        };
      });
    };

    function swimlaneKey(task, mode) {
      if (mode === 'Assignee') return task.assigneeUserId || 'Atanmamış';
      if (mode === 'Priority') return task.priority || 'Öncelik yok';
      if (mode === 'Team') return task.teamId || 'Takım yok';
      if (mode === 'Epic') return task.parentId || 'Epic yok';
      return 'Tüm işler';
    }

    function nextStatusFor(status) {
      if (status === 'To Do') return 'In Progress';
      if (status === 'In Progress') return 'Code Review';
      if (status === 'Code Review') return 'Test';
      return 'Done';
    }

    function currentSprintId() {
      return vm.project ? vm.project.key + '-sprint-1' : 'demo-sprint-1';
    }

    function formatDate(offsetDays) {
      var date = new Date();
      date.setDate(date.getDate() + offsetDays);
      return date.toISOString().slice(0, 10);
    }

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

    function firstAccessibleProject(projects) {
      return (projects || []).find(function(project) { return !!membershipFor(project); }) || null;
    }

    function setProjectState(project, preserveDraft) {
      var existingDraft = preserveDraft && vm.project && vm.project.id === project.id
        ? vm.projectDraft
        : null;
      vm.project = project;
      vm.projectDraft = existingDraft || { name: project.name, visibility: project.visibility };
      vm.projectMembership = membershipFor(project);
      vm.canManageProject = !!vm.projectMembership
        && ['ProjectOwner', 'ProjectAdmin'].indexOf(vm.projectMembership.role) >= 0;
      vm.canArchiveProject = !!vm.projectMembership && vm.projectMembership.role === 'ProjectOwner';
      var index = vm.projects.findIndex(function(item) { return item.id === project.id; });
      if (index >= 0) vm.projects[index] = project;
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
      return apiClient.get('/api/projects?organizationId=' + encodeURIComponent(vm.session.currentUser.organizationId))
        .then(function(projects) { vm.projects = projects; return projects; });
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
      if (team && !skipLocation) updateLocation('teams', null, false);
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
      setBoardState(board);
      vm.loadBoardAudit();
      if (!skipLocation) updateLocation(vm.activeSection, null, false);
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
          vm.projectMemberDraft = { userId: '', role: 'Developer' };
          vm.notify('success', 'Proje üyesi eklendi.');
          return vm.loadProjectAudit();
        }).catch(function(error) { vm.notify('error', apiActionError(error, 'Proje üyesi eklenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.changeProjectMemberRole = function(member) {
      if (!vm.project || !vm.canManageProject || !member || member.role === 'ProjectOwner' || vm.entitySaving) return;
      vm.entitySaving = true;
      return apiClient.patch('/api/projects/' + vm.project.id + '/members/' + member.userId + '/role', { role: member.role })
        .then(function(project) { setProjectState(project, true); vm.notify('success', 'Proje rolü güncellendi.'); return vm.loadProjectAudit(); })
        .catch(function(error) { vm.notify('error', apiActionError(error, 'Proje rolü güncellenemedi.')); })
        .finally(function() { vm.entitySaving = false; });
    };

    vm.removeProjectMember = function(member) {
      if (!vm.project || !vm.canManageProject || !member || member.role === 'ProjectOwner' || vm.entitySaving) return;
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
      setProjectState(project);
      if (!skipLocation) updateLocation(vm.activeSection, null, false);
      window.localStorage.setItem('zumbo.projectId', project.id);
      vm.board = null;
      vm.tasks = [];
      vm.archivedTasks = [];
      vm.selectedTask = null;
      vm.clearSelection();
      rememberProject(project);
      vm.entityAudit = [];
      if (vm.projectMembership) vm.loadProjectAudit();
      var workflowRequest = vm.projectMembership ? vm.loadWorkflow(project.id) : $q.when(null);
      return $q.all([vm.loadBoards(), workflowRequest]).then(function(result) {
        var boards = result[0];
        var route = new URLSearchParams($window.location.hash.slice(1));
        var linkedBoardId = route.get('project') === project.id ? route.get('board') : null;
        var linkedBoard = boards.find(function(board) { return board.id === linkedBoardId; });
        setBoardState(linkedBoard || boards[0] || null);
        vm.loadBoardAudit();
        if (!skipLocation && vm.board) updateLocation(vm.activeSection, null, false);
        if (!vm.board) return;
        return realtimeService.connect(project.id).catch(angular.noop).then(vm.loadTasks).then(function() {
          if (vm.activeSection === 'archive') return vm.loadArchivedTasks();
        });
      });
    };

    vm.restore = function() {
      if (!vm.session.currentUser) return;
      return apiClient.get('/api/projects?organizationId=' + encodeURIComponent(vm.session.currentUser.organizationId))
        .then(function(projects) {
          vm.projects = projects;
          var rememberedId = window.localStorage.getItem('zumbo.projectId');
          var remembered = projects.find(function(project) { return project.id === rememberedId; });
          var route = new URLSearchParams($window.location.hash.slice(1));
          var linked = projects.find(function(project) { return project.id === route.get('project'); });
          var selected = linked || (membershipFor(remembered) ? remembered : firstAccessibleProject(projects));
          if (!selected) return;
          window.localStorage.setItem('zumbo.projectId', selected.id);
          var selection = vm.selectProject(selected, true);
          applyLocation();
          return selection;
        }).then(function() {
          return $q.all([vm.loadNotifications(), vm.loadTeams(), vm.loadUsers()]);
        }).then(applyLocation);
    };

    if (vm.session.currentUser) {
      vm.restore().catch(function() {
        vm.session.currentUser = null;
        vm.session.accessToken = null;
        vm.session.refreshToken = null;
        window.localStorage.removeItem('zumbo.currentUser');
        window.localStorage.removeItem('zumbo.accessToken');
        window.localStorage.removeItem('zumbo.refreshToken');
        vm.project = null;
        vm.board = null;
      });
    }
    applyLocation();
  })
  .directive('fileChange', function() {
    return {
      restrict: 'A',
      link: function(scope, element, attrs) {
        element.on('change', function(event) {
          scope.$apply(function() { scope.$eval(attrs.fileChange, { file: event.target.files[0] }); });
        });
      }
    };
  })
  .directive('lucideIcon', function($timeout) {
    return {
      restrict: 'A',
      link: function() {
        $timeout(function() { if (window.lucide) window.lucide.createIcons({ attrs: { 'stroke-width': 1.8 } }); });
      }
    };
  })
  .directive('commandFocus', function($timeout) {
    return { link: function(scope, element) { $timeout(function() { element[0].focus(); }); } };
  })
  .directive('draggableTask', function() {
    return {
      restrict: 'A',
      link: function(scope, element, attrs) {
        element.attr('draggable', 'true');
        element.on('dragstart', function(event) {
          var nativeEvent = event.originalEvent || event;
          nativeEvent.dataTransfer.effectAllowed = 'move';
          nativeEvent.dataTransfer.setData('text/plain', attrs.draggableTask);
        });
      }
    };
  })
  .directive('dropLane', function() {
    return {
      restrict: 'A',
      link: function(scope, element, attrs) {
        element.on('dragover', function(event) { event.preventDefault(); });
        element.on('drop', function(event) {
          event.preventDefault();
          var nativeEvent = event.originalEvent || event;
          var taskId = nativeEvent.dataTransfer.getData('text/plain');
          scope.$apply(function() { scope.$eval(attrs.dropLane, { taskId: taskId }); });
        });
      }
    };
  })
  .directive('dropTaskBefore', function() {
    return {
      restrict: 'A',
      link: function(scope, element, attrs) {
        element.on('dragover', function(event) { event.preventDefault(); });
        element.on('drop', function(event) {
          event.preventDefault();
          event.stopPropagation();
          var nativeEvent = event.originalEvent || event;
          var taskId = nativeEvent.dataTransfer.getData('text/plain');
          scope.$apply(function() { scope.$eval(attrs.dropTaskBefore, { taskId: taskId }); });
        });
      }
    };
  });
