export type AccountTab = 'account' | 'security' | 'data';
export interface MfaStatus { readonly enabled: boolean; readonly remainingRecoveryCodes: number; }
export interface MfaSetup { readonly secret: string; readonly provisioningUri: string; }
export interface MfaConfirmation { readonly enabled: boolean; readonly recoveryCodes: readonly string[]; }
export interface AccountSession { readonly id: string; readonly deviceName?: string | null; readonly createdAt: string; readonly lastSeenAt: string; readonly expiresAt: string; readonly revokedAt?: string | null; readonly isCurrent: boolean; }
export interface ApiKeySummary { readonly id: string; readonly name: string; readonly keyPrefix: string; readonly expiresAt: string; readonly revokedAt?: string | null; }
export interface CreatedApiKey extends ApiKeySummary { readonly key: string; }
export interface NotificationPreferences { readonly inAppEnabled: boolean; readonly emailEnabled: boolean; readonly mutedTypes: readonly string[]; readonly version?: number; }
export interface PrivacyJob { readonly id: string; readonly state: string; readonly progressPercent?: number; readonly updatedAt?: string; readonly expiresAt?: string | null; readonly lastError?: string | null; }
export interface PrivacyReceipt { readonly job: PrivacyJob; readonly statusToken: string; }
export interface AccountSnapshot { readonly mfa: MfaStatus; readonly sessions: readonly AccountSession[]; readonly apiKeys: readonly ApiKeySummary[]; readonly preferences: NotificationPreferences; readonly failures: readonly string[]; }
