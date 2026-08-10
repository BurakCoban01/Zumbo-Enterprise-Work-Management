angular.module('zumboDesktop', ['zumbo.shared.api', 'zumbo.shared.displayNames', 'zumbo.desktop.pwa'])
  .controller('WorkspaceController', function($scope, $window, $document, $timeout, $q, apiClient, sessionStore, realtimeService, displayNameResolver, desktopPwaService, desktopShellFeature, desktopPersonalWorkFeature, desktopProjectDirectoryFeature, desktopProjectOverviewFeature, desktopProjectCatalogFeature, desktopIntakeFeature, desktopWorkAutomationFeature, desktopBulkJobFeature, desktopSettingsFeature, desktopPrivacyFeature, desktopAuditFeature, desktopIntegrationFeature, desktopOperationsFeature, desktopPlanningFeature, desktopPlanningViewsFeature, desktopReportingViewsFeature, desktopPortfolioFeature, desktopGoalFeature, desktopCapacityFeature, desktopKnowledgeFeature, desktopWorkItemFeature, desktopManagementFeature, desktopBoardViewFeature, desktopTaskBoardFeature, desktopBoardExcellenceFeature) {
    var vm = this;
    vm.session = sessionStore;
    vm.pwa = desktopPwaService.state;
    vm.applyUpdate = desktopPwaService.applyUpdate;
    desktopPwaService.start();
    vm.tasks = [];
    vm.audit = [];
    vm.boardAudit = [];
    vm.organizationAudit = [];
    vm.roles = [];
    vm.roleDraft = { name: '', permissions: ['WorkItemView'] };
    vm.userRoleDrafts = {};
    vm.permissionCatalog = [
      'UserRoleManage', 'AuditReadAll', 'OrganizationManage', 'IntegrationManage',
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
    vm.sprints = [];
    vm.backlogItems = [];
    vm.timelineEntries = [];
    vm.calendarGroups = [];
    vm.roadmapSprints = [];
    vm.workMode = 'overview';
    vm.burndown = [];
    vm.workflow = { transitions: [] };
    vm.nextStatus = '';
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
    vm.workItemSchema = { issueTypes: [], customFields: [], layouts: [] };
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
    vm.cardFields = angular.extend({ type: true, priority: true, assignee: true, dueDate: true, labels: true, estimate: true, blockers: true },
      JSON.parse(window.localStorage.getItem('zumbo.cardFields') || '{}'));
    vm.favoriteProjects = JSON.parse(window.localStorage.getItem('zumbo.favoriteProjects') || '[]');
    vm.theme = window.localStorage.getItem('zumbo.theme') || 'light';
    vm.density = window.localStorage.getItem('zumbo.density') || 'comfortable';
    vm.navCollapsed = window.localStorage.getItem('zumbo.navCollapsed') === 'true';
    vm.recentProjects = JSON.parse(window.localStorage.getItem('zumbo.recentProjects') || '[]');
    vm.userName = function(userId) {
      return displayNameResolver.user(userId, vm.users, vm.session.currentUser);
    };
    vm.organizationName = function() {
      var id = vm.session.currentUser && vm.session.currentUser.organizationId;
      return displayNameResolver.organization(id, vm.organizations, vm.organization);
    };
    vm.sprintName = function(sprintId) {
      return displayNameResolver.sprint(sprintId, vm.sprints);
    };
    vm.commands = [
      { label: 'Ana sayfayı aç', group: 'Navigasyon', icon: 'house', action: 'section', value: 'home' },
      { label: 'İşlerimi aç', group: 'Navigasyon', icon: 'list-checks', action: 'section', value: 'mywork' },
      { label: 'Gelen kutusunu aç', group: 'Navigasyon', icon: 'inbox', action: 'section', value: 'inbox' },
      { label: 'Proje genel bakışını aç', group: 'Navigasyon', icon: 'layout-dashboard', action: 'projectView', value: 'overview' },
      { label: 'Panoyu aç', group: 'Navigasyon', icon: 'kanban', action: 'projectView', value: 'board' }, { label: 'Intake merkezini aç', group: 'Navigasyon', icon: 'clipboard-list', action: 'projectView', value: 'intake' },
      { label: 'Projeleri aç', group: 'Navigasyon', icon: 'folder-kanban', action: 'section', value: 'projects' }, { label: 'Portföyleri aç', group: 'Navigasyon', icon: 'milestone', action: 'section', value: 'portfolios' }, { label: 'Hedefleri aç', group: 'Navigasyon', icon: 'target', action: 'section', value: 'goals' }, { label: 'Kapasite planlarını aç', group: 'Navigasyon', icon: 'gauge', action: 'section', value: 'capacity' }, { label: 'Bilgi dokümanlarını aç', group: 'Navigasyon', icon: 'book-open-text', action: 'section', value: 'knowledge' },
      { label: 'Ekipleri aç', group: 'Navigasyon', icon: 'users-round', action: 'section', value: 'teams' },
      { label: 'Raporları aç', group: 'Navigasyon', icon: 'chart-no-axes-combined', action: 'projectView', value: 'reports' },
      { label: 'Denetim merkezini aç', group: 'Navigasyon', icon: 'scroll-text', action: 'section', value: 'audit', requires: 'audit' },
      { label: 'Ayarları aç', group: 'Navigasyon', icon: 'settings', action: 'section', value: 'settings' },
      { label: 'Arşivi aç', group: 'Navigasyon', icon: 'archive-restore', action: 'section', value: 'archive' },
      { label: 'Yeni görev oluştur', group: 'Eylem', icon: 'plus', action: 'create' },
      { label: 'Temayı değiştir', group: 'Görünüm', icon: 'sun-moon', action: 'theme' },
      { label: 'Kart yoğunluğunu değiştir', group: 'Görünüm', icon: 'rows-3', action: 'density' }
    ];
    desktopShellFeature.install(vm);
    desktopPersonalWorkFeature.install(vm, { membershipFor: membershipFor });
    vm.showSection = function(section) {
      var sectionChanged = section !== vm.activeSection;
      if (sectionChanged && vm.clearSettingsOneTimeSecrets) vm.clearSettingsOneTimeSecrets();
      vm.activeSection = section;
      vm.closeCommandPalette();
      if (sectionChanged) {
        vm.selectedTask = null;
        vm.taskDraft = null;
        vm.audit = [];
      }
      updateLocation(section, null, true);
      loadSectionData(section);
    };
    vm.notify = function(kind, message) {
      vm.feedback = { kind: kind, message: message };
      $timeout.cancel(vm.feedbackTimer);
      vm.feedbackTimer = $timeout(function() { vm.feedback = null; }, 5000);
    };
    $scope.$on('zumbo:concurrency-conflict', function(_, conflict) {
      vm.notify('error', 'Bu kayıt başka bir kullanıcı tarafından değiştirildi. Güncel veriler yüklendi; değişikliğinizi yeniden uygulayın.');
      var target = conflict.resource;
      if (!target) return;
      if (target.kind === 'work-items') {
        vm.loadTasks();
        if (vm.selectedTask && vm.selectedTask.id === target.id) vm.reloadSelectedTaskAfterConflict();
      } else if (target.kind === 'teams') {
        vm.loadTeams();
      } else if (target.kind === 'projects') {
        if (vm.reloadProjectAfterConflict) vm.reloadProjectAfterConflict(); else vm.reloadProjects();
      } else if (target.kind === 'portfolios') { vm.loadPortfolios(); } else if (target.kind === 'goals') { vm.loadGoals(); } else if (target.kind === 'capacity-plans') { vm.loadCapacityPlans(); } else if (target.kind === 'knowledge-documents') { vm.loadKnowledge();
      } else if (target.kind === 'work-item-templates' || target.kind === 'work-item-recurrences') {
        if (vm.reloadWorkAutomationAfterConflict) vm.reloadWorkAutomationAfterConflict();
      } else if (target.kind === 'boards') {
        vm.loadBoards();
      } else if (target.kind === 'workflows' && vm.project) {
        vm.loadWorkflow(vm.project.id);
      }
    });
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
    function onGlobalKeydown(event) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        $scope.$applyAsync(vm.openCommandPalette);
      } else if (event.key === 'Escape' && vm.commandOpen) {
        $scope.$applyAsync(vm.closeCommandPalette);
      }
    }
    function loadSectionData(section) {
      if (!vm.session.currentUser) return;
      if (section === 'archive' && vm.project) vm.loadArchivedTasks();
      if (section === 'home' || section === 'mywork' || section === 'inbox') vm.loadPersonalWork();
      if (section === 'inbox') vm.loadNotifications();
      if (section === 'teams') vm.loadTeams();
      if (section === 'projects') vm.loadUsers(); if (section === 'portfolios') vm.loadPortfolios(); if (section === 'goals') { vm.loadUsers(); vm.loadGoals(); } if (section === 'capacity') { vm.loadUsers(); vm.loadTeams(); vm.loadCapacityPlans(); } if (section === 'knowledge') { vm.loadUsers(); vm.loadKnowledge(); }
      if (section === 'settings') vm.loadSettings(); else if (section === 'audit') vm.loadAuditCenter(true);
    }
    function updateLocation(section, taskId, push) {
      var params = new URLSearchParams();
      params.set('section', section || 'board');
      if (vm.project) params.set('project', vm.project.id);
      if (vm.board && (section === 'board' || section === 'projects')) params.set('board', vm.board.id);
      if (vm.project && (section === 'board' || section === 'reports')) {
        params.set('view', vm.workMode || 'overview');
        if (vm.search) params.set('query', vm.search);
        if (vm.priorityFilter) params.set('priority', vm.priorityFilter);
        if (vm.isPlanningView && vm.isPlanningView()) {
          params.set('calendar', vm.planningCalendarMode);
          params.set('zoom', vm.planningZoom);
          params.set('anchor', window.ZumboPlanningCore.dateKey(vm.planningAnchor));
          if (vm.planningFilters.query) params.set('planQuery', vm.planningFilters.query);
          if (vm.planningFilters.assignee) params.set('assignee', vm.planningFilters.assignee);
          if (vm.planningFilters.team) params.set('team', vm.planningFilters.team);
          if (vm.planningFilters.type) params.set('type', vm.planningFilters.type);
        }
        if (vm.isReportingView && vm.isReportingView()) params.set('range', vm.reportingRangeDays);
      }
      if (vm.selectedTeam && section === 'teams') params.set('team', vm.selectedTeam.id);
      if (taskId) {
        params.set('task', taskId);
        if (vm.taskDetailMode === 'page') params.set('detail', 'page');
      }
      var next = '#' + params.toString();
      if ($window.location.hash === next) return;
      $window.history[push ? 'pushState' : 'replaceState'](null, '', next);
    }
    function applyLocation() {
      var params = new URLSearchParams($window.location.hash.slice(1)); if (vm.applyPublicIntakeLocation(params)) return;
      var section = params.get('section');
      var sectionChanged = ['home', 'mywork', 'inbox', 'board', 'projects', 'portfolios', 'goals', 'capacity', 'knowledge', 'teams', 'reports', 'audit', 'archive', 'settings'].indexOf(section) >= 0 && vm.activeSection !== section;
      if (sectionChanged) {
        if (vm.clearSettingsOneTimeSecrets) vm.clearSettingsOneTimeSecrets();
        vm.activeSection = section;
        loadSectionData(section);
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
      vm.taskDetailMode = params.get('detail') === 'page' ? 'page' : 'drawer';
      if (taskId && vm.session.currentUser && (!vm.selectedTask || vm.selectedTask.id !== taskId)) vm.selectTask({ id: taskId }, true);
      else if (!taskId) { vm.selectedTask = null; vm.taskDraft = null; }
      var teamId = params.get('team');
      if (teamId && vm.teams.length) {
        var linkedTeam = vm.teams.find(function(team) { return team.id === teamId; });
        if (linkedTeam && (!vm.selectedTeam || vm.selectedTeam.id !== linkedTeam.id)) vm.selectTeam(linkedTeam, true);
      }
      if (vm.project && (params.get('section') === 'board' || params.get('section') === 'reports')) {
        vm.applyProjectViewLocation(params);
      }
      if (!sectionChanged) loadSectionData(vm.activeSection);
    }
    function onPopState() { $scope.$applyAsync(applyLocation); }
    $document.on('keydown', onGlobalKeydown);
    $window.addEventListener('popstate', onPopState);
    var unsubscribeRealtime = realtimeService.subscribe(function(change) {
      if (change.eventType === 'resyncRequired') {
        if (vm.project && change.projectId === vm.project.id) vm.loadTasks();
        return;
      }
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
        else { vm.refreshSelectedTaskFromRealtime(change.workItem); }
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
      window.localStorage.setItem('zumbo.currentUser', JSON.stringify(auth.user));
      window.sessionStorage.setItem('zumbo.csrfToken', auth.csrfToken);
      return auth;
    }

    vm.login = function() {
      vm.loginError = null;
      return apiClient.post('/api/browser-auth/login', vm.loginForm)
        .then(acceptAuth)
        .then(vm.restore)
        .catch(function(error) {
          var code = error.data && error.data.error && error.data.error.code;
          vm.mfaRequired = code === 'MFA_REQUIRED' || code === 'MFA_INVALID';
          vm.loginError = vm.mfaRequired ? 'Doğrulama kodunu kontrol edin.' : 'Giriş başarısız.';
        });
    };

    vm.logout = function() {
      return apiClient.post('/api/browser-auth/logout', { allSessions: false }).finally(function() {
        realtimeService.stop();
        apiClient.clearSession('logout');
        vm.project = null;
        vm.board = null;
        vm.tasks = [];
      });
    };

    var desktopTasks = desktopTaskBoardFeature.install(vm, {
      acceptAuth: acceptAuth,
      setProjectState: function(project) { return desktopManagement.setProjectState(project); },
      rememberProject: function(project) { return desktopManagement.rememberProject(project); },
      membershipFor: function(project) { return desktopManagement.membershipFor(project); }
    });

    desktopSettingsFeature.install(vm, desktopTasks.apiActionError); desktopPrivacyFeature.install(vm, desktopTasks.apiActionError); desktopAuditFeature.install(vm, desktopTasks.apiActionError); desktopIntegrationFeature.install(vm, desktopTasks.apiActionError); desktopOperationsFeature.install(vm, desktopTasks.apiActionError);
    desktopPlanningFeature.install(vm, desktopTasks.apiActionError);

    desktopWorkItemFeature.install(vm, {
      updateLocation: updateLocation,
      nextStatusFor: function(status) { return desktopBoardView.nextStatusFor(status); },
      apiActionError: desktopTasks.apiActionError
    });
    var desktopBoardView = desktopBoardViewFeature.install(vm, {
      setBoardState: function(board) { return desktopManagement.setBoardState(board); },
      apiActionError: desktopTasks.apiActionError
    });

    var desktopManagement = desktopManagementFeature.install(vm, {
      updateLocation: updateLocation,
      apiActionError: desktopTasks.apiActionError
    });
    desktopProjectCatalogFeature.install(vm, { setProjectState: desktopManagement.setProjectState }); desktopIntakeFeature.install(vm);
    desktopWorkAutomationFeature.install(vm);
    desktopBulkJobFeature.install(vm, desktopTasks.apiActionError);
    desktopBoardExcellenceFeature.install(vm, { storage: $window.localStorage, apiActionError: desktopTasks.apiActionError });
    desktopPlanningViewsFeature.install(vm, {
      storage: $window.localStorage,
      updateLocation: updateLocation,
      apiActionError: desktopTasks.apiActionError
    });
    desktopReportingViewsFeature.install(vm, {
      updateLocation: updateLocation,
      apiActionError: desktopTasks.apiActionError
    });
    desktopProjectDirectoryFeature.install(vm, { membershipFor: membershipFor });
    desktopPortfolioFeature.install(vm, { apiActionError: desktopTasks.apiActionError }); desktopGoalFeature.install(vm, { apiActionError: desktopTasks.apiActionError }); desktopCapacityFeature.install(vm, { apiActionError: desktopTasks.apiActionError }); desktopKnowledgeFeature.install(vm, { apiActionError: desktopTasks.apiActionError }); desktopProjectOverviewFeature.install(vm, { updateLocation: updateLocation });
    function membershipFor(project) { return desktopManagement.membershipFor(project); }
    function firstAccessibleProject(projects) { return desktopManagement.firstAccessibleProject(projects); }
    function setBoardState(board) { return desktopManagement.setBoardState(board); }
    function setProjectState(project) { return desktopManagement.setProjectState(project); }
    function rememberProject(project) { return desktopManagement.rememberProject(project); }

    vm.restore = function() {
      if (!vm.session.currentUser) return;
      return apiClient.get('/api/projects?organizationId=' + encodeURIComponent(vm.session.currentUser.organizationId))
        .then(function(projects) {
          vm.projects = projects;
          var rememberedId = window.localStorage.getItem('zumbo.projectId');
          var remembered = projects.find(function(project) { return project.id === rememberedId; });
          var route = new URLSearchParams($window.location.hash.slice(1));
          var linked = projects.find(function(project) { return project.id === route.get('project'); });
          if (linked && !membershipFor(linked)) {
            apiClient.transitionContext('permission-lost:' + linked.id);
            setProjectState(linked);
            vm.activeSection = 'projects';
            vm.board = null;
            vm.boards = [];
            vm.tasks = [];
            vm.backlogItems = [];
            vm.sprints = [];
            vm.timelineEntries = [];
            vm.summary = {};
            vm.statusDistribution = [];
            vm.workload = [];
            vm.dueDateRisks = [];
            vm.velocity = [];
            vm.selectedTask = null;
            vm.clearSelection();
            updateLocation('projects', null, false);
            return;
          }
          var selected = membershipFor(linked)
            ? linked
            : (membershipFor(remembered) ? remembered : firstAccessibleProject(projects));
          if (!selected) return;
          window.localStorage.setItem('zumbo.projectId', selected.id);
          var selection = vm.selectProject(selected, true);
          applyLocation();
          return selection;
        }).then(function() {
          loadSectionData(vm.activeSection);
          return $q.all([vm.loadNotifications(), vm.loadTeams(), vm.loadUsers(), vm.loadAuditCapabilities()]);
        }).then(applyLocation);
    };
    if (!vm.applyPublicIntakeLocation(new URLSearchParams($window.location.hash.slice(1)))) apiClient.get('/api/browser-auth/session').then(acceptAuth).then(vm.restore).catch(function() {
        apiClient.clearSession('restore-failed');
        vm.project = null;
        vm.board = null;
      });
    applyLocation();
  });
