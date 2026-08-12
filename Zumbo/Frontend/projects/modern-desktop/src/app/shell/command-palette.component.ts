import { Component, HostListener, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PROJECT_VIEWS, ProjectSummary, ProjectViewDefinition, WorkspaceSection } from './desktop-shell.models';
import { ZumboIconComponent } from './zumbo-icon.component';

interface CommandItem {
  readonly id: string;
  readonly label: string;
  readonly group: string;
  readonly route: readonly string[];
}

const SECTION_COMMANDS: readonly { id: WorkspaceSection; label: string }[] = [
  { id: 'home', label: 'Ana sayfayı aç' },
  { id: 'mywork', label: 'İşlerimi aç' },
  { id: 'inbox', label: 'Gelen kutusunu aç' },
  { id: 'projects', label: 'Projeleri aç' },
  { id: 'portfolios', label: 'Portföyleri aç' },
  { id: 'goals', label: 'Hedefleri aç' },
  { id: 'capacity', label: 'Kapasite planlarını aç' },
  { id: 'knowledge', label: 'Bilgi alanını aç' },
  { id: 'teams', label: 'Ekipleri aç' },
  { id: 'audit', label: 'Denetim merkezini aç' },
  { id: 'archive', label: 'Arşivi aç' },
  { id: 'settings', label: 'Ayarları aç' }
];

@Component({
  selector: 'zumbo-command-palette',
  imports: [RouterLink, ZumboIconComponent],
  templateUrl: './command-palette.component.html',
  styleUrl: './command-palette.component.scss'
})
export class CommandPaletteComponent {
  readonly project = input<ProjectSummary | null>(null);
  readonly availableViews = input<readonly ProjectViewDefinition[]>(PROJECT_VIEWS);
  readonly showAudit = input(false);
  protected readonly opened = signal(false);
  protected readonly query = signal('');
  protected readonly commands = computed<readonly CommandItem[]>(() => {
    const sections: readonly CommandItem[] = SECTION_COMMANDS
      .filter(command => command.id !== 'audit' || this.showAudit())
      .map(command => ({
      id: `section-${command.id}`,
      label: command.label,
      group: 'Çalışma alanı',
      route: ['/workspace', 'section', command.id]
      }));
    const project = this.project();
    const views: readonly CommandItem[] = project ? this.availableViews().map(view => ({
      id: `view-${view.id}`,
      label: `${view.label} görünümünü aç`,
      group: project.name,
      route: ['/workspace', project.id, view.id]
    })) : [];
    const normalized = this.query().trim().toLocaleLowerCase('tr');
    const commands = [...sections, ...views];
    return normalized ? commands.filter(command => command.label.toLocaleLowerCase('tr').includes(normalized)) : commands;
  });

  @HostListener('document:keydown', ['$event'])
  protected handleShortcut(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.open();
    } else if (event.key === 'Escape' && this.opened()) {
      this.close();
    }
  }

  protected open(): void {
    this.opened.set(true);
    this.query.set('');
    setTimeout(() => document.querySelector<HTMLInputElement>('.command-dialog input')?.focus());
  }

  protected close(): void {
    this.opened.set(false);
  }

  protected updateQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }
}
