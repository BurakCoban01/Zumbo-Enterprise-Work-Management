import { Component, ElementRef, HostListener, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { NotificationItem, notificationLabel } from './notification.models';
import { NotificationService } from './notification.service';

@Component({
  selector: 'zumbo-notification-popover',
  imports: [RouterLink, ZumboIconComponent],
  providers: [NotificationService],
  templateUrl: './notification-popover.component.html',
  styleUrl: './notification-popover.component.scss'
})
export class NotificationPopoverComponent {
  readonly initialUnreadCount = input(0);
  readonly unreadChange = output<number>();

  protected readonly open = signal(false);
  protected readonly loaded = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  protected readonly notifications = signal<readonly NotificationItem[]>([]);

  constructor(
    private readonly host: ElementRef<HTMLElement>,
    private readonly notificationService: NotificationService
  ) {}

  protected unreadCount(): number {
    return this.initialUnreadCount();
  }

  protected toggle(): void {
    this.open.update(value => !value);
    if (this.open() && !this.loaded() && !this.loading()) this.load();
  }

  protected close(): void {
    this.open.set(false);
  }

  protected read(notification: NotificationItem): void {
    if (notification.read) return;
    this.notificationService.markRead(notification.id).subscribe({
      next: () => {
        this.notifications.update(items => items.map(item => item.id === notification.id ? { ...item, read: true } : item));
        this.unreadChange.emit(this.notifications().filter(item => !item.read).length);
      }
    });
  }

  protected label(notification: NotificationItem): string {
    return notificationLabel(notification);
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) this.close();
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.close();
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.notificationService.load().pipe(finalize(() => this.loading.set(false))).subscribe({
      next: result => {
        this.notifications.set(result.items);
        this.loaded.set(true);
        this.unreadChange.emit(result.items.filter(item => !item.read).length);
      },
      error: () => this.error.set(true)
    });
  }
}
