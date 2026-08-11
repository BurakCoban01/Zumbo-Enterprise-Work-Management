import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import { BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { CreateProjectWorkItem, ProjectWorkItemCollection, ProjectWorkItemDetail, WorkItemSchema } from './project-work-item.models';
import { ProjectWorkItemService } from './project-work-item.service';

interface CreateDraft { readonly title: string; readonly type: string; readonly priority: string; readonly dueDate: string; readonly parentId: string; }

@Component({
  selector: 'zumbo-work-item-create',
  imports: [ZumboIconComponent],
  templateUrl: './work-item-create.component.html',
  styleUrl: './work-item-create.component.scss'
})
export class WorkItemCreateComponent {
  readonly project = input.required<ProjectSummary>();
  readonly boards = input.required<readonly BoardSummary[]>();
  readonly userId = input.required<string>();
  readonly contextReady = input(false);
  readonly created = output<ProjectWorkItemDetail>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly workItems = inject(ProjectWorkItemService);
  private loadedProjectId = '';
  protected readonly collection = signal<ProjectWorkItemCollection | null>(null);
  protected readonly schema = signal<WorkItemSchema | null>(null);
  protected readonly open = signal(false);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly draft = signal<CreateDraft>(emptyDraft());
  protected readonly activeTypes = computed(() => (this.schema()?.issueTypes ?? []).filter(type => type.active).sort((a, b) => a.position - b.position));
  protected readonly canCreate = computed(() => this.hasPermission('WorkItemCreate') && this.boards().length > 0);
  protected readonly parentCandidates = computed(() => this.collection()?.tasks.filter(task => task.type !== 'Subtask') ?? []);

  constructor() {
    effect(() => {
      const projectId = this.project().id;
      if (!this.contextReady() || projectId === this.loadedProjectId) return;
      this.loadedProjectId = projectId;
      this.loadContext();
    });
  }

  protected show(): void {
    if (!this.canCreate()) return;
    const type = this.activeTypes().find(item => item.key === 'Task')?.key ?? this.activeTypes()[0]?.key ?? 'Task';
    this.draft.set({ ...emptyDraft(), type });
    this.error.set(null);
    this.open.set(true);
    setTimeout(() => document.querySelector<HTMLInputElement>('.work-create-dialog input')?.focus());
  }

  protected close(): void { if (!this.saving()) this.open.set(false); }
  protected update(field: keyof CreateDraft, event: Event): void {
    this.draft.update(value => ({ ...value, [field]: (event.target as HTMLInputElement | HTMLSelectElement).value }));
    this.error.set(null);
  }

  protected submit(): void {
    const draft = this.draft();
    const board = this.boards()[0];
    if (!this.canCreate() || !board || this.saving() || !draft.title.trim()) return;
    const request: CreateProjectWorkItem = {
      projectId: this.project().id,
      boardId: board.id,
      title: draft.title.trim(),
      type: draft.type,
      priority: draft.priority,
      assigneeUserId: this.userId(),
      dueDate: draft.dueDate || null,
      parentId: draft.parentId || null,
      teamId: null,
      customFields: []
    };
    this.saving.set(true);
    this.workItems.create(request).pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: task => { this.open.set(false); this.created.emit(task); },
      error: () => this.error.set('İş öğesi oluşturulamadı; alanları ve proje şemasını kontrol edin.')
    });
  }

  private loadContext(): void {
    this.loading.set(true);
    forkJoin({ collection: this.workItems.load(this.project().id), schema: this.workItems.schema(this.project().id) })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: context => { this.collection.set(context.collection); this.schema.set(context.schema); },
        error: () => this.error.set('İş oluşturma bağlamı yüklenemedi.')
      });
  }

  private hasPermission(permission: string): boolean {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role = this.collection()?.roles.find(item => item.name === membership?.role && item.isActive);
    return !!role?.permissions.some(value => value === '*' || value === permission);
  }
}

function emptyDraft(): CreateDraft { return { title: '', type: 'Task', priority: 'Medium', dueDate: '', parentId: '' }; }
