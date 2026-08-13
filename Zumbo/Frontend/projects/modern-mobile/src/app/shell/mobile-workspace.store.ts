import { Injectable, inject, signal } from '@angular/core';
import { ZumboApiClient, ZumboSessionService } from '@zumbo/modern-shared';
import { catchError, firstValueFrom, forkJoin, map, of } from 'rxjs';
import { MobileNotification, MobileProject, MobileSearchResult, MobileWorkItem } from './mobile-workspace.models';

@Injectable({ providedIn: 'root' })
export class MobileWorkspaceStore {
  private readonly api = inject(ZumboApiClient);
  private readonly session = inject(ZumboSessionService);
  private loadPromise: Promise<void> | null = null;

  readonly projects = signal<readonly MobileProject[]>([]);
  readonly tasks = signal<readonly MobileWorkItem[]>([]);
  readonly notifications = signal<readonly MobileNotification[]>([]);
  readonly loading = signal(false);
  readonly partial = signal(false);
  readonly error = signal<string | null>(null);
  readonly ready = signal(false);

  load(force = false): Promise<void> {
    if (this.ready() && !force) return Promise.resolve();
    if (this.loadPromise) return this.loadPromise;
    const user = this.session.currentUser();
    if (!user) return Promise.reject(new Error('Session unavailable.'));
    this.loading.set(true);
    this.error.set(null);
    this.loadPromise = firstValueFrom(this.api.get<readonly MobileProject[]>(`/api/projects?organizationId=${encodeURIComponent(user.organizationId)}`).pipe(
      map(projects => ({ projects })),
      catchError(() => { throw new Error('Projeler yüklenemedi.'); })
    )).then(({ projects }) => {
      const workProjects = projects.filter(project => project.members?.some(member => member.userId === user.id));
      return firstValueFrom(forkJoin({
        work: workProjects.length ? forkJoin(workProjects.map(project => this.api.post<MobileSearchResult>('/api/work-items/search', {
          projectId: project.id, assigneeUserId: user.id, page: 1, pageSize: 50
        }).pipe(map(result => ({ project, result })), catchError(() => of({ project, result: null }))))) : of([]),
        notifications: this.api.get<readonly MobileNotification[]>('/api/notifications?page=1&pageSize=50').pipe(catchError(() => of([])))
      })).then(result => ({ projects, ...result }));
    }).then(({ projects, work, notifications }) => {
      this.projects.set(projects);
      this.tasks.set(work.flatMap(({ project, result }) => (result?.items ?? []).map(item => ({ ...item, projectName: project.name }))));
      this.notifications.set(notifications);
      this.partial.set(work.some(({ result }) => result === null));
      this.ready.set(true);
    }).catch(error => {
      this.error.set(error instanceof Error ? error.message : 'Çalışma alanı yüklenemedi.');
      throw error;
    }).finally(() => {
      this.loading.set(false);
      this.loadPromise = null;
    });
    return this.loadPromise;
  }

  async markRead(id: string): Promise<void> {
    await firstValueFrom(this.api.patch(`/api/notifications/${encodeURIComponent(id)}/read`, {}));
    this.notifications.update(items => items.map(item => item.id === id ? { ...item, read: true } : item));
  }
}
