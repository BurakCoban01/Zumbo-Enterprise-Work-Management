function mobileActionError(error, fallback) {
  return error && error.data && error.data.error && error.data.error.message
    ? error.data.error.message
    : fallback;
}

function createDemoPassword() {
  var bytes = new Uint8Array(18);
  window.crypto.getRandomValues(bytes);
  return 'Z1!' + Array.prototype.map.call(bytes, function(value) {
    return ('0' + value.toString(16)).slice(-2);
  }).join('');
}

angular.module('zumboMobile', ['ionic'])
  .constant('API_BASE_URL', window.localStorage.getItem('zumbo.apiBaseUrl') || 'http://localhost:5088')
  .factory('tokenStorage', function() {
    return {
      get: function() { return window.localStorage.getItem('zumbo.accessToken'); },
      getRefresh: function() { return window.localStorage.getItem('zumbo.refreshToken'); },
      set: function(accessToken, refreshToken) {
        window.localStorage.setItem('zumbo.accessToken', accessToken);
        if (refreshToken) { window.localStorage.setItem('zumbo.refreshToken', refreshToken); }
      },
      clear: function() {
        window.localStorage.removeItem('zumbo.accessToken');
        window.localStorage.removeItem('zumbo.refreshToken');
      }
    };
  })
  .factory('sessionStore', function() {
    var state = { currentUser: JSON.parse(window.localStorage.getItem('zumbo.currentUser') || 'null'), project: null, board: null, team: null };
    return {
      state: state,
      setUser: function(user) {
        state.currentUser = user;
        window.localStorage.setItem('zumbo.currentUser', JSON.stringify(user));
      },
      clear: function() {
        state.currentUser = null;
        state.project = null;
        state.board = null;
        state.team = null;
        window.localStorage.removeItem('zumbo.currentUser');
      }
    };
  })
  .factory('apiClient', function($http, $q, API_BASE_URL, tokenStorage) {
    var refreshPromise = null;
    function unwrap(promise) {
      return promise.then(function(response) { return response.data.data; });
    }
    function config() {
      var token = tokenStorage.get();
      return token ? { headers: { Authorization: 'Bearer ' + token } } : {};
    }
    function request(httpConfig, allowRefresh) {
      return $http(httpConfig).catch(function(error) {
        var refreshToken = tokenStorage.getRefresh();
        if (!allowRefresh || error.status !== 401 || !refreshToken) { return $q.reject(error); }
        if (!refreshPromise) {
          refreshPromise = $http.post(API_BASE_URL + '/api/auth/refresh', { refreshToken: refreshToken })
            .then(function(response) {
              var auth = response.data.data;
              tokenStorage.set(auth.accessToken, auth.refreshToken);
              return auth;
            }).catch(function(refreshError) {
              tokenStorage.clear();
              return $q.reject(refreshError);
            }).finally(function() {
              refreshPromise = null;
            });
        }
        return refreshPromise.then(function() {
          httpConfig.headers = httpConfig.headers || {};
          httpConfig.headers.Authorization = 'Bearer ' + tokenStorage.get();
          return $http(httpConfig);
        });
      });
    }
    function allowsRefresh(url) {
      return ['/api/auth/login', '/api/auth/register', '/api/auth/refresh', '/api/auth/forgot-password', '/api/auth/reset-password']
        .indexOf(url) < 0;
    }
    return {
      get: function(url) { return unwrap(request(angular.extend(config(), { method: 'GET', url: API_BASE_URL + url }), true)); },
      post: function(url, data) { return unwrap(request(angular.extend(config(), { method: 'POST', url: API_BASE_URL + url, data: data }), allowsRefresh(url))); },
      put: function(url, data) { return unwrap(request(angular.extend(config(), { method: 'PUT', url: API_BASE_URL + url, data: data }), true)); },
      patch: function(url, data) { return unwrap(request(angular.extend(config(), { method: 'PATCH', url: API_BASE_URL + url, data: data }), true)); },
      delete: function(url) { return unwrap(request(angular.extend(config(), { method: 'DELETE', url: API_BASE_URL + url }), true)); },
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
        return unwrap(request(requestConfig, true));
      },
      download: function(url) {
        var requestConfig = config();
        requestConfig.responseType = 'blob';
        requestConfig.method = 'GET';
        requestConfig.url = API_BASE_URL + url;
        return request(requestConfig, true).then(function(response) {
          return response.data;
        });
      }
    };
  })
  .factory('realtimeService', function($q, $rootScope, API_BASE_URL, tokenStorage) {
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
            accessTokenFactory: function() { return tokenStorage.get() || ''; },
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
  .factory('authService', function($q, apiClient, tokenStorage, sessionStore) {
    function accept(auth) {
      tokenStorage.set(auth.accessToken, auth.refreshToken);
      sessionStore.setUser(auth.user);
      return auth;
    }
    return {
      login: function(form) { return apiClient.post('/api/auth/login', form).then(accept); },
      forgotPassword: function(email) {
        return apiClient.post('/api/auth/forgot-password', { email: email });
      },
      resetPassword: function(token, newPassword) {
        return apiClient.post('/api/auth/reset-password', { token: token, newPassword: newPassword });
      },
      logout: function() {
        var refreshToken = tokenStorage.getRefresh();
        if (!refreshToken) { tokenStorage.clear(); return $q.when(); }
        return apiClient.post('/api/auth/logout', { refreshToken: refreshToken, allSessions: false })
          .finally(tokenStorage.clear);
      },
      registerDemo: function() {
        var suffix = Date.now();
        return apiClient.post('/api/auth/register', {
          username: 'demo-user' + suffix,
          email: 'demo-user' + suffix + '@zumbo.local',
          password: createDemoPassword(),
          organizationId: 'mobile-demo-' + suffix
        }).then(accept);
      }
    };
  })
  .factory('zumboApi', function(apiClient, sessionStore) {
    return {
      projects: function(archived) { return apiClient.get('/api/projects?organizationId=' + sessionStore.state.currentUser.organizationId + (archived ? '&archived=true' : '')); },
      createProject: function(draft) {
        draft = draft || {};
        return apiClient.post('/api/projects', {
          organizationId: sessionStore.state.currentUser.organizationId,
          key: draft.key || 'MOB' + String(Date.now()).slice(-7),
          name: draft.name || 'Mobil Teslimat',
          ownerUserId: sessionStore.state.currentUser.id
        });
      },
      updateProject: function(projectId, draft) { return apiClient.put('/api/projects/' + projectId, draft); },
      archiveProject: function(projectId) { return apiClient.delete('/api/projects/' + projectId); },
      restoreProject: function(projectId) { return apiClient.post('/api/projects/' + projectId + '/restore', {}); },
      createBoard: function(projectId, draft) {
        draft = draft || {};
        return apiClient.post('/api/boards', { projectId: projectId, name: draft.name || 'Mobil Pano', type: draft.type || 'Kanban' });
      },
      boards: function(projectId, archived) { return apiClient.get('/api/boards/by-project/' + projectId + (archived ? '?archived=true' : '')); },
      updateBoard: function(boardId, draft) { return apiClient.put('/api/boards/' + boardId, draft); },
      archiveBoard: function(boardId) { return apiClient.delete('/api/boards/' + boardId); },
      restoreBoard: function(boardId) { return apiClient.post('/api/boards/' + boardId + '/restore', {}); },
      teams: function(archived) { return apiClient.get('/api/teams?organizationId=' + sessionStore.state.currentUser.organizationId + (archived ? '&archived=true' : '')); },
      createTeam: function(name) { return apiClient.post('/api/teams', { organizationId: sessionStore.state.currentUser.organizationId, name: name, ownerUserId: sessionStore.state.currentUser.id }); },
      updateTeam: function(teamId, name) { return apiClient.put('/api/teams/' + teamId, { name: name }); },
      inviteTeamMember: function(teamId, email, role) { return apiClient.post('/api/teams/' + teamId + '/members', { email: email, role: role }); },
      removeTeamMember: function(teamId, memberKey) { return apiClient.delete('/api/teams/' + teamId + '/members/' + encodeURIComponent(memberKey)); },
      archiveTeam: function(teamId) { return apiClient.delete('/api/teams/' + teamId); },
      restoreTeam: function(teamId) { return apiClient.post('/api/teams/' + teamId + '/restore', {}); },
      audit: function(entityType, entityId) { return apiClient.get('/api/audit/entity/' + entityType + '/' + entityId); },
      tasks: function(projectId, status, page, pageSize) {
        var query = status ? '&status=' + encodeURIComponent(status) : '';
        return apiClient.get('/api/work-items?projectId=' + encodeURIComponent(projectId) +
          '&assigneeUserId=' + encodeURIComponent(sessionStore.state.currentUser.id) + query +
          '&page=' + (page || 1) + '&pageSize=' + (pageSize || 50));
      },
      createTask: function(projectId, boardId) {
        return apiClient.post('/api/work-items', {
          projectId: projectId,
          boardId: boardId,
          title: 'Mobil takip ' + new Date().toLocaleTimeString(),
          type: 'Task',
          priority: 'Medium',
          assigneeUserId: sessionStore.state.currentUser.id
        });
      },
      task: function(taskId) { return apiClient.get('/api/work-items/' + taskId); },
      workflow: function(projectId) { return apiClient.get('/api/workflows/' + projectId); },
      moveTask: function(taskId, status) { return apiClient.patch('/api/work-items/' + taskId + '/status', { status: status }); },
      addComment: function(taskId, body) { return apiClient.post('/api/work-items/' + taskId + '/comments', { body: body, mentions: [] }); },
      addChecklist: function(taskId, text) { return apiClient.post('/api/work-items/' + taskId + '/checklist', { text: text }); },
      completeChecklist: function(taskId, itemId, completed) { return apiClient.patch('/api/work-items/' + taskId + '/checklist/' + itemId, { completed: completed }); },
      addLabel: function(taskId, label) { return apiClient.post('/api/work-items/' + taskId + '/labels', { label: label }); },
      uploadAttachment: function(taskId, file) { return apiClient.upload('/api/work-items/' + taskId + '/attachments/upload', file); },
      deleteAttachment: function(taskId, attachmentId) { return apiClient.delete('/api/work-items/' + taskId + '/attachments/' + attachmentId); },
      downloadAttachment: function(taskId, attachmentId) { return apiClient.download('/api/work-items/' + taskId + '/attachments/' + attachmentId + '/download'); },
      summary: function(projectId) { return apiClient.get('/api/work-items/reports/project-summary/' + projectId); },
      notifications: function() { return apiClient.get('/api/notifications/' + sessionStore.state.currentUser.id); },
      read: function(id) { return apiClient.patch('/api/notifications/' + id + '/read', {}); }
    };
  })
  .config(function($stateProvider, $urlRouterProvider) {
    $stateProvider
      .state('login', { url: '/login', templateUrl: 'templates/login.html', controller: 'LoginController as vm' })
      .state('forgot-password', { url: '/forgot-password', templateUrl: 'templates/forgot-password.html', controller: 'ForgotPasswordController as vm' })
      .state('reset-password', { url: '/reset-password?token', templateUrl: 'templates/reset-password.html', controller: 'ResetPasswordController as vm' })
      .state('project-detail', { url: '/projects/:projectId', templateUrl: 'templates/project-detail.html', controller: 'ProjectDetailController as vm' })
      .state('team-detail', { url: '/teams/:teamId', templateUrl: 'templates/team-detail.html', controller: 'TeamDetailController as vm' })
      .state('task-detail', { url: '/tasks/:taskId', templateUrl: 'templates/task-detail.html', controller: 'TaskDetailController as vm' })
      .state('app', { url: '/app', abstract: true, templateUrl: 'templates/tabs.html' })
      .state('app.dashboard', { url: '/dashboard', views: { dashboard: { templateUrl: 'templates/dashboard.html', controller: 'DashboardController as vm' } } })
      .state('app.projects', { url: '/projects', views: { projects: { templateUrl: 'templates/projects.html', controller: 'ProjectsController as vm' } } })
      .state('app.tasks', { url: '/tasks', views: { tasks: { templateUrl: 'templates/tasks.html', controller: 'TasksController as vm' } } })
      .state('app.notifications', { url: '/notifications', views: { notifications: { templateUrl: 'templates/notifications.html', controller: 'NotificationsController as vm' } } })
      .state('app.profile', { url: '/profile', views: { profile: { templateUrl: 'templates/profile.html' } } });
    $urlRouterProvider.otherwise('/login');
  })
  .controller('ShellController', function($state, authService, sessionStore, realtimeService) {
    var vm = this;
    vm.session = sessionStore.state;
    vm.theme = window.localStorage.getItem('zumbo.mobileTheme') || 'light';
    vm.toggleTheme = function() {
      vm.theme = vm.theme === 'dark' ? 'light' : 'dark';
      window.localStorage.setItem('zumbo.mobileTheme', vm.theme);
    };
    vm.logout = function() {
      realtimeService.stop();
      authService.logout().finally(function() {
        sessionStore.clear();
        $state.go('login');
      });
    };
  })
  .controller('LoginController', function($state, authService, zumboApi, sessionStore) {
    var vm = this;
    vm.form = { usernameOrEmail: '', password: '', mfaCode: '' };
    vm.login = function() {
      vm.error = null;
      authService.login(vm.form).then(function() { $state.go('app.dashboard'); }).catch(function(error) {
        var code = error.data && error.data.error && error.data.error.code;
        vm.mfaRequired = code === 'MFA_REQUIRED' || code === 'MFA_INVALID';
        vm.error = vm.mfaRequired ? 'Doğrulama kodunu kontrol edin.' : 'Giriş başarısız.';
      });
    };
    vm.demo = function() {
      vm.error = null;
      vm.demoPending = true;
      authService.registerDemo()
        .then(function() { return zumboApi.createProject(); })
        .then(function(project) {
          sessionStore.state.project = project;
          return zumboApi.createBoard(project.id).then(function(board) {
            sessionStore.state.board = board;
            return zumboApi.createTask(project.id, board.id);
          });
        })
        .then(function() { $state.go('app.dashboard'); })
        .catch(function() { vm.error = 'Demo çalışma alanı oluşturulamadı.'; })
        .finally(function() { vm.demoPending = false; });
    };
  })
  .controller('ForgotPasswordController', function($state, authService) {
    var vm = this;
    vm.email = '';
    vm.submit = function() {
      vm.error = null;
      vm.pending = true;
      authService.forgotPassword(vm.email).then(function() {
        vm.sent = true;
      }).catch(function() {
        vm.error = 'İstek gönderilemedi.';
      }).finally(function() {
        vm.pending = false;
      });
    };
    vm.back = function() { $state.go('login'); };
  })
  .controller('ResetPasswordController', function($state, $stateParams, authService) {
    var vm = this;
    vm.form = { newPassword: '', confirmPassword: '' };
    vm.submit = function() {
      vm.error = null;
      if (!vm.form.newPassword || vm.form.newPassword !== vm.form.confirmPassword) {
        vm.error = 'Parolalar eşleşmiyor.';
        return;
      }
      vm.pending = true;
      authService.resetPassword($stateParams.token, vm.form.newPassword).then(function() {
        vm.reset = true;
      }).catch(function() {
        vm.error = 'Bağlantı geçersiz veya süresi dolmuş.';
      }).finally(function() {
        vm.pending = false;
      });
    };
    vm.back = function() { $state.go('login'); };
  })
  .controller('DashboardController', function($scope, $state, $ionicScrollDelegate, $q, zumboApi, sessionStore, realtimeService) {
    var vm = this;
    vm.summary = {};
    vm.tasks = [];
    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (!sessionStore.state.project || change.projectId !== sessionStore.state.project.id) { return; }
      var index = vm.tasks.findIndex(function(task) { return task.id === change.workItemId; });
      var visible = change.eventType !== 'archived'
        && change.workItem.assigneeUserId === sessionStore.state.currentUser.id;
      if (!visible && index >= 0) { vm.tasks.splice(index, 1); }
      else if (visible && index >= 0) { vm.tasks[index] = change.workItem; }
      else if (visible) { vm.tasks.unshift(change.workItem); }
      vm.tasks.sort(function(left, right) { return (left.rank || 0) - (right.rank || 0); });
    });
    $scope.$on('$destroy', unsubscribeRealtime);
    vm.refresh = function() {
      return zumboApi.projects().then(function(projects) {
        var selectedId = sessionStore.state.project && sessionStore.state.project.id;
        sessionStore.state.project = projects.filter(function(project) { return project.id === selectedId; })[0]
          || projects.filter(function(project) {
            return project.members.some(function(member) {
              return member.userId === sessionStore.state.currentUser.id;
            });
          })[0]
          || null;
        if (!sessionStore.state.project) { return []; }
        return realtimeService.connect(sessionStore.state.project.id).catch(angular.noop).then(function() {
          return $q.all([
            zumboApi.summary(sessionStore.state.project.id),
            zumboApi.tasks(sessionStore.state.project.id, '')
          ]);
        });
      }).then(function(result) {
        if (result && result.length) {
          vm.summary = result[0];
          vm.tasks = result[1];
        }
      }).finally(function() {
        $ionicScrollDelegate.$getByHandle('dashboardScroll').resize();
      });
    };
    vm.openTask = function(task) {
      $state.go('task-detail', { taskId: task.id });
    };
    vm.refresh();
  })
  .controller('ProjectsController', function($scope, $state, $q, zumboApi, sessionStore) {
    var vm = this;
    vm.mode = 'projects';
    vm.projects = [];
    vm.teams = [];
    vm.archivedProjects = [];
    vm.archivedTeams = [];
    vm.projectDraft = { key: '', name: '' };
    vm.teamDraft = { name: '' };
    vm.load = function() {
      vm.loading = true;
      return $q.all([zumboApi.projects(), zumboApi.teams(), zumboApi.projects(true), zumboApi.teams(true)])
        .then(function(result) {
          vm.projects = result[0];
          vm.teams = result[1];
          vm.archivedProjects = result[2];
          vm.archivedTeams = result[3];
        }).catch(function(error) { vm.error = mobileActionError(error, 'Çalışma alanları yüklenemedi.'); })
        .finally(function() { vm.loading = false; });
    };
    vm.setMode = function(mode) { vm.mode = mode; vm.error = null; };
    vm.select = function(project) {
      sessionStore.state.project = project;
      $state.go('project-detail', { projectId: project.id });
    };
    vm.selectTeam = function(team) {
      sessionStore.state.team = team;
      $state.go('team-detail', { teamId: team.id });
    };
    vm.createProject = function() {
      if (!vm.projectDraft.key || !vm.projectDraft.name || vm.saving) return;
      vm.saving = true;
      zumboApi.createProject(vm.projectDraft).then(function(project) {
        vm.projectDraft = { key: '', name: '' };
        vm.notice = 'Proje oluşturuldu.';
        vm.projects.unshift(project);
      }).catch(function(error) { vm.error = mobileActionError(error, 'Proje oluşturulamadı.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.createTeam = function() {
      if (!vm.teamDraft.name || vm.saving) return;
      vm.saving = true;
      zumboApi.createTeam(vm.teamDraft.name).then(function(team) {
        vm.teamDraft = { name: '' };
        vm.notice = 'Ekip oluşturuldu.';
        vm.teams.unshift(team);
      }).catch(function(error) { vm.error = mobileActionError(error, 'Ekip oluşturulamadı.'); })
        .finally(function() { vm.saving = false; });
    };
    vm.restoreProject = function(project) { return zumboApi.restoreProject(project.id).then(vm.load); };
    vm.restoreTeam = function(team) { return zumboApi.restoreTeam(team.id).then(vm.load); };
    $scope.$on('$ionicView.beforeEnter', vm.load);
    vm.load();
  })
  .controller('TasksController', function($scope, $state, $ionicPopup, $q, zumboApi, sessionStore, realtimeService) {
    var vm = this;
    vm.status = '';
    vm.tasks = [];
    vm.page = 1;
    vm.pageSize = 50;
    vm.hasMore = false;
    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (!sessionStore.state.project || change.projectId !== sessionStore.state.project.id) { return; }
      var index = vm.tasks.findIndex(function(task) { return task.id === change.workItemId; });
      var visible = change.eventType !== 'archived'
        && change.workItem.assigneeUserId === sessionStore.state.currentUser.id
        && (!vm.status || change.workItem.status === vm.status);
      if (!visible && index >= 0) { vm.tasks.splice(index, 1); }
      else if (visible && index >= 0) { vm.tasks[index] = change.workItem; }
      else if (visible) { vm.tasks.unshift(change.workItem); }
      vm.tasks.sort(function(left, right) { return (left.rank || 0) - (right.rank || 0); });
    });
    $scope.$on('$destroy', unsubscribeRealtime);
    vm.filter = function(status) { vm.status = status; vm.load(); };
    vm.load = function(page, append) {
      page = Number.isInteger(page) && page > 0 ? page : 1;
      append = append === true;
      var projectPromise = sessionStore.state.project
        ? $q.when(sessionStore.state.project)
        : zumboApi.projects().then(function(projects) {
            sessionStore.state.project = projects[0] || null;
            return sessionStore.state.project;
          });
      return projectPromise.then(function(project) {
        if (!project) {
          vm.tasks = [];
          return [];
        }
        return realtimeService.connect(project.id).catch(angular.noop).then(function() {
          return zumboApi.tasks(project.id, vm.status, page, vm.pageSize).then(function(data) {
            vm.page = page;
            vm.hasMore = data.length === vm.pageSize;
            vm.tasks = append ? vm.tasks.concat(data.filter(function(task) {
              return !vm.tasks.some(function(existing) { return existing.id === task.id; });
            })) : data;
            return data;
          });
        });
      });
    };
    vm.loadMore = function() {
      if (!vm.hasMore) {
        $scope.$broadcast('scroll.infiniteScrollComplete');
        return;
      }
      vm.load(vm.page + 1, true).finally(function() {
        $scope.$broadcast('scroll.infiniteScrollComplete');
      });
    };
    vm.openTask = function(task) {
      $state.go('task-detail', { taskId: task.id });
    };
    vm.quickAdd = function() {
      var project = sessionStore.state.project;
      if (!project) {
        $ionicPopup.alert({ title: 'Önce proje seçin' });
        return;
      }
      zumboApi.boards(project.id).then(function(boards) {
        if (boards.length) { return boards[0]; }
        return zumboApi.createBoard(project.id);
      }).then(function(board) {
        sessionStore.state.board = board;
        return zumboApi.createTask(project.id, board.id);
      }).then(vm.load);
    };
    vm.load();
  })
  .controller('NotificationsController', function(zumboApi) {
    var vm = this;
    vm.notifications = [];
    vm.load = function() { return zumboApi.notifications().then(function(data) { vm.notifications = data; }); };
    vm.read = function(notification) { zumboApi.read(notification.id).then(vm.load); };
    vm.load();
  })
  .controller('ProjectDetailController', function($state, $stateParams, $q, zumboApi, sessionStore) {
    var vm = this;
    vm.project = sessionStore.state.project;
    vm.boards = [];
    vm.archivedBoards = [];
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
        return $q.all([zumboApi.boards(vm.project.id), zumboApi.boards(vm.project.id, true), zumboApi.audit('Project', vm.project.id)]);
      }).then(function(result) {
        vm.boards = result[0];
        vm.archivedBoards = result[1];
        vm.audit = result[2];
      }).catch(function(error) {
        vm.error = mobileActionError(error, 'Proje yüklenemedi.');
      });
    };
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
    vm.load();
  })
  .controller('TeamDetailController', function($state, $stateParams, $q, zumboApi, sessionStore) {
    var vm = this;
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
  .controller('TaskDetailController', function($scope, $stateParams, $window, zumboApi, realtimeService) {
    var vm = this;
    vm.task = null;
    vm.transitions = [];
    vm.commentBody = '';
    vm.checklistText = '';
    vm.labelText = '';
    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (change.workItemId === $stateParams.taskId && change.eventType !== 'archived') {
        vm.load();
      }
    });
    $scope.$on('$destroy', unsubscribeRealtime);
    vm.load = function() {
      return zumboApi.task($stateParams.taskId).then(function(task) {
        vm.task = task;
        return realtimeService.connect(task.projectId).catch(angular.noop).then(function() {
          return zumboApi.workflow(task.projectId);
        });
      }).then(function(workflow) {
        vm.transitions = workflow.transitions.filter(function(transition) {
          return transition.fromStatus === vm.task.status;
        });
      });
    };
    vm.move = function(status) { zumboApi.moveTask(vm.task.id, status).then(vm.load); };
    vm.addComment = function() {
      if (!vm.commentBody.trim()) { return; }
      zumboApi.addComment(vm.task.id, vm.commentBody).then(function() { vm.commentBody = ''; return vm.load(); });
    };
    vm.addChecklist = function() {
      if (!vm.checklistText.trim()) { return; }
      zumboApi.addChecklist(vm.task.id, vm.checklistText).then(function() { vm.checklistText = ''; return vm.load(); });
    };
    vm.toggleChecklist = function(item) { zumboApi.completeChecklist(vm.task.id, item.id, !item.completed).then(vm.load); };
    vm.addLabel = function() {
      if (!vm.labelText.trim()) { return; }
      zumboApi.addLabel(vm.task.id, vm.labelText).then(function() { vm.labelText = ''; return vm.load(); });
    };
    vm.upload = function() {
      if (!vm.attachmentFile) { return; }
      zumboApi.uploadAttachment(vm.task.id, vm.attachmentFile).then(function() { vm.attachmentFile = null; return vm.load(); });
    };
    vm.removeAttachment = function(attachment) { zumboApi.deleteAttachment(vm.task.id, attachment.id).then(vm.load); };
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
  })
  .directive('fileChange', function() {
    return {
      restrict: 'A',
      link: function(scope, element, attrs) {
        element.on('change', function(event) {
          scope.$apply(function() {
            scope.$eval(attrs.fileChange, { file: event.target.files[0] });
          });
        });
      }
    };
  });
