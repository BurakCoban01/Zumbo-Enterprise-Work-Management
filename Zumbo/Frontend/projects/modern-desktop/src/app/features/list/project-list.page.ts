import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ZumboApiError, ZumboRealtimeService } from '@zumbo/modern-shared';
import { Observable, finalize } from 'rxjs';
import { BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import {
  BulkWorkItemResponse,
  ProjectWorkItem,
  ProjectWorkItemRole,
  ProjectWorkItemUpdate
} from '../work-items/project-work-item.models';
import { ProjectWorkItemService } from '../work-items/project-work-item.service';
import {
  DEFAULT_LIST_PREFERENCES,
  ListColumn,
  ListDensity,
  ListSortField,
  ProjectListData,
  ProjectListPreferences
} from './project-list.models';
import { ProjectListService } from './project-list.service';

interface ListEditDraft {
  readonly title: string;
  readonly priority: string;
  readonly dueDate: string;
}

const PREFERENCES_KEY = 'zumbo.listPreferences';
const PRIORITY_ORDER: Readonly<Record<string, number>> = { Critical: 0, High: 1, Medium: 2, Low: 3 };

@Component({
  selector: 'zumbo-project-list-page',
  imports: [RouterLink, ZumboIconComponent],
  providers: [ProjectListService],
  templateUrl: './project-list.page.html',
  styleUrls: ['./project-list.page.scss', './project-list-table.scss', './project-list-responsive.scss']
})
export class ProjectListPage {
  readonly project = input.required<ProjectSummary>();
  readonly boards = input.required<readonly BoardSummary[]>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(ZumboRealtimeService);
  private contextProjectId = '';
  protected readonly data = signal<ProjectListData | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly query = signal('');
  protected readonly priority = signal('');
  protected readonly selectedBoardId = signal<string | null>(null);
  protected readonly preferences = signal(readPreferences());
  protected readonly columnMenuOpen = signal(false);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly pending = signal(false);
  protected readonly editTaskId = signal<string | null>(null);
  protected readonly editDraft = signal<ListEditDraft | null>(null);
  protected readonly bulkTargetStatus = signal('');

  protected readonly board = computed(() => this.boards().find(item => item.id === this.selectedBoardId()) ?? this.boards()[0] ?? null);
  protected readonly canEdit = computed(() => this.hasPermission('WorkItemUpdate'));
  protected readonly canMove = computed(() => this.hasPermission('WorkItemMove'));
  protected readonly canAssign = computed(() => this.hasPermission('WorkItemAssign'));
  protected readonly canArchive = computed(() => this.hasPermission('WorkItemDelete'));
  protected readonly visibleTasks = computed(() => {
    const board = this.board();
    const query = this.query().trim().toLocaleLowerCase('tr-TR');
    if (!board) return [];
    const tasks = (this.data()?.tasks ?? []).filter(task => task.boardId === board.id
      && (!this.priority() || task.priority === this.priority())
      && (!query || `${task.title} ${task.type} ${task.status} ${task.labels.join(' ')}`.toLocaleLowerCase('tr-TR').includes(query)));
    return tasks.slice().sort((left, right) => this.compareTasks(left, right));
  });
  protected readonly selectedIds = computed(() => this.visibleTasks().map(task => task.id).filter(id => this.selected().has(id)).slice(0, 100));
  protected readonly allVisibleSelected = computed(() => this.visibleTasks().length > 0 && this.visibleTasks().every(task => this.selected().has(task.id)));
  protected readonly bulkTransitionOptions = computed(() => {
    const tasks = this.visibleTasks().filter(task => this.selected().has(task.id));
    if (!tasks.length) return [];
    const transitions = this.data()?.workflow.transitions ?? [];
    const firstTargets = transitions.filter(item => item.fromStatus === tasks[0].status).map(item => item.toStatus);
    return [...new Set(firstTargets)].filter(target => tasks.every(task => transitions.some(item => item.fromStatus === task.status && item.toStatus === target)));
  });

  constructor(
    private readonly projectList: ProjectListService,
    private readonly workItems: ProjectWorkItemService
  ) {
    effect(() => {
      const projectId = this.project().id;
      if (!this.contextReady() || projectId === this.contextProjectId) return;
      this.contextProjectId = projectId;
      const stored = localStorage.getItem(`zumbo.board.${projectId}`);
      this.selectedBoardId.set(this.boards().some(board => board.id === stored) ? stored : this.boards()[0]?.id ?? null);
      this.load();
    });
    this.realtime.changes$.pipe(takeUntilDestroyed()).subscribe(change => {
      if (change.projectId === this.project().id && !this.pending()) this.load(false);
    });
    this.realtime.resync$.pipe(takeUntilDestroyed()).subscribe(() => this.load(false));
  }

  protected load(showLoading = true): void {
    if (showLoading) this.loading.set(true);
    this.error.set(null);
    this.projectList.load(this.project().id).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: data => {
        this.data.set(data);
        this.selected.update(ids => new Set([...ids].filter(id => data.tasks.some(task => task.id === id))));
        this.realtime.synchronize(data.tasks);
      },
      error: () => this.error.set('Liste verileri yüklenemedi.')
    });
  }

  protected selectBoard(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    if (!this.boards().some(board => board.id === id)) return;
    this.selectedBoardId.set(id);
    this.selected.set(new Set());
    localStorage.setItem(`zumbo.board.${this.project().id}`, id);
  }

  protected setQuery(event: Event): void { this.query.set((event.target as HTMLInputElement).value); }
  protected setPriority(event: Event): void { this.priority.set((event.target as HTMLSelectElement).value); }
  protected setDensity(density: ListDensity): void { this.updatePreferences({ ...this.preferences(), density }); }

  protected toggleColumn(column: ListColumn): void {
    const current = this.preferences();
    this.updatePreferences({ ...current, columns: { ...current.columns, [column]: !current.columns[column] } });
  }

  protected columnVisible(column: ListColumn): boolean { return this.preferences().columns[column]; }

  protected sortBy(sort: ListSortField): void {
    const current = this.preferences();
    this.updatePreferences({
      ...current,
      sort,
      direction: current.sort === sort && current.direction === 'asc' ? 'desc' : 'asc'
    });
  }

  protected sortLabel(sort: ListSortField): string {
    if (this.preferences().sort !== sort) return '';
    return this.preferences().direction === 'asc' ? 'artan' : 'azalan';
  }

  protected toggleTask(taskId: string): void {
    this.selected.update(ids => {
      const next = new Set(ids);
      next.has(taskId) ? next.delete(taskId) : next.add(taskId);
      return next;
    });
  }

  protected toggleAllVisible(): void {
    const checked = this.allVisibleSelected();
    this.selected.update(ids => {
      const next = new Set(ids);
      this.visibleTasks().slice(0, 100).forEach(task => checked ? next.delete(task.id) : next.add(task.id));
      return next;
    });
  }

  protected clearSelection(): void { this.selected.set(new Set()); }

  protected startEdit(task: ProjectWorkItem): void {
    if (!this.canEdit() || this.pending()) return;
    this.editTaskId.set(task.id);
    this.editDraft.set({ title: task.title, priority: task.priority, dueDate: dateInputValue(task.dueDate) });
    this.notice.set(null);
  }

  protected updateDraft(field: keyof ListEditDraft, event: Event): void {
    const draft = this.editDraft();
    if (!draft) return;
    this.editDraft.set({ ...draft, [field]: (event.target as HTMLInputElement | HTMLSelectElement).value });
  }

  protected cancelEdit(): void { this.editTaskId.set(null); this.editDraft.set(null); }

  protected saveEdit(task: ProjectWorkItem): void {
    const draft = this.editDraft();
    if (!draft?.title.trim() || this.editTaskId() !== task.id || this.pending()) return;
    const update: ProjectWorkItemUpdate = {
      title: draft.title.trim(),
      description: task.description ?? '',
      priority: draft.priority,
      dueDate: draft.dueDate || null
    };
    const snapshot = this.data();
    if (!snapshot) return;
    this.pending.set(true);
    this.data.set({ ...snapshot, tasks: snapshot.tasks.map(item => item.id === task.id ? { ...item, ...update } : item) });
    this.workItems.update(task, update).pipe(
      finalize(() => this.pending.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: authoritative => {
        this.data.update(data => data ? { ...data, tasks: data.tasks.map(item => item.id === task.id ? authoritative : item) } : data);
        this.cancelEdit();
        this.notice.set('Liste satırı kaydedildi.');
      },
      error: error => {
        this.data.set(snapshot);
        this.cancelEdit();
        const normalized = error as ZumboApiError;
        this.notice.set(normalized.code === 'CONCURRENCY_CONFLICT'
          ? 'Satır başka bir kullanıcı tarafından değiştirildi; güncel liste yükleniyor.'
          : 'Satır kaydedilemedi; önceki değerler geri yüklendi.');
        if (normalized.code === 'CONCURRENCY_CONFLICT') this.load(false);
      }
    });
  }

  protected bulkMove(): void {
    const status = this.bulkTargetStatus();
    if (!this.bulkTransitionOptions().includes(status)) return;
    this.runBulk(this.workItems.bulkMove(this.selectedIds(), status), 'Taşıma');
  }

  protected bulkAssignToMe(): void {
    this.runBulk(this.workItems.bulkAssign(this.selectedIds(), this.userId()), 'Atama');
  }

  protected bulkArchive(): void {
    if (!window.confirm(`${this.selectedIds().length} iş öğesi arşivlensin mi?`)) return;
    this.runBulk(this.workItems.bulkArchive(this.selectedIds()), 'Arşivleme');
  }

  protected userName(id: string | null | undefined): string {
    if (!id) return 'Atanmamış';
    const user = this.data()?.users.find(item => item.id === id);
    return user?.username || user?.email || 'Proje üyesi';
  }

  protected priorityLabel(priority: string): string {
    return ({ Critical: 'Kritik', High: 'Yüksek', Medium: 'Orta', Low: 'Düşük' } as Readonly<Record<string, string>>)[priority] ?? priority;
  }

  protected formatDate(value: string | null | undefined): string {
    return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(value)) : 'Tarih yok';
  }

  private runBulk(request: Observable<BulkWorkItemResponse>, label: string): void {
    if (!this.selectedIds().length || this.pending()) return;
    this.pending.set(true);
    request.pipe(finalize(() => this.pending.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.selected.set(new Set(result.results.filter(item => !item.success).map(item => item.workItemId)));
        this.bulkTargetStatus.set('');
        this.notice.set(`${label}: ${result.succeeded} başarılı${result.failed ? `, ${result.failed} başarısız` : ''}.`);
        this.load(false);
      },
      error: () => this.notice.set(`${label} tamamlanamadı; seçim korundu.`)
    });
  }

  private hasPermission(permission: string): boolean {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role: ProjectWorkItemRole | undefined = this.data()?.roles.find(item => item.name === membership?.role && item.isActive);
    return !!role?.permissions.some(value => value === '*' || value === permission);
  }

  private updatePreferences(preferences: ProjectListPreferences): void {
    this.preferences.set(preferences);
    localStorage.setItem(PREFERENCES_KEY, JSON.stringify(preferences));
  }

  private compareTasks(left: ProjectWorkItem, right: ProjectWorkItem): number {
    const preferences = this.preferences();
    let result = 0;
    if (preferences.sort === 'title') result = left.title.localeCompare(right.title, 'tr-TR');
    else if (preferences.sort === 'status') result = left.status.localeCompare(right.status, 'tr-TR');
    else if (preferences.sort === 'priority') result = (PRIORITY_ORDER[left.priority] ?? 99) - (PRIORITY_ORDER[right.priority] ?? 99);
    else if (preferences.sort === 'assignee') result = this.userName(left.assigneeUserId).localeCompare(this.userName(right.assigneeUserId), 'tr-TR');
    else if (preferences.sort === 'dueDate') {
      if (!left.dueDate || !right.dueDate) {
        if (!left.dueDate && !right.dueDate) result = 0;
        else return left.dueDate ? -1 : 1;
      } else result = nullableDate(left.dueDate) - nullableDate(right.dueDate);
    }
    else result = left.rank - right.rank;
    const directed = preferences.direction === 'asc' ? result : -result;
    return directed || left.rank - right.rank || left.id.localeCompare(right.id);
  }
}

function readPreferences(): ProjectListPreferences {
  try {
    const value = JSON.parse(localStorage.getItem(PREFERENCES_KEY) || '{}') as Partial<ProjectListPreferences>;
    const density: ListDensity = value.density === 'compact' ? 'compact' : 'comfortable';
    const sort: ListSortField = ['rank', 'title', 'status', 'priority', 'assignee', 'dueDate'].includes(value.sort ?? '') ? value.sort! : 'rank';
    return {
      density,
      sort,
      direction: value.direction === 'desc' ? 'desc' : 'asc',
      columns: { ...DEFAULT_LIST_PREFERENCES.columns, ...(value.columns ?? {}) }
    };
  } catch {
    return DEFAULT_LIST_PREFERENCES;
  }
}

function dateInputValue(value: string | null | undefined): string { return value ? value.slice(0, 10) : ''; }
function nullableDate(value: string | null | undefined): number { return value ? new Date(value).getTime() : Number.MAX_SAFE_INTEGER; }
