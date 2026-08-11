import { DestroyRef, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ZumboApiClient, ZumboRealtimeService, ZumboSessionService } from '@zumbo/modern-shared';
import { catchError, finalize, forkJoin, map, of, switchMap } from 'rxjs';
import { DesktopNavigationComponent } from './shell/desktop-navigation.component';
import { CommandPaletteComponent } from './shell/command-palette.component';
import { AutomationPage } from './features/automation/automation.page';
import { CapacityPage } from './features/capacity/capacity.page';
import { HomePage } from './features/home/home.page';
import { GoalPage } from './features/goals/goal.page';
import { ProjectCatalogPage } from './features/catalog/project-catalog.page';
import { ProjectBoardPage } from './features/board/project-board.page';
import { InboxPage } from './features/inbox/inbox.page';
import { IntakePage } from './features/intake/intake.page';
import { JobsPage } from './features/jobs/jobs.page';
import { KnowledgePage } from './features/knowledge/knowledge.page';
import { ProjectListPage } from './features/list/project-list.page';
import { NotificationItem } from './features/notifications/notification.models';
import { NotificationPopoverComponent } from './features/notifications/notification-popover.component';
import { ProjectOverviewPage } from './features/overview/project-overview.page';
import { ProjectBacklogPage } from './features/planning/project-backlog.page';
import { ProjectSprintPage } from './features/planning/project-sprint.page';
import { ProjectPlanningViewPage } from './features/planning/views/project-planning-view.page';
import { MyWorkPage } from './features/personal-work/my-work.page';
import { PortfolioPage } from './features/portfolios/portfolio.page';
import { ProjectDirectoryPage } from './features/projects/project-directory.page';
import { ProjectReportingPage } from './features/reporting/project-reporting.page';
import { ProjectWorkItemDetail } from './features/work-items/project-work-item.models';
import { WorkItemCreateComponent } from './features/work-items/work-item-create.component';
import { WorkItemDetailComponent } from './features/work-items/work-item-detail.component';
import {
  BoardSummary,
  isProjectView,
  isWorkspaceSection,
  OrganizationSummary,
  PROJECT_VIEWS,
  ProjectSummary,
  ProjectViewId,
  WorkspaceSection
} from './shell/desktop-shell.models';
import { ProjectSwitcherComponent } from './shell/project-switcher.component';
import { ProjectViewTabsComponent } from './shell/project-view-tabs.component';
import { ZumboIconComponent } from './shell/zumbo-icon.component';

const PROJECT_KEY = 'zumbo.modern.projectId';
const RECENT_KEY = 'zumbo.modern.recentProjects';
const FAVORITES_KEY = 'zumbo.favoriteProjects';
const THEME_KEY = 'zumbo.theme';
const NAV_KEY = 'zumbo.navCollapsed';

@Component({
  selector: 'zumbo-desktop-workspace',
  imports: [AutomationPage, CapacityPage, CommandPaletteComponent, DesktopNavigationComponent, GoalPage, HomePage, InboxPage, IntakePage, JobsPage, KnowledgePage, MyWorkPage, NotificationPopoverComponent, PortfolioPage, ProjectBacklogPage, ProjectBoardPage, ProjectCatalogPage, ProjectDirectoryPage, ProjectListPage, ProjectOverviewPage, ProjectPlanningViewPage, ProjectReportingPage, ProjectSprintPage, ProjectSwitcherComponent, ProjectViewTabsComponent, RouterLink, WorkItemCreateComponent, WorkItemDetailComponent, ZumboIconComponent],
  templateUrl: './workspace.page.html',
  styleUrls: ['./workspace.page.scss', './workspace-responsive.scss']
})
export class DesktopWorkspacePage {
  private readonly api = inject(ZumboApiClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(ZumboRealtimeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private projectContextId: string | null = null;
  protected readonly session = inject(ZumboSessionService);

  protected readonly projects = signal<readonly ProjectSummary[]>([]);
  protected readonly boards = signal<readonly BoardSummary[]>([]);
  protected readonly projectContextLoading = signal(false);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly selectedProjectId = signal<string | null>(null);
  protected readonly selectedTaskId = signal<string | null>(null);
  protected readonly section = signal<WorkspaceSection | 'project'>('project');
  protected readonly activeView = signal<ProjectViewId>('overview');
  protected readonly organizationName = signal('Çalışma alanı');
  protected readonly unreadCount = signal(0);
  protected readonly navCollapsed = signal(localStorage.getItem(NAV_KEY) === 'true');
  protected readonly mobileNavOpen = signal(false);
  protected readonly theme = signal<'light' | 'dark'>(localStorage.getItem(THEME_KEY) === 'dark' ? 'dark' : 'light');
  protected readonly favorites = signal<readonly ProjectSummary[]>(readProjects(FAVORITES_KEY));
  protected readonly recentProjects = signal<readonly ProjectSummary[]>(readProjects(RECENT_KEY));

  protected readonly selectedProject = computed(() => this.projects().find(item => item.id === this.selectedProjectId()) ?? null);
  protected readonly availableViews = computed(() => PROJECT_VIEWS.filter(view => !view.requiresBoard || this.boards().length > 0));
  protected readonly pageTitle = computed(() => {
    if (this.section() === 'project') return PROJECT_VIEWS.find(view => view.id === this.activeView())?.label ?? 'Proje';
    return SECTION_LABELS[this.section() as WorkspaceSection];
  });
  protected readonly favorite = computed(() => this.favorites().some(project => project.id === this.selectedProjectId()));

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(() => this.applyRoute());
    this.restoreWorkspace();
  }

  protected selectProject(projectId: string): void {
    const project = this.projects().find(item => item.id === projectId);
    if (!project) return;
    this.rememberProject(project);
    const remembered = localStorage.getItem(`zumbo.projectView.${projectId}`);
    const view = isProjectView(remembered) ? remembered : 'overview';
    void this.router.navigate(['/workspace', projectId, view]);
  }

  protected toggleFavorite(): void {
    const project = this.selectedProject();
    if (!project) return;
    this.toggleProjectFavorite(project);
  }

  protected toggleProjectFavorite(project: ProjectSummary): void {
    const favorites = this.favorites().some(item => item.id === project.id)
      ? this.favorites().filter(item => item.id !== project.id)
      : [project, ...this.favorites().filter(item => item.id !== project.id)].slice(0, 12);
    this.favorites.set(favorites);
    localStorage.setItem(FAVORITES_KEY, JSON.stringify(favorites));
  }

  protected openProject(projectId: string): void {
    const project = this.projects().find(item => item.id === projectId);
    if (!project) return;
    this.rememberProject(project);
    void this.router.navigate(['/workspace', project.id, 'overview']);
  }

  protected updateProject(updated: ProjectSummary): void {
    this.projects.update(projects => projects.map(project => project.id === updated.id ? updated : project));
    this.favorites.update(projects => replaceProject(projects, updated));
    this.recentProjects.update(projects => replaceProject(projects, updated));
    localStorage.setItem(FAVORITES_KEY, JSON.stringify(this.favorites()));
    localStorage.setItem(RECENT_KEY, JSON.stringify(this.recentProjects()));
  }

  protected removeProject(projectId: string): void {
    this.projects.update(projects => projects.filter(project => project.id !== projectId));
    this.favorites.update(projects => projects.filter(project => project.id !== projectId));
    this.recentProjects.update(projects => projects.filter(project => project.id !== projectId));
    localStorage.setItem(FAVORITES_KEY, JSON.stringify(this.favorites()));
    localStorage.setItem(RECENT_KEY, JSON.stringify(this.recentProjects()));
    if (this.selectedProjectId() === projectId) {
      const fallbackId = this.projects()[0]?.id ?? null;
      this.selectedProjectId.set(fallbackId);
      fallbackId ? localStorage.setItem(PROJECT_KEY, fallbackId) : localStorage.removeItem(PROJECT_KEY);
    }
  }

  protected toggleTheme(): void {
    const theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(theme);
    localStorage.setItem(THEME_KEY, theme);
  }

  protected toggleNav(): void {
    const collapsed = !this.navCollapsed();
    this.navCollapsed.set(collapsed);
    localStorage.setItem(NAV_KEY, String(collapsed));
  }

  protected logout(): void {
    void this.realtime.stop().finally(() => {
      this.session.logout().subscribe(() => void this.router.navigate(['/login']));
    });
  }

  protected openCreatedTask(task: ProjectWorkItemDetail): void {
    void this.router.navigate(['/workspace', task.projectId, this.activeView(), 'task', task.id]);
  }

  protected openIntakeWorkItem(workItemId: string): void {
    const projectId = this.selectedProjectId();
    if (projectId) void this.router.navigate(['/workspace', projectId, 'intake', 'task', workItemId]);
  }

  protected openAutomationWorkItem(workItemId: string): void {
    const projectId = this.selectedProjectId();
    if (projectId) void this.router.navigate(['/workspace', projectId, 'automation', 'task', workItemId]);
  }

  protected closeTaskDetail(): void {
    const projectId = this.selectedProjectId();
    if (projectId) void this.router.navigate(['/workspace', projectId, this.activeView()]);
  }

  private restoreWorkspace(): void {
    this.session.restore().pipe(
      switchMap(auth => {
        if (!auth) {
          void this.router.navigate(['/login']);
          return of(null);
        }
        const organizationId = encodeURIComponent(auth.user.organizationId);
        return forkJoin({
          projects: this.api.get<readonly ProjectSummary[]>(`/api/projects?organizationId=${organizationId}`),
          organizations: this.api.get<readonly OrganizationSummary[]>('/api/organizations').pipe(catchError(() => of([]))),
          notifications: this.api.get<readonly NotificationItem[]>('/api/notifications?page=1&pageSize=50').pipe(catchError(() => of([])))
        }).pipe(map(data => ({ ...data, organizationId: auth.user.organizationId })));
      }),
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: data => {
        if (!data) return;
        this.projects.set(data.projects);
        this.organizationName.set(data.organizations.find(item => item.id === data.organizationId)?.name ?? 'Çalışma alanı');
        this.unreadCount.set(data.notifications.filter(item => !item.read).length);
        if (!this.applyLegacyLocation()) this.applyRoute(true);
      },
      error: () => this.error.set('Çalışma alanı yüklenemedi.')
    });
  }

  private applyRoute(selectDefault = false): void {
    if (!this.projects().length) return;
    const section = this.route.snapshot.paramMap.get('section');
    if (isWorkspaceSection(section)) {
      const storedProjectId = localStorage.getItem(PROJECT_KEY);
      const project = this.projects().find(item => item.id === storedProjectId) ?? this.projects()[0] ?? null;
      this.selectedProjectId.set(project?.id ?? null);
      this.section.set(section);
      this.selectedTaskId.set(null);
      this.mobileNavOpen.set(false);
      return;
    }

    const routeProjectId = this.route.snapshot.paramMap.get('projectId');
    const storedProjectId = localStorage.getItem(PROJECT_KEY);
    const project = this.projects().find(item => item.id === routeProjectId)
      ?? this.projects().find(item => item.id === storedProjectId)
      ?? this.projects()[0];
    if (!project) return;

    const routeView = this.route.snapshot.paramMap.get('view');
    const storedView = localStorage.getItem(`zumbo.projectView.${project.id}`);
    const view = isProjectView(routeView) ? routeView : isProjectView(storedView) ? storedView : 'overview';
    if (selectDefault && (!routeProjectId || !isProjectView(routeView))) {
      void this.router.navigate(['/workspace', project.id, view], { replaceUrl: true });
      return;
    }
    this.section.set('project');
    this.activeView.set(view);
    this.selectedProjectId.set(project.id);
    this.selectedTaskId.set(this.route.snapshot.paramMap.get('taskId'));
    this.rememberProject(project);
    if (this.projectContextId !== project.id) {
      this.loadProjectContext(project.id, view);
    } else if (PROJECT_VIEWS.some(candidate => candidate.id === view && candidate.requiresBoard) && !this.boards().length) {
      void this.router.navigate(['/workspace', project.id, 'overview'], { replaceUrl: true });
    }
    this.mobileNavOpen.set(false);
  }

  private loadProjectContext(projectId: string, requestedView: ProjectViewId): void {
    this.projectContextId = projectId;
    this.boards.set([]);
    this.projectContextLoading.set(true);
    this.api.get<readonly BoardSummary[]>(`/api/boards/by-project/${encodeURIComponent(projectId)}`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: boards => {
          this.boards.set(boards);
          this.projectContextLoading.set(false);
          const available = PROJECT_VIEWS.some(view => view.id === requestedView && (!view.requiresBoard || boards.length > 0));
          if (!available) void this.router.navigate(['/workspace', projectId, 'overview'], { replaceUrl: true });
        },
        error: () => {
          this.projectContextLoading.set(false);
          this.error.set('Proje bağlamı yüklenemedi.');
        }
      });
    void this.realtime.stop().then(() => this.realtime.connect(projectId)).catch(() => {
      this.error.set('Canlı güncellemeler şu anda kullanılamıyor.');
    });
  }

  private rememberProject(project: ProjectSummary): void {
    localStorage.setItem(PROJECT_KEY, project.id);
    localStorage.setItem(`zumbo.projectView.${project.id}`, this.activeView());
    const recent = [project, ...this.recentProjects().filter(item => item.id !== project.id)].slice(0, 8);
    this.recentProjects.set(recent);
    localStorage.setItem(RECENT_KEY, JSON.stringify(recent));
  }

  private applyLegacyLocation(): boolean {
    if (!location.hash || !location.hash.includes('=')) return false;
    const params = new URLSearchParams(location.hash.slice(1));
    const projectId = params.get('project');
    const view = params.get('view');
    const section = params.get('section');
    history.replaceState(null, '', location.pathname);
    if (projectId && this.projects().some(project => project.id === projectId)) {
      void this.router.navigate(['/workspace', projectId, isProjectView(view) ? view : 'overview'], { replaceUrl: true });
      return true;
    } else if (isWorkspaceSection(section)) {
      void this.router.navigate(['/workspace', 'section', section], { replaceUrl: true });
      return true;
    }
    return false;
  }
}

const SECTION_LABELS: Readonly<Record<WorkspaceSection, string>> = {
  home: 'Ana sayfa',
  mywork: 'İşlerim',
  inbox: 'Gelen kutusu',
  projects: 'Projeler',
  portfolios: 'Portföyler',
  goals: 'Hedefler',
  capacity: 'Kapasite',
  knowledge: 'Bilgi',
  teams: 'Ekipler',
  audit: 'Denetim',
  archive: 'Arşiv',
  settings: 'Ayarlar'
};

function readProjects(key: string): readonly ProjectSummary[] {
  try {
    const value = JSON.parse(localStorage.getItem(key) || '[]');
    return Array.isArray(value) ? value.filter(item => item && typeof item.id === 'string' && typeof item.name === 'string') : [];
  } catch {
    return [];
  }
}

function replaceProject(projects: readonly ProjectSummary[], updated: ProjectSummary): readonly ProjectSummary[] {
  return projects.map(project => project.id === updated.id ? updated : project);
}
