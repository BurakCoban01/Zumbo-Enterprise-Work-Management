import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login.page').then(module => module.DesktopLoginPage) },
  { path: 'workspace', loadComponent: () => import('./workspace.page').then(module => module.DesktopWorkspacePage) },
  { path: 'workspace/section/:section', loadComponent: () => import('./workspace.page').then(module => module.DesktopWorkspacePage) },
  { path: 'workspace/:projectId', loadComponent: () => import('./workspace.page').then(module => module.DesktopWorkspacePage) },
  { path: 'workspace/:projectId/:view', loadComponent: () => import('./workspace.page').then(module => module.DesktopWorkspacePage) },
  { path: 'workspace/:projectId/:view/task/:taskId', loadComponent: () => import('./workspace.page').then(module => module.DesktopWorkspacePage) },
  { path: '', pathMatch: 'full', redirectTo: 'workspace' },
  { path: '**', redirectTo: 'workspace' }
];
