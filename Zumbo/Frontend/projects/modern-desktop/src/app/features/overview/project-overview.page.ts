import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { BoardSummary, ProjectMemberSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { OverviewRole, ProjectAuditEntry, ProjectOverviewData, ProjectRiskItem } from './project-overview.models';
import { ProjectOverviewService } from './project-overview.service';

@Component({
  selector: 'zumbo-project-overview-page',
  imports: [RouterLink, ZumboIconComponent],
  providers: [ProjectOverviewService],
  templateUrl: './project-overview.page.html',
  styleUrls: ['./project-overview.page.scss', './project-overview-detail.scss', './project-overview-responsive.scss']
})
export class ProjectOverviewPage {
  readonly project = input.required<ProjectSummary>();
  readonly boards = input<readonly BoardSummary[]>([]);
  readonly contextReady = input(false);
  readonly userId = input.required<string>();

  private readonly destroyRef = inject(DestroyRef);
  private contextKey = '';
  protected readonly data = signal<ProjectOverviewData | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly activeSprint = computed(() => this.data()?.sprints.find(sprint => sprint.status === 'Active') ?? null);
  protected readonly nextMilestone = computed(() => nextDated(this.project().milestones ?? [], 'dueAt', item => item.status !== 'Completed'));
  protected readonly nextRelease = computed(() => nextDated(this.project().releases ?? [], 'scheduledAt', item => item.status !== 'Published'));
  protected readonly owner = computed(() => this.project().members?.find(member => this.role(member.role)?.isProtected) ?? null);
  protected readonly contributors = computed(() => (this.project().members ?? []).filter(member => member !== this.owner()).slice(0, 4));
  protected readonly projectTeams = computed(() => {
    const ids = new Set(this.project().teamIds ?? []);
    return this.data()?.teams.filter(team => ids.has(team.id)) ?? [];
  });
  protected readonly canManage = computed(() => {
    const membership = this.project().members?.find(member => member.userId === this.userId());
    const role = this.role(membership?.role);
    return !!role?.permissions.some(permission => permission === '*' || permission === 'BoardManage');
  });
  protected readonly activity = computed(() => this.data()?.activity.slice(0, 6) ?? []);

  constructor(private readonly overview: ProjectOverviewService) {
    effect(() => {
      const project = this.project();
      const boardId = this.boards()[0]?.id ?? null;
      if (!this.contextReady()) return;
      const key = `${project.id}:${boardId ?? ''}`;
      if (key === this.contextKey) return;
      this.contextKey = key;
      this.load(project, boardId);
    });
  }

  protected load(project = this.project(), boardId = this.boards()[0]?.id ?? null): void {
    this.loading.set(true);
    this.error.set(null);
    this.overview.load(project.id, project.organizationId, boardId).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: data => this.data.set(data),
      error: () => this.error.set('Proje genel bakışı yüklenemedi.')
    });
  }

  protected healthTone(): 'danger' | 'warning' | 'success' | 'neutral' {
    const data = this.data();
    if (!data) return 'neutral';
    if (data.summary.overdue > 0) return 'danger';
    if (data.risks.length > 0) return 'warning';
    return this.activeSprint() ? 'success' : 'neutral';
  }

  protected healthLabel(): string {
    const tone = this.healthTone();
    return tone === 'danger' ? 'Takip gerekli' : tone === 'warning' ? 'Risk var' : tone === 'success' ? 'Yolunda' : 'Planlanıyor';
  }

  protected memberName(member: ProjectMemberSummary | null): string {
    if (!member) return 'Atanmamış';
    return this.userName(member.userId);
  }

  protected userName(userId: string | null | undefined): string {
    if (!userId) return 'Atanmamış';
    const user = this.data()?.users.find(item => item.id === userId);
    return user?.username || user?.email || 'Proje üyesi';
  }

  protected roleLabel(member: ProjectMemberSummary): string {
    return this.role(member.role)?.displayName ?? member.role;
  }

  protected activityLabel(entry: ProjectAuditEntry): string {
    const labels: Readonly<Record<string, string>> = {
      ProjectCreated: 'Projeyi oluşturdu', ProjectUpdated: 'Proje bilgilerini güncelledi',
      MemberAdded: 'Projeye üye ekledi', MemberRemoved: 'Projeden üye çıkardı',
      BoardCreated: 'Pano oluşturdu', BoardUpdated: 'Panoyu güncelledi',
      WorkItemCreated: 'İş öğesi oluşturdu', WorkItemUpdated: 'İş öğesini güncelledi'
    };
    return labels[entry.action] ?? 'Proje üzerinde çalıştı';
  }

  protected formatDate(value: string | null | undefined, includeTime = false): string {
    if (!value) return 'Tarih yok';
    return new Intl.DateTimeFormat('tr-TR', includeTime
      ? { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }
      : { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(value));
  }

  protected riskRoute(risk: ProjectRiskItem): readonly string[] {
    return ['/workspace', this.project().id, 'board', 'task', risk.id];
  }

  private role(name: string | undefined): OverviewRole | null {
    return this.data()?.roles.find(role => role.name === name && role.isActive) ?? null;
  }
}

function nextDated<T>(items: readonly T[], key: keyof T, include: (item: T) => boolean): T | null {
  return items.filter(item => include(item) && typeof item[key] === 'string')
    .sort((left, right) => String(left[key]).localeCompare(String(right[key])))[0] ?? null;
}
