import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login.page').then(module => module.MobileLoginPage) },
  { path: 'forgot-password', loadComponent: () => import('./features/auth/mobile-forgot-password.page').then(module => module.MobileForgotPasswordPage) },
  { path: 'reset-password', loadComponent: () => import('./features/auth/mobile-reset-password.page').then(module => module.MobileResetPasswordPage) },
  { path: 'intake/:publicId', loadComponent: () => import('./features/intake/mobile-public-intake.page').then(module => module.MobilePublicIntakePage) },
  {
    path: 'workspace',
    loadComponent: () => import('./shell/mobile-tabs.page').then(module => module.MobileTabsPage),
    children: [
      { path: 'home', loadComponent: () => import('./features/daily-work/mobile-home.page').then(module => module.MobileHomePage) },
      { path: 'work', loadComponent: () => import('./features/daily-work/mobile-my-work.page').then(module => module.MobileMyWorkPage) },
      { path: 'create', loadComponent: () => import('./features/create/mobile-create.page').then(module => module.MobileCreatePage) },
      { path: 'inbox', loadComponent: () => import('./features/inbox/mobile-inbox.page').then(module => module.MobileInboxPage) },
      { path: 'more', loadComponent: () => import('./features/more/mobile-more.page').then(module => module.MobileMorePage) },
      { path: 'account', loadComponent: () => import('./features/account/mobile-account.page').then(module => module.MobileAccountPage) },
      { path: 'portfolios', loadComponent: () => import('./features/strategy/mobile-portfolio.page').then(module => module.MobilePortfolioPage) },
      { path: 'goals', loadComponent: () => import('./features/strategy/mobile-goal.page').then(module => module.MobileGoalPage) },
      { path: 'capacity', loadComponent: () => import('./features/capacity/mobile-capacity.page').then(module => module.MobileCapacityPage) },
      { path: 'knowledge', loadComponent: () => import('./features/knowledge/mobile-knowledge.page').then(module => module.MobileKnowledgePage) },
      { path: 'teams', loadComponent: () => import('./features/teams/mobile-team.page').then(module => module.MobileTeamPage) },
      { path: 'projects', loadComponent: () => import('./workspace.page').then(module => module.MobileWorkspacePage) },
      { path: 'projects/:projectId/catalog', loadComponent: () => import('./features/catalog/mobile-project-catalog.page').then(module => module.MobileProjectCatalogPage) },
      { path: 'projects/:projectId/intake', loadComponent: () => import('./features/intake/mobile-project-intake.page').then(module => module.MobileProjectIntakePage) },
      { path: 'projects/:projectId', loadComponent: () => import('./features/project-hub/mobile-project-hub.page').then(module => module.MobileProjectHubPage) },
      { path: 'projects/:projectId/work', loadComponent: () => import('./features/work/mobile-project-work.page').then(module => module.MobileProjectWorkPage) },
      { path: 'search', loadComponent: () => import('./features/work/mobile-search.page').then(module => module.MobileSearchPage) },
      { path: '', pathMatch: 'full', redirectTo: 'home' }
    ]
  },
  { path: 'tasks/:taskId', loadComponent: () => import('./features/task-detail/mobile-task-detail.page').then(module => module.MobileTaskDetailPage) },
  { path: 'projects/:projectId/catalog', loadComponent: () => import('./features/catalog/mobile-project-catalog.page').then(module => module.MobileProjectCatalogPage) },
  { path: 'projects/:projectId/intake', loadComponent: () => import('./features/intake/mobile-project-intake.page').then(module => module.MobileProjectIntakePage) },
  { path: 'teams/:teamId', loadComponent: () => import('./features/teams/mobile-team.page').then(module => module.MobileTeamPage) },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'workspace' }
];
