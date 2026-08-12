import { Component, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProjectViewDefinition, ProjectViewId } from './desktop-shell.models';
import { ZumboIconComponent } from './zumbo-icon.component';

@Component({
  selector: 'zumbo-project-view-tabs',
  imports: [RouterLink, ZumboIconComponent],
  templateUrl: './project-view-tabs.component.html',
  styleUrl: './project-view-tabs.component.scss'
})
export class ProjectViewTabsComponent {
  readonly projectId = input.required<string>();
  readonly activeView = input.required<ProjectViewId>();
  readonly views = input.required<readonly ProjectViewDefinition[]>();

  protected readonly groupLabels = new Map<ProjectViewDefinition['group'], string>([
    ['plan', 'Planlama'],
    ['operate', 'Operasyon'],
    ['insights', 'İçgörüler']
  ]);
  protected readonly secondaryGroups = ['plan', 'operate', 'insights'] as const;
  protected readonly openGroup = signal<ProjectViewDefinition['group'] | null>(null);

  protected primary(): readonly ProjectViewDefinition[] {
    return this.views().filter(view => view.group === 'primary');
  }

  protected secondary(): readonly ProjectViewDefinition[] {
    return this.views().filter(view => view.group !== 'primary');
  }

  protected viewsInGroup(group: ProjectViewDefinition['group']): readonly ProjectViewDefinition[] {
    return this.secondary().filter(view => view.group === group);
  }

  protected isGroupActive(group: ProjectViewDefinition['group']): boolean {
    return this.viewsInGroup(group).some(view => view.id === this.activeView());
  }

  protected toggleGroup(event: Event, group: ProjectViewDefinition['group']): void {
    const details = event.currentTarget as HTMLDetailsElement;
    if (details.open) this.openGroup.set(group);
    else if (this.openGroup() === group) this.openGroup.set(null);
  }
}
