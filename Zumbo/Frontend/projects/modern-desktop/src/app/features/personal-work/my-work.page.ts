import { Component, OnInit, computed, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { PersonalMode, PersonalSort, PersonalWorkItem, SavedPersonalView } from './personal-work.models';
import { PersonalWorkService, compareDueDates, isBlocked, isOpen, isOverdue } from './personal-work.service';

const MODE_KEY = 'zumbo.personal.mode';
const SORT_KEY = 'zumbo.personal.sort';
const VIEWS_KEY = 'zumbo.personalViews';
const MODES: readonly PersonalMode[] = ['assigned', 'due', 'blocked', 'recent'];
const SORTS: readonly PersonalSort[] = ['urgency', 'project', 'recent'];

@Component({
  selector: 'zumbo-my-work-page',
  imports: [ReactiveFormsModule, RouterLink, ZumboIconComponent],
  providers: [PersonalWorkService],
  templateUrl: './my-work.page.html',
  styleUrl: './my-work.page.scss'
})
export class MyWorkPage implements OnInit {
  readonly projects = input.required<readonly ProjectSummary[]>();
  readonly userId = input.required<string>();

  protected readonly tasks = signal<readonly PersonalWorkItem[]>([]);
  protected readonly mode = signal<PersonalMode>(readChoice(MODE_KEY, MODES, 'assigned'));
  protected readonly sort = signal<PersonalSort>(readChoice(SORT_KEY, SORTS, 'urgency'));
  protected readonly savedViews = signal<readonly SavedPersonalView[]>(readViews());
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly partial = signal(false);
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly freshAt = signal<Date | null>(null);
  protected readonly viewName = new FormControl('', { nonNullable: true });
  protected readonly visibleTasks = computed(() => sortTasks(filterTasks(this.tasks(), this.mode()), this.sort()));

  constructor(private readonly personalWork: PersonalWorkService) {}

  ngOnInit(): void {
    this.load();
  }

  protected load(page = 1, append = false): void {
    append ? this.loadingMore.set(true) : this.loading.set(true);
    this.error.set(null);
    this.personalWork.load(this.projects(), this.userId(), page).pipe(finalize(() => {
      this.loading.set(false);
      this.loadingMore.set(false);
    })).subscribe({
      next: result => {
        this.tasks.update(current => append ? mergeTasks(current, result.tasks) : result.tasks);
        this.partial.set(result.partial);
        this.hasMore.set(result.hasMore);
        this.page.set(result.page);
        this.freshAt.set(new Date());
      },
      error: () => this.error.set('İşleriniz yüklenemedi.')
    });
  }

  protected setMode(mode: PersonalMode): void {
    this.mode.set(mode);
    localStorage.setItem(MODE_KEY, mode);
  }

  protected setSort(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (!SORTS.includes(value as PersonalSort)) return;
    this.sort.set(value as PersonalSort);
    localStorage.setItem(SORT_KEY, value);
  }

  protected handleTabKey(event: KeyboardEvent, index: number): void {
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? MODES.length - 1
      : event.key === 'ArrowRight' ? (index + 1) % MODES.length
        : event.key === 'ArrowLeft' ? (index - 1 + MODES.length) % MODES.length : -1;
    if (next < 0) return;
    event.preventDefault();
    this.setMode(MODES[next]);
    setTimeout(() => document.querySelector<HTMLButtonElement>('.my-work-tabs [aria-selected="true"]')?.focus());
  }

  protected saveView(): void {
    const name = this.viewName.value.trim();
    if (!name) return;
    const views = [{ id: String(Date.now()), name, mode: this.mode() }, ...this.savedViews().filter(view => view.name !== name)].slice(0, 8);
    this.savedViews.set(views);
    localStorage.setItem(VIEWS_KEY, JSON.stringify(views));
    this.viewName.reset();
  }

  protected taskRoute(task: PersonalWorkItem): readonly string[] {
    return ['/workspace', task.projectId, 'board', 'task', task.id];
  }

  protected blocked(task: PersonalWorkItem): boolean { return isBlocked(task); }
  protected overdue(task: PersonalWorkItem): boolean { return isOverdue(task); }
  protected formatDate(value: string | null | undefined): string {
    return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value)) : '';
  }
}

function filterTasks(tasks: readonly PersonalWorkItem[], mode: PersonalMode): readonly PersonalWorkItem[] {
  if (mode === 'recent') return [...tasks].sort((left, right) => right.personalActivityAt.localeCompare(left.personalActivityAt));
  const assigned = tasks.filter(isOpen);
  if (mode === 'due') return assigned.filter(task => !!task.dueDate).sort(compareDueDates);
  if (mode === 'blocked') return assigned.filter(isBlocked);
  return assigned;
}

function sortTasks(tasks: readonly PersonalWorkItem[], sort: PersonalSort): readonly PersonalWorkItem[] {
  if (sort === 'project') return [...tasks].sort((left, right) => left.projectName.localeCompare(right.projectName, 'tr'));
  if (sort === 'recent') return [...tasks].sort((left, right) => right.personalActivityAt.localeCompare(left.personalActivityAt));
  return [...tasks].sort((left, right) => Number(isBlocked(right)) - Number(isBlocked(left)) || compareDueDates(left, right));
}

function mergeTasks(current: readonly PersonalWorkItem[], next: readonly PersonalWorkItem[]): readonly PersonalWorkItem[] {
  return [...current, ...next.filter(task => !current.some(existing => existing.id === task.id))];
}

function readChoice<T extends string>(key: string, choices: readonly T[], fallback: T): T {
  const value = localStorage.getItem(key);
  return choices.includes(value as T) ? value as T : fallback;
}

function readViews(): readonly SavedPersonalView[] {
  try {
    const value = JSON.parse(localStorage.getItem(VIEWS_KEY) || '[]');
    return Array.isArray(value) ? value.filter(view => view && typeof view.id === 'string' && typeof view.name === 'string' && MODES.includes(view.mode)) : [];
  } catch {
    return [];
  }
}
