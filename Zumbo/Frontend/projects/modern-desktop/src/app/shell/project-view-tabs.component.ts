import { Component, input } from '@angular/core';
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

  protected primary(): readonly ProjectViewDefinition[] {
    return this.views().filter(view => view.group === 'primary');
  }

  protected secondary(): readonly ProjectViewDefinition[] {
    return this.views().filter(view => view.group !== 'primary');
  }
}
