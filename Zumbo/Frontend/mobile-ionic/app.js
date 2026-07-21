(function() {
  'use strict';

  angular.module('zumboMobile', ['ionic', 'zumbo.shared.api', 'zumbo.shared.displayNames'])
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
  });
})();
