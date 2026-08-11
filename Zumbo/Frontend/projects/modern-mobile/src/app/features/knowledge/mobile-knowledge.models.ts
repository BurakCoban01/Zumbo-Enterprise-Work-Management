export interface KnowledgeSearchResponse { readonly items:readonly KnowledgeSummary[]; readonly visibleTotal:number; readonly sourceStatus:string; }
export interface KnowledgeSummary { readonly id:string; readonly scopeName:string; readonly title:string; readonly excerpt:string; readonly tags:readonly string[]; readonly currentContentVersion:number; readonly archived:boolean; }
export interface KnowledgeDocument { readonly id:string; readonly scopeType:string; readonly scopeId:string; readonly scopeName:string; readonly title:string; readonly contentMarkdown:string; readonly tags:readonly string[]; readonly workItemIds:readonly string[]; readonly userIds:readonly string[]; readonly currentContentVersion:number; readonly versions:readonly KnowledgeVersion[]; readonly comments:readonly KnowledgeComment[]; readonly canEdit:boolean; readonly canComment:boolean; readonly archived:boolean; readonly version:number; }
export interface KnowledgeVersion { readonly number:number; readonly title:string; readonly changeSummary:string; readonly authorUserId:string; readonly createdAt:string; }
export interface KnowledgeComment { readonly id:string; readonly body:string; readonly authorUserId:string; readonly resolved:boolean; readonly createdAt:string; }
export interface KnowledgeLinkOption { readonly id:string; readonly label:string; readonly context?:string|null; }
export interface KnowledgeLinkOptions { readonly workItems:readonly KnowledgeLinkOption[]; readonly users:readonly KnowledgeLinkOption[]; readonly sourceStatus:string; }
export interface KnowledgeContext { readonly document:KnowledgeDocument; readonly links:KnowledgeLinkOptions; }
export interface KnowledgeRole { readonly name:string; readonly permissions:readonly string[]; readonly isActive:boolean; }
export interface KnowledgeInitiative { readonly id:string; readonly name:string; readonly ownerUserId:string; readonly projectIds:readonly string[]; readonly canUpdateStatus:boolean; }
export interface KnowledgePortfolio { readonly id:string; readonly name:string; readonly canEdit:boolean; readonly initiatives:readonly KnowledgeInitiative[]; }
export interface KnowledgeScope { readonly key:string; readonly type:'Project'|'Initiative'; readonly id:string; readonly label:string; }
export interface KnowledgeDraft { id?:string; scopeKey:string; title:string; contentMarkdown:string; tagsText:string; changeSummary:string; version?:number; }
