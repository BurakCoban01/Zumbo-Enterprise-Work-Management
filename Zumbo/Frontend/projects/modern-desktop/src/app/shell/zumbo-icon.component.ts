import { Component, ElementRef, Input, OnChanges, inject } from '@angular/core';
import {
  ArchiveRestore,
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  ArrowUpRight,
  Bell,
  Bookmark,
  BookOpenText,
  BriefcaseBusiness,
  ChartNoAxesCombined,
  Check,
  CheckCheck,
  Columns3,
  CopyCheck,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  FolderKanban,
  Eye,
  Gauge,
  House,
  Inbox,
  Kanban,
  ListChecks,
  Link2,
  LogOut,
  Menu,
  MessageSquareText,
  Milestone,
  Moon,
  Paperclip,
  Pencil,
  Plus,
  RefreshCw,
  Rows3,
  Search,
  Save,
  Settings,
  Star,
  Sun,
  Target,
  Trash2,
  Unlink,
  UsersRound,
  X,
  createElement
} from 'lucide';
import { IconName } from './desktop-shell.models';

const ICONS = {
  archive: ArchiveRestore,
  'arrow-down': ArrowDown,
  'arrow-left': ArrowLeft,
  'arrow-right': ArrowRight,
  'arrow-up': ArrowUp,
  'arrow-up-right': ArrowUpRight,
  bell: Bell,
  bookmark: Bookmark,
  book: BookOpenText,
  briefcase: BriefcaseBusiness,
  chart: ChartNoAxesCombined,
  check: Check,
  'check-check': CheckCheck,
  columns: Columns3,
  copy: CopyCheck,
  'chevron-down': ChevronDown,
  'chevron-left': ChevronLeft,
  'chevron-right': ChevronRight,
  'chevrons-left': ChevronsLeft,
  folder: FolderKanban,
  edit: Pencil,
  eye: Eye,
  gauge: Gauge,
  home: House,
  inbox: Inbox,
  kanban: Kanban,
  list: ListChecks,
  link: Link2,
  logout: LogOut,
  menu: Menu,
  'message-square': MessageSquareText,
  milestone: Milestone,
  moon: Moon,
  paperclip: Paperclip,
  plus: Plus,
  refresh: RefreshCw,
  rows: Rows3,
  search: Search,
  save: Save,
  settings: Settings,
  star: Star,
  sun: Sun,
  target: Target,
  trash: Trash2,
  unlink: Unlink,
  users: UsersRound,
  x: X
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
