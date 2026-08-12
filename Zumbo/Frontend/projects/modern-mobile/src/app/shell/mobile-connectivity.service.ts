import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class MobileConnectivityService {
  private readonly destroyRef = inject(DestroyRef);
  private readonly onlineState = signal(navigator.onLine);
  readonly online = this.onlineState.asReadonly();
  readonly offline = computed(() => !this.onlineState());

  constructor() {
    const online = () => this.onlineState.set(true);
    const offline = () => this.onlineState.set(false);
    window.addEventListener('online', online);
    window.addEventListener('offline', offline);
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('online', online);
      window.removeEventListener('offline', offline);
    });
  }
}
