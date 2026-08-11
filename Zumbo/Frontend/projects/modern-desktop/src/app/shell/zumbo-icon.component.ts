import { Component, ElementRef, Input, OnChanges, inject } from '@angular/core';
import {
  ArchiveRestore,
  Bell,
  BookOpenText,
  BriefcaseBusiness,
  ChartNoAxesCombined,
  ChevronDown,
  ChevronsLeft,
  FolderKanban,
  Gauge,
  House,
  Inbox,
  Kanban,
  ListChecks,
  LogOut,
  Menu,
  Milestone,
  Moon,
  Search,
  Settings,
  Star,
  Sun,
  Target,
  UsersRound,
  createElement
} from 'lucide';
import { IconName } from './desktop-shell.models';

const ICONS = {
  archive: ArchiveRestore,
  bell: Bell,
  book: BookOpenText,
  briefcase: BriefcaseBusiness,
  chart: ChartNoAxesCombined,
  'chevron-down': ChevronDown,
  'chevrons-left': ChevronsLeft,
  folder: FolderKanban,
  gauge: Gauge,
  home: House,
  inbox: Inbox,
  kanban: Kanban,
  list: ListChecks,
  logout: LogOut,
  menu: Menu,
  milestone: Milestone,
  moon: Moon,
  search: Search,
  settings: Settings,
  star: Star,
  sun: Sun,
  target: Target,
  users: UsersRound
} as const;

@Component({
  selector: 'zumbo-icon',
  template: '',
  host: { 'aria-hidden': 'true' }
})
export class ZumboIconComponent implements OnChanges {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input({ required: true }) name!: IconName;
  @Input() size = 18;

  ngOnChanges(): void {
    const svg = createElement(ICONS[this.name], {
      width: this.size,
      height: this.size,
      'stroke-width': 2
    });
    this.host.nativeElement.replaceChildren(svg);
  }
}
