import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';
import { MobileWorkMode, dueTime, isBlocked, isOpen, priorityLabel } from '../../shell/mobile-workspace.models';

const MODES: readonly MobileWorkMode[] = ['assigned', 'due', 'blocked', 'recent'];
@Component({ selector: 'zumbo-mobile-my-work', imports: [RouterLink, IonContent, IonHeader, IonRefresher, IonRefresherContent, IonTitle, IonToolbar], templateUrl: './mobile-my-work.page.html', styleUrls: ['./mobile-my-work.page.scss', './mobile-daily-work.shared.scss'] })
export class MobileMyWorkPage {
  protected readonly store = inject(MobileWorkspaceStore);
  private readonly route = inject(ActivatedRoute);
  protected readonly mode = signal<MobileWorkMode>(readMode(this.route.snapshot.queryParamMap.get('mode')));
  protected readonly priorityLabel = priorityLabel;
  protected readonly projectId = this.route.snapshot.queryParamMap.get('project');
  protected readonly visible = computed(() => {
    const scoped = this.projectId ? this.store.tasks().filter(item => item.projectId === this.projectId) : this.store.tasks();
    const open = scoped.filter(isOpen);
    const filtered = this.mode() === 'due' ? open.filter(item => item.dueDate) : this.mode() === 'blocked' ? open.filter(isBlocked) : this.mode() === 'recent' ? scoped : open;
    return [...filtered].sort((a, b) => this.mode() === 'recent' ? b.id.localeCompare(a.id) : dueTime(a) - dueTime(b));
  });
  protected setMode(value: MobileWorkMode): void { this.mode.set(value); localStorage.setItem('zumbo.mobile.workMode', value); }
  protected async refresh(event: Event): Promise<void> { try { await this.store.load(true); } finally { await (event.target as unknown as { complete(): Promise<void> }).complete(); } }
  protected date(value?: string | null): string { return value ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value)) : ''; }
}
function readMode(query: string | null): MobileWorkMode { const stored = query ?? localStorage.getItem('zumbo.mobile.workMode'); return MODES.includes(stored as MobileWorkMode) ? stored as MobileWorkMode : 'assigned'; }
