import {inject,Injectable} from '@angular/core';
import {ZumboApiClient} from '@zumbo/modern-shared';
import {forkJoin,of} from 'rxjs';
import {KnowledgeDocument,KnowledgeDraft,KnowledgeLinkOptions,KnowledgePortfolio,KnowledgeRole,KnowledgeScope,KnowledgeSearchResponse} from './mobile-knowledge.models';
@Injectable() export class MobileKnowledgeService {
 private readonly api=inject(ZumboApiClient);
 list(query=''){const suffix=query.trim()?`&query=${encodeURIComponent(query.trim())}`:'';return forkJoin({documents:this.api.get<KnowledgeSearchResponse>(`/api/knowledge-documents?page=1&pageSize=100${suffix}`),portfolios:this.api.get<{readonly items:readonly KnowledgePortfolio[]}>('/api/portfolios?page=1&pageSize=100'),roles:this.api.get<readonly KnowledgeRole[]>('/api/auth/roles?scope=Project')});}
 detail(id:string){const value=encodeURIComponent(id);return this.api.get<KnowledgeDocument>(`/api/knowledge-documents/${value}`);}
 context(document:KnowledgeDocument){return forkJoin({document:of(document),links:this.api.get<KnowledgeLinkOptions>(`/api/knowledge-documents/scope-link-options?scopeType=${encodeURIComponent(document.scopeType)}&scopeId=${encodeURIComponent(document.scopeId)}`)});}
 comment(document:KnowledgeDocument,body:string){return this.api.post<KnowledgeDocument>(`/api/knowledge-documents/${encodeURIComponent(document.id)}/comments`,{body:body.trim()},{ifMatch:document.version,idempotencyKey:this.api.newIdempotencyKey()});}
 resolve(document:KnowledgeDocument,commentId:string){return this.api.patch<KnowledgeDocument>(`/api/knowledge-documents/${encodeURIComponent(document.id)}/comments/${encodeURIComponent(commentId)}/resolve`,{},{ifMatch:document.version});}
 save(draft:KnowledgeDraft,scope:KnowledgeScope,document?:KnowledgeDocument|null){const body={title:draft.title.trim(),contentMarkdown:draft.contentMarkdown.trim(),tags:[...new Set(draft.tagsText.split(',').map(value=>value.trim()).filter(Boolean))],workItemIds:document?.workItemIds||[],userIds:document?.userIds||[],changeSummary:draft.changeSummary.trim()};return draft.id?this.api.put<KnowledgeDocument>(`/api/knowledge-documents/${encodeURIComponent(draft.id)}`,body,{ifMatch:draft.version}):this.api.post<KnowledgeDocument>('/api/knowledge-documents',{scopeType:scope.type,scopeId:scope.id,...body},{idempotencyKey:this.api.newIdempotencyKey()});}
 archive(document:KnowledgeDocument){return this.api.delete<{readonly archived:boolean}>(`/api/knowledge-documents/${encodeURIComponent(document.id)}`,{ifMatch:document.version});}
}
