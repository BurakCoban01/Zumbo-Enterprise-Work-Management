export interface DependencyMetric{readonly dependency:string;readonly executions:number;readonly failed:number;readonly timedOut:number;readonly circuitOpen:boolean;readonly averageLatencyMilliseconds:number}
export interface DependencyStatus{readonly status:string;readonly dependencies:readonly DependencyMetric[]}
export interface QueueMetrics{readonly pending:number;readonly processing:number;readonly deadLetter:number;readonly completed?:number;readonly sent?:number;readonly disabled?:number}
export interface DeadLetter{readonly id:string;readonly eventType?:string;readonly type?:string;readonly attempts:number;readonly deadLetteredAtUtc?:string;readonly deadLetteredAt?:string;readonly status?:string}
export interface StorageStatus{readonly quarantined:number;readonly clean:number;readonly rejected:number;readonly oldestQuarantinedAt?:string|null}
export interface OperationsSnapshot{readonly dependencies?:DependencyStatus;readonly messaging?:QueueMetrics;readonly messageDeadLetters:readonly DeadLetter[];readonly notifications?:QueueMetrics;readonly notificationDeadLetters:readonly DeadLetter[];readonly storage?:StorageStatus;readonly failures:readonly string[]}
