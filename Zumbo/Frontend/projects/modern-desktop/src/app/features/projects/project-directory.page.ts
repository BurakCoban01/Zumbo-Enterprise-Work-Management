import { Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, finalize, of } from 'rxjs';
import { BoardSummary, ProjectMemberSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { ProjectDirectoryMode, ProjectDirectorySort, ProjectRoleSummary } from './project-directory.models';
import { ProjectDirectoryService } from './project-directory.service';

const MODE_KEY = 'zumbo.projects.mode';
const SORT_KEY = 'zumbo.projects.sort';
const MODES: readonly ProjectDirectoryMode[] = ['mine', 'favorites', 'recent', 'all'];
const SORTS: readonly ProjectDirectorySort[] = ['name', 'key', 'recent'];
const PAGE_SIZE = 12;

@Component({
  selector: 'zumbo-project-directory-page',
  imports: [ReactiveFormsModule, RouterLink, ZumboIconComponent],
  providers: [ProjectDirectoryService],
  templateUrl: './project-directory.page.html',
  styleUrls: ['./project-directory.page.scss', './project-directory-detail.scss']
})
export class ProjectDirectoryPage implements OnInit {
  readonly projects = input.required<readonly ProjectSummary[]>();
  readonly userId = input.required<string>();
  readonly selectedProjectId = input<string | null>(null);
  readonly favorites = input<readonly ProjectSummary[]>([]);
  readonly recentProjects = input<readonly ProjectSummary[]>([]);
  readonly favoriteToggle = output<ProjectSummary>();
  readonly projectOpen = output<string>();
  readonly projectUpdated = output<ProjectSummary>();
  readonly projectArchived = output<string>();

  private readonly destroyRef = inject(DestroyRef);
  protected readonly mode = signal<ProjectDirectoryMode>(readChoice(MODE_KEY, MODES, 'mine'));
  protected readonly sort = signal<ProjectDirectorySort>(readChoice(SORT_KEY, SORTS, 'name'));
  protected readonly query = signal('');
  protected readonly page = signal(1);
  protected readonly inspectedId = signal<string | null>(null);
  protected readonly roles = signal<readonly ProjectRoleSummary[]>([]);
  protected readonly boards = signal<readonly BoardSummary[]>([]);
  protected readonly loadingBoards = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly archiveConfirm = signal(false);
  protected readonly visibility = signal('Internal');
  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly nameControl = new FormControl('', { nonNullable: true });

  protected readonly filteredProjects = computed(() => {
    const query = this.query().trim().toLocaleLowerCase('tr-TR');
    const recentOrder = new Map(this.recentProjects().map((project, index) => [project.id, index]));
    const favorites = new Set(this.favorites().map(project => project.id));
    return this.projects().filter(project => {
      if (this.mode() === 'mine' && !this.membership(project)) return false;
      if (this.mode() === 'favorites' && !favorites.has(project.id)) return false;
      if (this.mode() === 'recent' && !recentOrder.has(project.id)) return false;
      return !query || `${project.key} ${project.name}`.toLocaleLowerCase('tr-TR').includes(query);
    }).sort((left, right) => {
      if (this.sort() === 'key') return left.key.localeCompare(right.key, 'tr');
      if (this.sort() === 'recent') return (recentOrder.get(left.id) ?? Number.MAX_SAFE_INTEGER) - (recentOrder.get(right.id) ?? Number.MAX_SAFE_INTEGER);
      return left.name.localeCompare(right.name, 'tr');
    });
  });
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filteredProjects().length / PAGE_SIZE)));
  protected readonly pageItems = computed(() => this.filteredProjects().slice((this.page() - 1) * PAGE_SIZE, this.page() * PAGE_SIZE));
  protected readonly inspectedProject = computed(() => this.projects().find(project => project.id === this.inspectedId()) ?? null);
  protected readonly inspectedMembership = computed(() => this.membership(this.inspectedProject()));
  protected readonly inspectedRole = computed(() => this.role(this.inspectedMembership()?.role));
  protected readonly canManage = computed(() => this.roleHasPermission(this.inspectedRole(), 'BoardManage'));
  protected readonly canArchive = computed(() => !!this.inspectedRole()?.isProtected);

  constructor(private readonly directory: ProjectDirectoryService) {
    this.searchControl.valueChanges.pipe(takeUntilDestroyed()).subscribe(value => {
      this.query.set(value);
      this.page.set(1);
    });
  }

  ngOnInit(): void {
    this.directory.loadRoles().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: roles => this.roles.set(roles),
      error: () => this.error.set('Proje rolleri yüklenemedi.')
    });
    const initial = this.projects().find(project => project.id === this.selectedProjectId()) ?? this.projects()[0] ?? null;
    if (initial) this.inspect(initial);
  }

  protected setMode(mode: ProjectDirectoryMode): void {
    this.mode.set(mode);
    this.page.set(1);
    localStorage.setItem(MODE_KEY, mode);
  }

  protected handleTabKey(event: KeyboardEvent, index: number): void {
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? MODES.length - 1
      : event.key === 'ArrowRight' ? (index + 1) % MODES.length
        : event.key === 'ArrowLeft' ? (index - 1 + MODES.length) % MODES.length : -1;
    if (next < 0) return;
    event.preventDefault();
    this.setMode(MODES[next]);
    setTimeout(() => document.querySelector<HTMLButtonElement>('.project-tabs [aria-selected="true"]')?.focus());
  }

  protected setSort(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as ProjectDirectorySort;
    if (!SORTS.includes(value)) return;
    this.sort.set(value);
    this.page.set(1);
    localStorage.setItem(SORT_KEY, value);
  }

  protected setVisibility(event: Event): void {
    this.visibility.set((event.target as HTMLSelectElement).value);
  }

  protected changePage(delta: number): void {
    this.page.set(Math.max(1, Math.min(this.pageCount(), this.page() + delta)));
  }

  protected toggleFavorite(project: ProjectSummary): void {
    this.page.set(1);
    this.favoriteToggle.emit(project);
  }

  protected inspect(project: ProjectSummary): void {
    this.inspectedId.set(project.id);
    this.nameControl.setValue(project.name);
    this.visibility.set(project.visibility ?? 'Internal');
    this.archiveConfirm.set(false);
    this.notice.set(null);
    this.loadBoards(project);
  }

  protected save(): void {
    const project = this.inspectedProject();
    const name = this.nameControl.value.trim();
    if (!project || !this.canManage() || !name || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.directory.update(project.id, { name, visibility: this.visibility() }).pipe(
      finalize(() => this.saving.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: updated => {
        this.projectUpdated.emit(updated);
        this.notice.set('Proje kaydedildi.');
      },
      error: () => this.error.set('Proje kaydedilemedi.')
    });
  }

  protected archive(): void {
    const project = this.inspectedProject();
    if (!project || !this.canArchive() || !this.archiveConfirm() || this.saving()) return;
    this.saving.set(true);
    this.directory.archive(project.id).pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        const fallback = this.projects().find(item => item.id !== project.id) ?? null;
        this.projectArchived.emit(project.id);
        fallback ? this.inspect(fallback) : this.inspectedId.set(null);
      },
      error: () => this.error.set('Proje arşivlenemedi.')
    });
  }

  protected membership(project: ProjectSummary | null): ProjectMemberSummary | null {
    return project?.members?.find(member => member.userId === this.userId()) ?? null;
  }

  protected roleLabel(project: ProjectSummary): string {
    const membership = this.membership(project);
    return membership ? this.role(membership.role)?.displayName ?? membership.role : 'Üye değil';
  }

  protected visibilityLabel(value: string | undefined): string {
    return value === 'Private' ? 'Özel' : value === 'Internal' ? 'Kuruluş içi' : 'Görünürlük belirtilmedi';
  }

  protected isFavorite(project: ProjectSummary): boolean {
    return this.favorites().some(item => item.id === project.id);
  }

  private loadBoards(project: ProjectSummary): void {
    if (!this.membership(project)) {
      this.boards.set([]);
      return;
    }
    this.loadingBoards.set(true);
    this.directory.loadBoards(project.id).pipe(
      catchError(() => {
        this.error.set('Proje panoları yüklenemedi.');
        return of([]);
      }),
      finalize(() => this.loadingBoards.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(boards => this.boards.set(boards));
  }

  private role(name: string | undefined): ProjectRoleSummary | null {
    return this.roles().find(role => role.name === name && role.isActive) ?? null;
  }

  private roleHasPermission(role: ProjectRoleSummary | null, permission: string): boolean {
    return !!role && role.permissions.some(value => value === '*' || value === permission);
  }
}

function readChoice<T extends string>(key: string, choices: readonly T[], fallback: T): T {
  const value = localStorage.getItem(key);
  return choices.includes(value as T) ? value as T : fallback;
}
