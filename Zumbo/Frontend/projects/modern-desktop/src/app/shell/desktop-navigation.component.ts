import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconName, ProjectSummary, WorkspaceSection } from './desktop-shell.models';
import { ZumboIconComponent } from './zumbo-icon.component';

interface NavigationItem {
  readonly section: WorkspaceSection | 'project';
  readonly label: string;
  readonly icon: IconName;
}

interface NavigationGroup {
  readonly label: string;
  readonly items: readonly NavigationItem[];
}

@Component({
  selector: 'zumbo-desktop-navigation',
  imports: [RouterLink, ZumboIconComponent],
  templateUrl: './desktop-navigation.component.html',
  styleUrl: './desktop-navigation.component.scss'
})
export class DesktopNavigationComponent {
  readonly activeSection = input.required<string>();
  readonly currentProject = input<ProjectSummary | null>(null);
  readonly unreadCount = input(0);
  readonly showAudit = input(false);
  readonly favorites = input<readonly ProjectSummary[]>([]);
  readonly recentProjects = input<readonly ProjectSummary[]>([]);

  protected readonly groups: readonly NavigationGroup[] = [
    { label: 'Çalışma', items: [
      { section: 'home', label: 'Ana sayfa', icon: 'home' },
      { section: 'mywork', label: 'İşlerim', icon: 'list' },
      { section: 'inbox', label: 'Gelen kutusu', icon: 'inbox' },
      { section: 'project', label: 'Proje', icon: 'kanban' },
      { section: 'projects', label: 'Projeler', icon: 'folder' }
    ] },
    { label: 'Planlama', items: [
      { section: 'portfolios', label: 'Portföyler', icon: 'milestone' },
      { section: 'goals', label: 'Hedefler', icon: 'target' },
      { section: 'capacity', label: 'Kapasite', icon: 'gauge' },
      { section: 'knowledge', label: 'Bilgi', icon: 'book' }
    ] },
    { label: 'Yönetim', items: [
      { section: 'teams', label: 'Ekipler', icon: 'users' },
      { section: 'audit', label: 'Denetim', icon: 'briefcase' },
      { section: 'archive', label: 'Arşiv', icon: 'archive' },
      { section: 'settings', label: 'Ayarlar', icon: 'settings' }
    ] }
  ];

  protected routeFor(item: NavigationItem): readonly string[] {
    if (item.section === 'project' && this.currentProject()) {
      return ['/workspace', this.currentProject()!.id, 'overview'];
    }
    return ['/workspace', 'section', item.section === 'project' ? 'projects' : item.section];
  }

  protected isActive(item: NavigationItem): boolean {
    return item.section === 'project'
      ? this.activeSection() === 'project'
      : this.activeSection() === item.section;
  }
}
