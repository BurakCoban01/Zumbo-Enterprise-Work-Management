import { Component, input, output } from '@angular/core';
import { ProjectSummary } from './desktop-shell.models';

@Component({
  selector: 'zumbo-project-switcher',
  templateUrl: './project-switcher.component.html',
  styleUrl: './project-switcher.component.scss'
})
export class ProjectSwitcherComponent {
  readonly organizationName = input.required<string>();
  readonly projects = input.required<readonly ProjectSummary[]>();
  readonly selectedProjectId = input<string | null>(null);
  readonly projectChange = output<string>();

  protected select(event: Event): void {
    const projectId = (event.target as HTMLSelectElement).value;
    if (projectId) this.projectChange.emit(projectId);
  }
}
