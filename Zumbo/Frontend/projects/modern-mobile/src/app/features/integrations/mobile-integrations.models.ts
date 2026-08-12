export type MobileIntegrationsTab = 'webhooks' | 'development';
export type MobileIntegrationsView = 'list' | 'webhook-detail' | 'webhook-editor' | 'development-detail' | 'development-editor';

export interface MobileIntegrationRole { readonly name: string; readonly permissions: readonly string[]; readonly isActive: boolean; }
export interface MobileWebhook { readonly id: string; readonly name: string; readonly targetUrl: string; readonly eventScopes: readonly string[]; readonly isActive: boolean; readonly secretFingerprint: string; readonly secretVersion: number; readonly version: number; }
export interface MobileWebhookMetrics { readonly pending: number; readonly processing: number; readonly delivered: number; readonly deadLetter: number; }
export interface MobileWebhookDelivery { readonly id: string; readonly status: string; readonly eventScope?: string | null; readonly attempts?: number | null; readonly lastErrorCode?: string | null; }
export interface MobileWebhookReceipt { readonly subscription: MobileWebhook; readonly secret?: string | null; }
export interface MobileSecretReceipt { readonly secret: string; readonly fingerprint: string; readonly version: number; }
export interface MobileDevelopmentConnection { readonly id: string; readonly name: string; readonly provider: string; readonly baseUrl: string; readonly isConnected: boolean; readonly healthStatus: string; readonly healthErrorCode?: string | null; readonly webhookSecretFingerprint: string; readonly webhookSecretVersion: number; readonly version: number; }
export interface MobileDevelopmentReceipt { readonly connection: MobileDevelopmentConnection; readonly webhookSecret: string; }
export interface MobileDevelopmentRepository { readonly externalRepositoryId: string; readonly name: string; readonly fullName: string; readonly url: string; readonly defaultBranch: string; }
export interface MobileDevelopmentMapping { readonly id: string; readonly projectId: string; readonly projectName?: string | null; readonly repositoryName: string; readonly repositoryFullName: string; readonly repositoryUrl: string; readonly defaultBranch: string; readonly isActive?: boolean; readonly version: number; }
export interface MobileIntegrationProject { readonly id: string; readonly key: string; readonly name: string; }
export interface MobileWebhookDraft { name: string; targetUrl: string; eventScopes: string[]; expectedVersion?: number; }
export interface MobileDevelopmentDraft { name: string; provider: 'GitHub' | 'GitLab'; baseUrl: string; accessToken: string; }
export interface MobileDevelopmentMappingDraft { projectId: string; repositoryId: string; }
