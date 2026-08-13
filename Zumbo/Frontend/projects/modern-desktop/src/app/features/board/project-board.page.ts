import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ZumboApiError, ZumboRealtimeService } from '@zumbo/modern-shared';
import { Observable, catchError, finalize, of, switchMap } from 'rxjs';
import { BoardColumnSummary, BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { BoardDropPlacement, BoardRole, BoardWorkItem, ProjectBoardData } from './project-board.models';
import { ProjectBoardService, compareRank } from './project-board.service';

interface DropTarget { readonly taskId: string; readonly placement: 'before' | 'after'; }

@Component({
  selector: 'zumbo-project-board-page',
  imports: [RouterLink, ZumboIconComponent],
  providers: [ProjectBoardService],
  templateUrl: './project-board.page.html',
  styleUrls: ['./project-board.page.scss', './project-board-cards.scss', './project-board-responsive.scss']
})
export class ProjectBoardPage {
  readonly project = input.required<ProjectSummary>();
  readonly boards = input.required<readonly BoardSummary[]>();
  readonly contextReady = input(false);
  readonly userId = input.required<string>();

  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(ZumboRealtimeService);
  private readonly router = inject(Router);
  private contextProjectId = '';
  private suppressCardOpen = false;
  protected readonly data = signal<ProjectBoardData | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly query = signal('');
  protected readonly priority = signal('');
  protected readonly density = signal<'comfortable' | 'compact'>(localStorage.getItem('zumbo.density') === 'compact' ? 'compact' : 'comfortable');
  protected readonly selectedBoardId = signal<string | null>(null);
  protected readonly pendingIds = signal<ReadonlySet<string>>(new Set());
  protected readonly draggingId = signal<string | null>(null);
  protected readonly dropTarget = signal<DropTarget | null>(null);
  protected readonly dropLaneId = signal<string | null>(null);

  protected readonly board = computed(() => this.boards().find(item => item.id === this.selectedBoardId()) ?? this.boards()[0] ?? null);
  protected readonly canMove = computed(() => this.hasPermission('WorkItemMove'));
  protected readonly canCreate = computed(() => this.hasPermission('WorkItemCreate'));
  protected readonly filteredTasks = computed(() => {
    const board = this.board();
    const query = this.query().trim().toLocaleLowerCase('tr-TR');
    if (!board) return [];
    return (this.data()?.tasks ?? []).filter(task => task.boardId === board.id
      && (!this.priority() || task.priority === this.priority())
      && (!query || `${task.title} ${task.type} ${task.labels.join(' ')}`.toLocaleLowerCase('tr-TR').includes(query)));
  });
  protected readonly columns = computed(() => (this.board()?.columns ?? []).slice().sort((left, right) => left.position - right.position).map(column => ({
    ...column,
    tasks: this.filteredTasks().filter(task => this.inColumn(task, column)).sort(compareRank)
  })));

  constructor(private readonly projectBoard: ProjectBoardService) {
    effect(() => {
      const projectId = this.project().id;
      if (!this.contextReady() || projectId === this.contextProjectId) return;
      this.contextProjectId = projectId;
      const stored = localStorage.getItem(`zumbo.board.${projectId}`);
      this.selectedBoardId.set(this.boards().some(board => board.id === stored) ? stored : this.boards()[0]?.id ?? null);
      this.load();
    });
    this.realtime.changes$.pipe(takeUntilDestroyed()).subscribe(change => {
      if (change.projectId !== this.project().id || this.pendingIds().has(change.workItemId)) return;
      this.load(false);
    });
    this.realtime.resync$.pipe(takeUntilDestroyed()).subscribe(() => this.load(false));
  }

  protected load(showLoading = true): void {
    if (showLoading) this.loading.set(true);
    this.error.set(null);
    this.projectBoard.load(this.project().id).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: data => {
        this.data.set(data);
        this.realtime.synchronize(data.tasks);
      },
      error: () => this.error.set('Pano verileri yüklenemedi.')
    });
  }

  protected selectBoard(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    if (!this.boards().some(board => board.id === id)) return;
    this.selectedBoardId.set(id);
    localStorage.setItem(`zumbo.board.${this.project().id}`, id);
  }

  protected setQuery(event: Event): void { this.query.set((event.target as HTMLInputElement).value); }
  protected setPriority(event: Event): void { this.priority.set((event.target as HTMLSelectElement).value); }
  protected setDensity(value: 'comfortable' | 'compact'): void { this.density.set(value); localStorage.setItem('zumbo.density', value); }

  protected dragStart(event: DragEvent, task: BoardWorkItem): void {
    if (!this.canMove() || this.pendingIds().has(task.id)) { event.preventDefault(); return; }
    this.suppressCardOpen = true;
    this.draggingId.set(task.id);
    event.dataTransfer?.setData('text/plain', task.id);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  protected dragOverCard(event: DragEvent, task: BoardWorkItem): void {
    if (!this.draggingId() || this.draggingId() === task.id) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.dropTarget.set({ taskId: task.id, placement: event.clientY >= rect.top + rect.height / 2 ? 'after' : 'before' });
    this.dropLaneId.set(task.columnId ?? null);
    this.autoscroll(event);
  }

  protected dragOverLane(event: DragEvent, column: BoardColumnSummary): void {
    if (!this.draggingId()) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    this.dropLaneId.set(column.id);
    this.autoscroll(event);
  }

  protected dropOnCard(event: DragEvent, anchor: BoardWorkItem): void {
    event.preventDefault();
    event.stopPropagation();
    const task = this.taskFromDrag(event);
    const placement = this.dropTarget()?.taskId === anchor.id ? this.dropTarget()!.placement : 'before';
    this.clearDrag();
    if (task) this.move(task, this.columnFor(anchor), anchor, placement);
  }

  protected dropOnLane(event: DragEvent, column: BoardColumnSummary): void {
    event.preventDefault();
    const task = this.taskFromDrag(event);
    this.clearDrag();
    if (task) this.move(task, column, null, 'end');
  }

  protected clearDrag(): void {
    this.draggingId.set(null);
    this.dropTarget.set(null);
    this.dropLaneId.set(null);
    setTimeout(() => { this.suppressCardOpen = false; });
  }

  protected openTask(event: MouseEvent, task: BoardWorkItem): void {
    const target = event.target as HTMLElement;
    if (this.suppressCardOpen || target.closest('a, button, input, select, textarea')) return;
    void this.router.navigate(['/workspace', this.project().id, 'board', 'task', task.id]);
  }

  protected finishDrag(): void {
    const task = this.data()?.tasks.find(item => item.id === this.draggingId()) ?? null;
    const target = this.dropTarget();
    const laneId = this.dropLaneId();
    const anchor = target ? this.data()?.tasks.find(item => item.id === target.taskId) ?? null : null;
    const column = anchor ? this.columnFor(anchor) : this.columns().find(item => item.id === laneId) ?? null;
    this.clearDrag();
    if (task && column) this.move(task, column, anchor, target?.placement ?? 'end');
  }

  protected handleTaskKey(event: KeyboardEvent, task: BoardWorkItem): void {
    if (!event.altKey || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') this.moveHorizontal(task, event.key === 'ArrowLeft' ? -1 : 1);
    else this.moveVertical(task, event.key === 'ArrowUp' ? -1 : 1);
  }

  protected moveHorizontal(task: BoardWorkItem, direction: number): void {
    const index = this.columns().findIndex(column => this.inColumn(task, column));
    const target = this.columns()[index + direction];
    if (target) this.move(task, target, null, 'end');
  }

  protected moveVertical(task: BoardWorkItem, direction: number): void {
    const column = this.columnFor(task);
    const tasks = this.columns().find(item => item.id === column?.id)?.tasks ?? [];
    const index = tasks.findIndex(item => item.id === task.id);
    const anchor = tasks[index + direction];
    if (column && anchor) this.move(task, column, anchor, direction < 0 ? 'before' : 'after');
  }

  protected canMoveHorizontal(task: BoardWorkItem, direction: number): boolean {
    const index = this.columns().findIndex(column => this.inColumn(task, column));
    const target = this.columns()[index + direction];
    return !!target && this.canTransition(task, target);
  }

  protected canMoveVertical(task: BoardWorkItem, direction: number): boolean {
    const column = this.columnFor(task);
    const tasks = this.columns().find(item => item.id === column?.id)?.tasks ?? [];
    const index = tasks.findIndex(item => item.id === task.id);
    return index + direction >= 0 && index + direction < tasks.length;
  }

  protected userName(id: string | null | undefined): string {
    if (!id) return 'Atanmamış';
    const user = this.data()?.users.find(item => item.id === id);
    return user?.username || user?.email || 'Proje üyesi';
  }

  protected formatDate(value: string | null | undefined): string {
    return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value)) : '';
  }

  protected priorityLabel(priority: string): string {
    return ({ Critical: 'Kritik', High: 'Yüksek', Medium: 'Orta', Low: 'Düşük' } as Readonly<Record<string, string>>)[priority] ?? priority;
  }

  protected atWipLimit(column: BoardColumnSummary & { readonly tasks: readonly BoardWorkItem[] }): boolean {
    return !!column.wipLimit && this.columnTaskCount(column) >= column.wipLimit;
  }

  protected columnTaskCount(column: BoardColumnSummary): number {
    const board = this.board();
    return board ? (this.data()?.tasks ?? []).filter(task => task.boardId === board.id && this.inColumn(task, column)).length : 0;
  }

  private move(task: BoardWorkItem, column: BoardColumnSummary | null, anchor: BoardWorkItem | null, placement: BoardDropPlacement): void {
    if (!column || !this.canMove() || this.pendingIds().has(task.id) || (anchor && anchor.id === task.id)) return;
    if (!this.canTransition(task, column)) { this.notice.set('Workflow bu durum geçişine izin vermiyor.'); return; }
    const statusChanged = !this.inColumn(task, column);
    const targetTasks = this.columns().find(item => item.id === column.id)?.tasks.filter(item => item.id !== task.id) ?? [];
    if (statusChanged && column.wipLimit && this.columnTaskCount(column) >= column.wipLimit) { this.notice.set('Kolonun WIP limiti dolu; kart taşınmadı.'); return; }
    const resolvedAnchor = anchor ?? targetTasks.at(-1) ?? null;
    const resolvedPlacement = anchor ? placement : resolvedAnchor ? 'after' : 'end';
    if (!statusChanged && resolvedAnchor?.id === task.id) return;
    const snapshot = this.data();
    if (!snapshot) return;
    const targetStatus = statusChanged ? column.name : task.status;
    const optimistic = { ...task, status: targetStatus, columnId: column.id, rank: optimisticRank(targetTasks, resolvedAnchor, resolvedPlacement) };
    this.data.set({ ...snapshot, tasks: snapshot.tasks.map(item => item.id === task.id ? optimistic : item) });
    this.pendingIds.update(ids => new Set(ids).add(task.id));
    this.notice.set(null);

    let statusMoved = false;
    const statusRequest = statusChanged ? this.projectBoard.changeStatus(task.id, targetStatus) : of(task);
    statusRequest.pipe(
      switchMap(moved => {
        if (statusChanged) statusMoved = true;
        this.realtime.remember(moved);
        if (!resolvedAnchor) return of(moved);
        return this.projectBoard.changeRank(task.id,
          resolvedPlacement === 'before' ? resolvedAnchor.id : null,
          resolvedPlacement === 'after' ? resolvedAnchor.id : null);
      }),
      catchError(error => this.rollbackMove(task, snapshot, statusMoved, error)),
      finalize(() => this.pendingIds.update(ids => { const next = new Set(ids); next.delete(task.id); return next; })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(authoritative => {
      if (!authoritative) return;
      this.realtime.remember(authoritative);
      this.data.update(data => data ? { ...data, tasks: data.tasks.map(item => item.id === task.id ? authoritative : item) } : data);
      this.notice.set('Kart konumu kaydedildi.');
    });
  }

  private rollbackMove(task: BoardWorkItem, snapshot: ProjectBoardData, statusMoved: boolean, error: unknown): Observable<null> {
    this.data.set(snapshot);
    const normalized = error as ZumboApiError;
    this.notice.set(movementError(normalized.code));
    const compensate = statusMoved ? this.projectBoard.changeStatus(task.id, task.status).pipe(catchError(() => of(null))) : of(null);
    if (['CONCURRENCY_CONFLICT', 'WORK_ITEM_RANK_EXHAUSTED', 'RESOURCE_BUSY'].includes(normalized.code ?? '')) {
      return compensate.pipe(switchMap(() => { this.load(false); return of(null); }));
    }
    return compensate.pipe(switchMap(() => of(null)));
  }

  private canTransition(task: BoardWorkItem, column: BoardColumnSummary): boolean {
    if (this.inColumn(task, column)) return true;
    return this.data()?.workflow.transitions.some(item => item.fromStatus === task.status && item.toStatus === column.name) ?? false;
  }

  private inColumn(task: BoardWorkItem, column: BoardColumnSummary): boolean {
    return task.columnId === column.id || column.statusNames?.includes(task.status) === true || (!column.statusNames?.length && task.status === column.name);
  }

  private columnFor(task: BoardWorkItem): BoardColumnSummary | null {
    return this.columns().find(column => this.inColumn(task, column)) ?? null;
  }

  private hasPermission(permission: string): boolean {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role: BoardRole | undefined = this.data()?.roles.find(item => item.name === membership?.role && item.isActive);
    return !!role?.permissions.some(value => value === '*' || value === permission);
  }

  private taskFromDrag(event: DragEvent): BoardWorkItem | null {
    const id = event.dataTransfer?.getData('text/plain') || this.draggingId();
    return this.data()?.tasks.find(item => item.id === id) ?? null;
  }

  private autoscroll(event: DragEvent): void {
    const viewport = (event.currentTarget as HTMLElement).closest<HTMLElement>('.board-scroll');
    if (viewport) {
      const rect = viewport.getBoundingClientRect();
      if (event.clientX < rect.left + 56) viewport.scrollBy({ left: -18 });
      else if (event.clientX > rect.right - 56) viewport.scrollBy({ left: 18 });
    }
    if (event.clientY < 80) window.scrollBy({ top: -16 });
    else if (event.clientY > window.innerHeight - 60) window.scrollBy({ top: 16 });
  }
}

function optimisticRank(tasks: readonly BoardWorkItem[], anchor: BoardWorkItem | null, placement: BoardDropPlacement): number {
  if (!anchor) return 1_000_000;
  const index = tasks.findIndex(item => item.id === anchor.id);
  const previous = placement === 'before' ? tasks[index - 1] : anchor;
  const next = placement === 'before' ? anchor : tasks[index + 1];
  if (!previous) return anchor.rank - 1;
  if (!next) return anchor.rank + 1;
  return previous.rank + (next.rank - previous.rank) / 2;
}

function movementError(code: string | null | undefined): string {
  if (code === 'BOARD_WIP_LIMIT_EXCEEDED' || code === 'WIP_LIMIT_EXCEEDED') return 'Kolonun WIP limiti dolu; kart eski konumuna alındı.';
  if (code === 'WORKFLOW_TRANSITION_FORBIDDEN') return 'Workflow bu durum geçişine izin vermedi; kart geri alındı.';
  if (code === 'CONCURRENCY_CONFLICT') return 'Kart başka bir kullanıcı tarafından değiştirildi; güncel pano yükleniyor.';
  return 'Kart taşınamadı; önceki konum geri yüklendi.';
}
