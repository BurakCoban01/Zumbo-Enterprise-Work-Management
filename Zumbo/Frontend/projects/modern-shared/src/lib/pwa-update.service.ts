import { Injectable, signal } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';

@Injectable({ providedIn: 'root' })
export class PwaUpdateService {
  private readonly readyState = signal(false);
  private readonly activatingState = signal(false);

  readonly ready = this.readyState.asReadonly();
  readonly activating = this.activatingState.asReadonly();

  constructor(private readonly updates: SwUpdate) {
    if (!updates.isEnabled) return;
    updates.versionUpdates.subscribe(event => {
      if (event.type === 'VERSION_READY') this.readyState.set(true);
    });
  }

  async activate(): Promise<void> {
    if (!this.readyState() || this.activatingState()) return;
    this.activatingState.set(true);
    try {
      await this.updates.activateUpdate();
      location.reload();
    } catch {
      this.activatingState.set(false);
    }
  }
}
