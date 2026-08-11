import { Component, ElementRef, Input, OnChanges, inject } from '@angular/core';
import {
  ArchiveRestore,
  ArrowUpRight,
  Bell,
  Bookmark,
  BookOpenText,
  BriefcaseBusiness,
  ChartNoAxesCombined,
  CheckCheck,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
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
  Plus,
  RefreshCw,
  Search,
  Save,
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
  'arrow-up-right': ArrowUpRight,
  bell: Bell,
  bookmark: Bookmark,
  book: BookOpenText,
  briefcase: BriefcaseBusiness,
  chart: ChartNoAxesCombined,
  'check-check': CheckCheck,
  'chevron-down': ChevronDown,
  'chevron-left': ChevronLeft,
  'chevron-right': ChevronRight,
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
  plus: Plus,
  refresh: RefreshCw,
  search: Search,
  save: Save,
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
