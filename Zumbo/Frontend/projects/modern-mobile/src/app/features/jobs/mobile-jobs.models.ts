export interface MobileJobRole {
  readonly name: string;
  readonly permissions: readonly string[];
  readonly isActive: boolean;
}

export interface MobileJobsProject {
  readonly id: string;
  readonly key: string;
  readonly name: string;
  readonly members?: readonly { readonly userId: string; readonly role?: string | null }[];
}

export interface MobileBulkJobPage {
  readonly items: readonly MobileBulkJob[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export interface MobileBulkJob {
  readonly id: string;
  readonly projectId: string;
  readonly type: string;
  readonly operation?: string | null;
  readonly dryRun: boolean;
  readonly state: string;
  readonly totalItems: number;
  readonly processedItems: number;
  readonly succeededItems: number;
  readonly failedItems: number;
  readonly cancelRequested: boolean;
  readonly hasResult: boolean;
  readonly hasErrorFile: boolean;
  readonly lastErrorCode?: string | null;
  readonly lastErrorMessage?: string | null;
  readonly createdAt: string;
  readonly startedAt?: string | null;
  readonly completedAt?: string | null;
  readonly artifactsExpireAt?: string | null;
  readonly version: number;
}

export interface MobileImportRow {
  readonly sourceKey: string;
  readonly boardId: string;
  readonly title: string;
  readonly type: string;
  readonly priority: string;
  readonly assigneeUserId: string | null;
  readonly dueDate: string | null;
  readonly parentId: string | null;
  readonly teamId: string | null;
  readonly customFields: readonly unknown[];
}

export interface MobileParsedImport {
  readonly valid: boolean;
  readonly rows: readonly MobileImportRow[];
  readonly errors: readonly string[];
  readonly totalErrors: number;
}

export interface MobileJobState {
  readonly label: string;
  readonly tone: 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'muted';
}
