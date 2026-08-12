import { inject, Injectable } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { catchError, defer, forkJoin, map, Observable, of } from 'rxjs';
import { MobileDeadLetter, MobileDependencyStatus, MobileOperationsRead, MobileOperationsRole, MobileOperationsSnapshot, MobileQueueMetrics, MobileSearchReconcileResult, MobileStorageStatus } from './mobile-operations.models';

@Injectable()
export class MobileOperationsService {
  private readonly api = inject(ZumboApiClient);

  roles(): Observable<readonly MobileOperationsRole[]> { return this.api.get('/api/auth/roles?scope=System'); }

  load(organizationId: string): Observable<MobileOperationsSnapshot> {
    const id = encodeURIComponent(organizationId);
    return defer(() => {
      const failures: MobileOperationsRead[] = [];
      const safe = <T>(read: MobileOperationsRead, request: Observable<T>, fallback: T): Observable<T> => request.pipe(catchError(() => {
        failures.push(read);
        return of(fallback);
      }));
      return forkJoin({
        dependencies: safe<MobileDependencyStatus | undefined>('dependencies', this.api.get<MobileDependencyStatus>('/api/operations/external-dependencies'), undefined),
        messaging: safe<MobileQueueMetrics | undefined>('messaging', this.api.get<MobileQueueMetrics>('/api/work-items/durable-messaging/metrics'), undefined),
        messageDeadLetters: safe<readonly MobileDeadLetter[]>('messageDeadLetters', this.api.get<readonly MobileDeadLetter[]>('/api/work-items/durable-messaging/dead-letters?pageSize=20'), []),
        notifications: safe<MobileQueueMetrics | undefined>('notifications', this.api.get<MobileQueueMetrics>(`/api/notifications/delivery/status?organizationId=${id}`), undefined),
        notificationDeadLetters: safe<readonly MobileDeadLetter[]>('notificationDeadLetters', this.api.get<readonly MobileDeadLetter[]>(`/api/notifications/delivery/dead-letters?organizationId=${id}&pageSize=20`), []),
        storage: safe<MobileStorageStatus | undefined>('storage', this.api.get<MobileStorageStatus>(`/api/operations/storage/security?organizationId=${id}`), undefined)
      }).pipe(map(value => ({ ...value, failures })));
    });
  }

  reconcile(): Observable<MobileSearchReconcileResult> { return this.api.post('/api/work-items/search/reconcile', {}); }
  replayMessage(id: string): Observable<{ readonly replayed: boolean }> { return this.api.post(`/api/work-items/durable-messaging/dead-letter/${encodeURIComponent(id)}/replay`, {}); }
  replayNotification(id: string, organizationId: string): Observable<void> { return this.api.post(`/api/notifications/delivery/${encodeURIComponent(id)}/replay?organizationId=${encodeURIComponent(organizationId)}`, {}); }
  maintainStorage(organizationId: string): Observable<{ readonly retried: number }> { return this.api.post(`/api/operations/storage/security/maintenance?organizationId=${encodeURIComponent(organizationId)}`, {}); }
}
