export interface MobileOperationsRole {
  readonly name: string;
  readonly permissions: readonly string[];
  readonly isActive: boolean;
}

export interface MobileDependencyMetric {
  readonly dependency: string;
  readonly executions: number;
  readonly failed: number;
  readonly timedOut: number;
  readonly circuitOpen: boolean;
  readonly averageLatencyMilliseconds: number;
}

export interface MobileDependencyStatus {
  readonly status: string;
  readonly dependencies: readonly MobileDependencyMetric[];
}

export interface MobileQueueMetrics {
  readonly pending: number;
  readonly processing: number;
  readonly deadLetter: number;
  readonly completed?: number;
  readonly sent?: number;
}

export interface MobileDeadLetter {
  readonly id: string;
  readonly eventType?: string;
  readonly type?: string;
  readonly attempts: number;
}

export interface MobileStorageStatus {
  readonly quarantined: number;
  readonly clean: number;
  readonly rejected: number;
}

export type MobileOperationsRead = 'dependencies' | 'messaging' | 'messageDeadLetters' | 'notifications' | 'notificationDeadLetters' | 'storage';

export interface MobileOperationsSnapshot {
  readonly dependencies?: MobileDependencyStatus;
  readonly messaging?: MobileQueueMetrics;
  readonly messageDeadLetters: readonly MobileDeadLetter[];
  readonly notifications?: MobileQueueMetrics;
  readonly notificationDeadLetters: readonly MobileDeadLetter[];
  readonly storage?: MobileStorageStatus;
  readonly failures: readonly MobileOperationsRead[];
}

export interface MobileSearchReconcileResult {
  readonly indexed: number;
  readonly removed: number;
}
