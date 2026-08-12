export type KnowledgeTab = 'content' | 'history' | 'links' | 'comments' | 'edit';
export type KnowledgeBlock =
  | { readonly type: 'heading'; readonly level: number; readonly segments: readonly KnowledgeSegment[] }
  | { readonly type: 'paragraph' | 'quote'; readonly segments: readonly KnowledgeSegment[] }
  | { readonly type: 'code'; readonly text: string; readonly language: string }
  | { readonly type: 'list'; readonly ordered: boolean; readonly items: readonly (readonly KnowledgeSegment[])[] };
export interface KnowledgeSegment { readonly type: 'text' | 'link' | 'code' | 'strong'; readonly text: string; readonly href?: string; }
export interface KnowledgeSearchResponse { readonly items: readonly KnowledgeSummary[]; readonly visibleTotal: number; readonly scannedDocuments: number; readonly sourceStatus: string; }
export interface KnowledgeSummary { readonly id: string; readonly scopeType: string; readonly scopeId: string; readonly scopeName: string; readonly ownerUserId: string; readonly title: string; readonly excerpt: string; readonly tags: readonly string[]; readonly currentContentVersion: number; readonly canEdit: boolean; readonly archived: boolean; readonly updatedAt: string; readonly version: number; }
export interface KnowledgeDocument { readonly id: string; readonly scopeType: string; readonly scopeId: string; readonly scopeName: string; readonly ownerUserId: string; readonly title: string; readonly contentMarkdown: string; readonly tags: readonly string[]; readonly workItemIds: readonly string[]; readonly userIds: readonly string[]; readonly currentContentVersion: number; readonly versions: readonly KnowledgeVersionSummary[]; readonly comments: readonly KnowledgeComment[]; readonly canEdit: boolean; readonly canComment: boolean; readonly archived: boolean; readonly updatedAt: string; readonly version: number; }
export interface KnowledgeVersionSummary { readonly number: number; readonly title: string; readonly changeSummary: string; readonly authorUserId: string; readonly createdAt: string; }
export interface KnowledgeVersion { readonly number: number; readonly title: string; readonly contentMarkdown: string; readonly tags: readonly string[]; readonly workItemIds: readonly string[]; readonly userIds: readonly string[]; readonly changeSummary: string; readonly authorUserId: string; readonly createdAt: string; }
export interface KnowledgeComment { readonly id: string; readonly body: string; readonly authorUserId: string; readonly resolved: boolean; readonly resolvedByUserId?: string | null; readonly resolvedAt?: string | null; readonly createdAt: string; }
export interface KnowledgeLinkOptions { readonly workItems: readonly KnowledgeLinkOption[]; readonly users: readonly KnowledgeLinkOption[]; readonly sourceStatus: string; }
export interface KnowledgeLinkOption { readonly id: string; readonly label: string; readonly context?: string | null; }
export interface KnowledgeRole { readonly name: string; readonly permissions: readonly string[]; readonly isActive: boolean; }
export interface KnowledgePortfolio { readonly id: string; readonly name: string; readonly canEdit: boolean; readonly initiatives: readonly KnowledgeInitiative[]; }
export interface KnowledgeInitiative { readonly id: string; readonly name: string; readonly ownerUserId: string; readonly projectIds: readonly string[]; readonly canUpdateStatus: boolean; }
export interface KnowledgeScope { readonly key: string; readonly type: 'Project' | 'Initiative'; readonly id: string; readonly label: string; readonly projectIds: readonly string[]; }
export interface KnowledgeDraft { id?: string; scopeKey: string; scopeType: string; scopeId: string; title: string; contentMarkdown: string; tagsText: string; workItemIds: string[]; userIds: string[]; changeSummary: string; version?: number; }
