import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin, map, of } from 'rxjs';
import { NotificationItem, NotificationPage } from './notification.models';

@Injectable()
export class NotificationService {
  private readonly api = inject(ZumboApiClient);

  load(page = 1, pageSize = 50): Observable<NotificationPage> {
    return this.api.get<readonly NotificationItem[]>(`/api/notifications?page=${page}&pageSize=${pageSize}`).pipe(
      map(items => ({ items, page, hasMore: items.length === pageSize }))
    );
  }

  markRead(notificationId: string): Observable<unknown> {
    return this.api.patch(`/api/notifications/${encodeURIComponent(notificationId)}/read`, {});
  }

  markAllRead(notifications: readonly NotificationItem[]): Observable<readonly unknown[]> {
    const unread = notifications.filter(item => !item.read);
    return unread.length ? forkJoin(unread.map(item => this.markRead(item.id))) : of([]);
  }
}
