(function() {
  'use strict';

  angular.module('zumboMobile', ['ionic', 'zumbo.shared.api', 'zumbo.shared.displayNames'])
  .config(function($stateProvider, $urlRouterProvider) {
    function protectedState(definition) {
      definition.resolve = {
        browserSession: function(authService) { return authService.restore(); }
      };
      return definition;
    }
    $stateProvider
      .state('login', { url: '/login', templateUrl: 'templates/login.html', controller: 'LoginController as vm' })
      .state('forgot-password', { url: '/forgot-password', templateUrl: 'templates/forgot-password.html', controller: 'ForgotPasswordController as vm' })
      .state('reset-password', { url: '/reset-password?token', templateUrl: 'templates/reset-password.html', controller: 'ResetPasswordController as vm' })
      .state('public-intake', { url: '/intake/:publicId', templateUrl: 'templates/public-intake.html', controller: 'PublicIntakeController as vm' })
      .state('project-detail', protectedState({ url: '/projects/:projectId', templateUrl: 'templates/project-detail.html', controller: 'ProjectDetailController as vm' }))
      .state('project-catalog', protectedState({ url: '/projects/:projectId/catalog?tab', templateUrl: 'templates/project-catalog.html', controller: 'ProjectCatalogController as vm' }))
      .state('project-intake', protectedState({ url: '/projects/:projectId/intake?tab', templateUrl: 'templates/intake-center.html', controller: 'MobileIntakeController as vm' }))
      .state('project-automation', protectedState({ url: '/projects/:projectId/automation?tab', templateUrl: 'templates/work-automation.html', controller: 'WorkAutomationController as vm' }))
      .state('project-jobs', protectedState({ url: '/projects/:projectId/jobs?mode', templateUrl: 'templates/bulk-job-center.html', controller: 'BulkJobCenterController as vm' }))
      .state('project-planning', protectedState({ url: '/projects/:projectId/plan?mode&zoom&anchor&query&type', templateUrl: 'templates/project-planning.html', controller: 'ProjectPlanningController as vm' }))
      .state('project-reporting', protectedState({ url: '/projects/:projectId/insights?mode&range', templateUrl: 'templates/project-reporting.html', controller: 'ProjectReportingController as vm' }))
      .state('team-detail', protectedState({ url: '/teams/:teamId', templateUrl: 'templates/team-detail.html', controller: 'TeamDetailController as vm' }))
      .state('task-detail', protectedState({ url: '/tasks/:taskId', templateUrl: 'templates/task-detail.html', controller: 'TaskDetailController as vm' }))
      .state('integration-center', protectedState({ url: '/profile/integrations', templateUrl: 'templates/integration-center.html', controller: 'IntegrationCenterController as vm' }))
      .state('operations-center', protectedState({ url: '/profile/operations', templateUrl: 'templates/operations-center.html', controller: 'OperationsCenterController as vm' }))
      .state('portfolio-center', protectedState({ url: '/portfolios', templateUrl: 'templates/portfolios.html', controller: 'PortfolioController as vm' }))
      .state('goal-center', protectedState({ url: '/goals', templateUrl: 'templates/goals.html', controller: 'GoalController as vm' }))
      .state('capacity-center', protectedState({ url: '/capacity', templateUrl: 'templates/capacity.html', controller: 'CapacityController as vm' }))
      .state('knowledge-center', protectedState({ url: '/knowledge', templateUrl: 'templates/knowledge.html', controller: 'KnowledgeController as vm' }))
      .state('app', protectedState({ url: '/app', abstract: true, templateUrl: 'templates/tabs.html' }))
      .state('app.dashboard', { url: '/dashboard', views: { home: { templateUrl: 'templates/dashboard.html', controller: 'DashboardController as vm' } } })
      .state('app.tasks', { url: '/tasks', views: { work: { templateUrl: 'templates/tasks.html', controller: 'TasksController as vm' } } })
      .state('app.create', { url: '/create', views: { create: { templateUrl: 'templates/create.html', controller: 'MobileCreateController as vm' } } })
      .state('app.notifications', { url: '/notifications', views: { inbox: { templateUrl: 'templates/notifications.html', controller: 'NotificationsController as vm' } } })
      .state('app.more', { url: '/more', views: { more: { templateUrl: 'templates/more.html', controller: 'MobileMoreController as vm' } } })
      .state('app.projects', { url: '/projects', views: { more: { templateUrl: 'templates/projects.html', controller: 'ProjectsController as vm' } } })
      .state('app.search', { url: '/search?q', views: { more: { templateUrl: 'templates/search.html', controller: 'MobileSearchController as vm' } } })
      .state('app.profile', { url: '/profile', views: { more: { templateUrl: 'templates/profile.html', controller: 'ProfileSecurityController as vm' } } });
    $urlRouterProvider.otherwise('/login');
  })
  .run(function($rootScope, $state) {
    $rootScope.$on('$stateChangeError', function(event, toState) {
      if (toState.name === 'login') return;
      event.preventDefault();
      $state.go('login');
    });
  });
})();
