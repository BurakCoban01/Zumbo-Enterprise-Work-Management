import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, map } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { NotificationService } from '../notifications/notification.service';
import { PersonalWorkService } from '../personal-work/personal-work.service';
import { HomeData } from './home.models';

@Injectable()
export class HomeService {
  private readonly notifications = inject(NotificationService);
  private readonly personalWork = inject(PersonalWorkService);

  load(projects: readonly ProjectSummary[], userId: string): Observable<HomeData> {
    return forkJoin({
      work: this.personalWork.load(projects, userId),
      notifications: this.notifications.load()
    }).pipe(map(({ work, notifications }) => ({
      tasks: work.tasks,
      notifications: notifications.items,
      partial: work.partial
    })));
  }

  markNotificationRead(notificationId: string): Observable<unknown> {
    return this.notifications.markRead(notificationId);
  }
}
