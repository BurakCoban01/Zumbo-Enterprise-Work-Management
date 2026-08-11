import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonContent, IonHeader, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { ZumboApiClient, ZumboSessionService } from '@zumbo/modern-shared';
import { finalize, forkJoin } from 'rxjs';
import { MobileConnectivityService } from '../../shell/mobile-connectivity.service';
import { MobileBoard, MobileRole, MobileSchema } from '../../shell/mobile-workspace.models';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';

interface CreatedTask {
  readonly id: string;
  readonly title: string;
}

@Component({
  selector: 'zumbo-mobile-create',
  imports: [FormsModule, IonContent, IonHeader, IonTitle, IonToolbar],
  templateUrl: './mobile-create.page.html',
  styleUrl: './mobile-create.page.scss'
})
export class MobileCreatePage {
  private readonly api = inject(ZumboApiClient);
  protected readonly connectivity = inject(MobileConnectivityService);
  protected readonly store = inject(MobileWorkspaceStore);
  private readonly session = inject(ZumboSessionService);

  protected projectId = '';
  protected title = '';
  protected type = 'Task';
  protected priority = 'Medium';
  protected dueDate = '';
  protected readonly boards = signal<readonly MobileBoard[]>([]);
  protected readonly roles = signal<readonly MobileRole[]>([]);
  protected readonly schema = signal<MobileSchema | null>(null);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly types = computed(() =>
    [...(this.schema()?.issueTypes ?? [])].filter(item => item.active).sort((a, b) => a.position - b.position)
  );
  protected readonly canCreate = computed(() => {
    const user = this.session.currentUser();
    const project = this.store.projects().find(item => item.id === this.projectId);
    const roleName = project?.members?.find(item => item.userId === user?.id)?.role;
    const role = this.roles().find(item => item.name === roleName && item.isActive);
    return !!this.boards().length && !!role?.permissions.some(item => item === '*' || item === 'WorkItemCreate');
  });

  protected loadContext(): void {
    if (!this.projectId) return;

    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      boards: this.api.get<readonly MobileBoard[]>(`/api/boards/by-project/${encodeURIComponent(this.projectId)}`),
      roles: this.api.get<readonly MobileRole[]>('/api/auth/roles?scope=Project'),
      schema: this.api.get<MobileSchema>(`/api/work-item-schemas/${encodeURIComponent(this.projectId)}`)
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: value => {
          this.boards.set(value.boards);
          this.roles.set(value.roles);
          this.schema.set(value.schema);
          this.type = this.types().find(item => item.key === 'Task')?.key ?? this.types()[0]?.key ?? 'Task';
        },
        error: () => this.error.set('Görev oluşturma bağlamı yüklenemedi.')
      });
  }

  protected submit(): void {
    const user = this.session.currentUser();
    const board = this.boards()[0];
    if (!user || !board || !this.canCreate() || this.connectivity.offline() || this.saving() || !this.title.trim()) return;

    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.api
      .post<CreatedTask>(
        '/api/work-items',
        {
          projectId: this.projectId,
          boardId: board.id,
          title: this.title.trim(),
          type: this.type,
          priority: this.priority,
          assigneeUserId: user.id,
          dueDate: this.dueDate || null,
          parentId: null,
          teamId: null,
          customFields: []
        },
        { idempotencyKey: this.api.newIdempotencyKey() }
      )
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: item => {
          this.notice.set(`${item.title} oluşturuldu.`);
          this.title = '';
          void this.store.load(true);
        },
        error: () => this.error.set('Görev oluşturulamadı. Alanları ve proje yetkinizi kontrol edin.')
      });
  }
}
