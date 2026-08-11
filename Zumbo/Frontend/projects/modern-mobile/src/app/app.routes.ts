import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login.page').then(module => module.MobileLoginPage) },
  { path: 'forgot-password', loadComponent: () => import('./features/auth/mobile-forgot-password.page').then(module => module.MobileForgotPasswordPage) },
  { path: 'reset-password', loadComponent: () => import('./features/auth/mobile-reset-password.page').then(module => module.MobileResetPasswordPage) },
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
      { path: 'projects', loadComponent: () => import('./workspace.page').then(module => module.MobileWorkspacePage) },
      { path: 'projects/:projectId', loadComponent: () => import('./features/project-hub/mobile-project-hub.page').then(module => module.MobileProjectHubPage) },
      { path: 'projects/:projectId/work', loadComponent: () => import('./features/work/mobile-project-work.page').then(module => module.MobileProjectWorkPage) },
      { path: 'search', loadComponent: () => import('./features/work/mobile-search.page').then(module => module.MobileSearchPage) },
      { path: '', pathMatch: 'full', redirectTo: 'home' }
    ]
  },
  { path: 'tasks/:taskId', loadComponent: () => import('./features/task-detail/mobile-task-detail.page').then(module => module.MobileTaskDetailPage) },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'workspace' }
];
