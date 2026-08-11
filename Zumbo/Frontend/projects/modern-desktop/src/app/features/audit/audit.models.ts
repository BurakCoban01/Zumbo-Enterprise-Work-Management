export interface AuditUserContext {
  readonly organizationId: string;
  readonly roleNames: readonly string[];
}

export interface AuditRole {
  readonly name: string;
  readonly permissions: readonly string[];
  readonly isActive: boolean;
}

export interface AuditUser {
  readonly id: string;
  readonly username: string;
  readonly email: string;
  readonly organizationId: string;
}

export interface AuditProject { readonly id: string; readonly name: string; }
export interface AuditChange { readonly field: string; readonly oldValue?: string | null; readonly newValue?: string | null; readonly redacted: boolean; }

export interface AuditEntry {
  readonly id: string;
  readonly actorUserId: string;
  readonly action: string;
  readonly entityType: string;
  readonly entityId: string;
  readonly correlationId?: string | null;
  readonly createdAt: string;
  readonly changes?: readonly AuditChange[] | null;
}

export interface AuditPageResponse {
  readonly items: readonly AuditEntry[];
  readonly page: number;
  readonly pageSize: number;
  readonly hasNextPage: boolean;
  readonly nextCursor?: string | null;
}

export interface AuditIntegrity {
  readonly organizationId: string;
  readonly verified: number;
  readonly valid: boolean;
  readonly brokenRecordId?: string | null;
  readonly completeHistory: boolean;
  readonly firstSequence: number;
  readonly anchorHash?: string | null;
}

export interface AuditFilters {
  actorUserId: string;
  action: string;
  entityType: string;
  entityId: string;
  from: string;
  to: string;
}

export type AuditIntegrityState = 'empty' | 'invalid' | 'partial' | 'valid' | 'unknown';
