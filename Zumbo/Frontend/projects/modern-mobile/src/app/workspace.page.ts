import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { finalize, switchMap } from 'rxjs';
import { ZumboApiClient, ZumboRealtimeService, ZumboSessionService } from '@zumbo/modern-shared';

interface ProjectSummary {
  readonly id: string;
  readonly key: string;
  readonly name: string;
}

@Component({
  selector: 'zumbo-mobile-workspace',
  imports: [IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonTitle, IonToolbar],
  templateUrl: './workspace.page.html',
  styleUrl: './workspace.page.scss'
})
export class MobileWorkspacePage {
  private readonly api = inject(ZumboApiClient);
  private readonly realtime = inject(ZumboRealtimeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly session = inject(ZumboSessionService);

  protected readonly projects = signal<readonly ProjectSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly selectedProjectId = signal<string | null>(null);
  protected readonly selectedProject = computed(() => this.projects().find(item => item.id === this.selectedProjectId()) ?? null);

  constructor() {
    this.session.restore().pipe(switchMap(auth => {
      if (!auth) {
        void this.router.navigate(['/login']);
        return [];
      }
      return this.api.get<readonly ProjectSummary[]>(`/api/projects?organizationId=${encodeURIComponent(auth.user.organizationId)}`);
    }), finalize(() => this.loading.set(false))).subscribe({
      next: projects => {
        this.projects.set(projects);
        const projectId = this.route.snapshot.paramMap.get('projectId');
        if (projectId && projects.some(item => item.id === projectId)) {
          this.selectedProjectId.set(projectId);
          void this.realtime.connect(projectId).catch(() => this.error.set('Canlı güncellemeler şu anda kullanılamıyor.'));
        }
      },
      error: () => this.error.set('Projeler yüklenemedi.')
    });
  }

  protected open(project: ProjectSummary): void {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
    void this.router.navigate(['/workspace', project.id]);
  }

  protected logout(): void {
    void this.realtime.stop().finally(() => {
      this.session.logout().subscribe(() => void this.router.navigate(['/login']));
    });
  }
}
