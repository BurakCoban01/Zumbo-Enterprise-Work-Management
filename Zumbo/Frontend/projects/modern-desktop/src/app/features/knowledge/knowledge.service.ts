import { Injectable,inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { forkJoin,Observable } from 'rxjs';
import { tags } from './knowledge.core';
import { KnowledgeDocument,KnowledgeDraft,KnowledgeLinkOptions,KnowledgePortfolio,KnowledgeRole,KnowledgeScope,KnowledgeSearchResponse,KnowledgeVersion } from './knowledge.models';

@Injectable() export class KnowledgeService {
  private readonly api=inject(ZumboApiClient);
  context(query=''){const suffix=query.trim()?`&query=${encodeURIComponent(query.trim())}`:'';return forkJoin({documents:this.api.get<KnowledgeSearchResponse>(`/api/knowledge-documents?page=1&pageSize=100${suffix}`),portfolios:this.api.get<{readonly items:readonly KnowledgePortfolio[]}>('/api/portfolios?page=1&pageSize=100'),roles:this.api.get<readonly KnowledgeRole[]>('/api/auth/roles?scope=Project')});}
  detail(id:string){return this.api.get<KnowledgeDocument>(`/api/knowledge-documents/${encodeURIComponent(id)}`);}
  links(scope:KnowledgeScope){return this.api.get<KnowledgeLinkOptions>(`/api/knowledge-documents/scope-link-options?scopeType=${encodeURIComponent(scope.type)}&scopeId=${encodeURIComponent(scope.id)}`);}
  version(id:string,number:number){return this.api.get<KnowledgeVersion>(`/api/knowledge-documents/${encodeURIComponent(id)}/versions/${number}`);}
  save(draft:KnowledgeDraft,scope:KnowledgeScope):Observable<KnowledgeDocument>{const body={title:draft.title.trim(),contentMarkdown:draft.contentMarkdown.trim(),tags:tags(draft.tagsText),workItemIds:[...new Set(draft.workItemIds)],userIds:[...new Set(draft.userIds)],changeSummary:draft.changeSummary.trim()};return draft.id?this.api.put(`/api/knowledge-documents/${encodeURIComponent(draft.id)}`,body,{ifMatch:draft.version}):this.api.post('/api/knowledge-documents',{scopeType:scope.type,scopeId:scope.id,...body},{idempotencyKey:this.api.newIdempotencyKey()});}
  comment(document:KnowledgeDocument,body:string){return this.api.post<KnowledgeDocument>(`/api/knowledge-documents/${encodeURIComponent(document.id)}/comments`,{body:body.trim()},{ifMatch:document.version});}
  resolve(document:KnowledgeDocument,commentId:string){return this.api.patch<KnowledgeDocument>(`/api/knowledge-documents/${encodeURIComponent(document.id)}/comments/${encodeURIComponent(commentId)}/resolve`,{},{ifMatch:document.version});}
  archive(document:KnowledgeDocument){return this.api.delete<{readonly archived:boolean}>(`/api/knowledge-documents/${encodeURIComponent(document.id)}`,{ifMatch:document.version});}
}
