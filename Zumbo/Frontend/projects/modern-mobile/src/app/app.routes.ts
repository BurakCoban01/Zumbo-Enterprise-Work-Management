import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login.page').then(module => module.MobileLoginPage) },
  { path: 'workspace', loadComponent: () => import('./workspace.page').then(module => module.MobileWorkspacePage) },
  { path: 'workspace/:projectId', loadComponent: () => import('./workspace.page').then(module => module.MobileWorkspacePage) },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'workspace' }
];
