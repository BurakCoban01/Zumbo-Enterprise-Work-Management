import { inject, Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { ZUMBO_RUNTIME_CONFIG } from './runtime-config';
import { ZumboSessionService } from './session.service';

export interface WorkItemRealtimeChange {
  readonly eventType: string;
  readonly projectId: string;
  readonly workItemId: string;
  readonly resourceVersion: number;
  readonly schemaVersion: number;
  readonly workItem?: { readonly id: string; readonly version: number };
}

@Injectable({ providedIn: 'root' })
export class ZumboRealtimeService {
  private readonly runtime = inject(ZUMBO_RUNTIME_CONFIG);
  private readonly session = inject(ZumboSessionService);
  private readonly protocolVersion = 1;
  private connection: HubConnection | null = null;
  private projectId: string | null = null;
  private readonly knownVersions = new Map<string, number>();
  private resyncPending = false;
  private readonly changesSubject = new Subject<WorkItemRealtimeChange>();
  private readonly resyncSubject = new Subject<string>();

  readonly state = signal<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>('disconnected');
  readonly changes$ = this.changesSubject.asObservable();
  readonly resync$ = this.resyncSubject.asObservable();

  async connect(projectId: string): Promise<void> {
    if (this.connection && this.projectId === projectId) return;
    await this.stop();
    this.projectId = projectId;
    this.state.set('connecting');
    this.synchronize([]);
    const connection = new HubConnectionBuilder()
      .withUrl(`${this.runtime.apiBaseUrl}/hubs/work-items`, {
        withCredentials: true,
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true,
        headers: { 'X-CSRF-Token': this.session.getCsrf() || '' }
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .withStatefulReconnect({ bufferSize: 65536 })
      .build();
    this.connection = connection;
    connection.on('workItemChanged', change => this.accept(change as WorkItemRealtimeChange));
    connection.onreconnecting(() => this.state.set('reconnecting'));
    connection.onreconnected(async () => {
      this.state.set('connected');
      this.synchronize([]);
      try {
        if (this.projectId) await connection.invoke('SubscribeProject', this.projectId);
        this.requestResync('reconnected');
      } catch {
        this.requestResync('subscription-failed');
      }
    });
    connection.onclose(() => this.state.set('disconnected'));
    await connection.start();
    if (this.projectId !== projectId) return;
    await connection.invoke('SubscribeProject', projectId);
    this.state.set('connected');
  }

  remember(item: { readonly id?: string; readonly version?: number }): void {
    if (!item.id || !Number.isSafeInteger(item.version)) return;
    const previous = this.knownVersions.get(item.id);
    if (previous == null || Number(item.version) > previous) this.knownVersions.set(item.id, Number(item.version));
  }

  synchronize(items: readonly { readonly id?: string; readonly version?: number }[]): void {
    this.knownVersions.clear();
    for (const item of items) this.remember(item);
    this.resyncPending = false;
  }

  async stop(): Promise<void> {
    const active = this.connection;
    this.connection = null;
    this.projectId = null;
    this.knownVersions.clear();
    this.resyncPending = false;
    if (active) await active.stop();
    this.state.set('disconnected');
  }

  private accept(change: WorkItemRealtimeChange): void {
    if (!this.projectId || change.projectId !== this.projectId) return;
    if (change.schemaVersion !== this.protocolVersion || !Number.isSafeInteger(change.resourceVersion)) {
      this.requestResync('protocol');
      return;
    }
    const previous = this.knownVersions.get(change.workItemId);
    if (previous != null && change.resourceVersion <= previous) return;
    if (previous != null && change.resourceVersion > previous + 1) {
      this.requestResync('version-gap');
      return;
    }
    this.knownVersions.set(change.workItemId, change.resourceVersion);
    this.changesSubject.next(change);
  }

  private requestResync(reason: string): void {
    if (!this.projectId || this.resyncPending) return;
    this.resyncPending = true;
    this.resyncSubject.next(reason);
  }
}
