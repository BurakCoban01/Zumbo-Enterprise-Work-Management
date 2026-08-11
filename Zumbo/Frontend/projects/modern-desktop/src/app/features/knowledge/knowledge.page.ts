import { CommonModule } from '@angular/common';
import { Component,DestroyRef,OnInit,computed,inject,input,signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { knowledgeDraft,knowledgeError,knowledgeScopes,parseMarkdown } from './knowledge.core';
import { KnowledgeComment,KnowledgeDocument,KnowledgeDraft,KnowledgeLinkOptions,KnowledgePortfolio,KnowledgeRole,KnowledgeScope,KnowledgeSummary,KnowledgeTab,KnowledgeVersion } from './knowledge.models';
import { KnowledgeService } from './knowledge.service';

@Component({
  selector:'zumbo-knowledge-page',
  imports:[CommonModule,FormsModule,ZumboIconComponent],
  providers:[KnowledgeService],
  templateUrl:'./knowledge.page.html',
  styleUrls:['./knowledge.page.scss','./knowledge-layout.scss','./knowledge-responsive.scss','./knowledge-theme.scss']
})
export class KnowledgePage implements OnInit {
  readonly projects=input.required<readonly ProjectSummary[]>();
  readonly userId=input.required<string>();
  private readonly api=inject(KnowledgeService);
  private readonly destroyRef=inject(DestroyRef);
  protected readonly loading=signal(true);
  protected readonly busy=signal(false);
  protected readonly error=signal<string|null>(null);
  protected readonly notice=signal<string|null>(null);
  protected readonly documents=signal<readonly KnowledgeSummary[]>([]);
  protected readonly portfolios=signal<readonly KnowledgePortfolio[]>([]);
  protected readonly roles=signal<readonly KnowledgeRole[]>([]);
  protected readonly selected=signal<KnowledgeDocument|null>(null);
  protected readonly linkOptions=signal<KnowledgeLinkOptions>({workItems:[],users:[],sourceStatus:'Ready'});
  protected readonly preview=signal<KnowledgeVersion|null>(null);
  protected readonly tab=signal<KnowledgeTab>('content');
  protected readonly query=signal('');
  protected readonly sourceStatus=signal('Ready');
  protected commentBody='';
  protected draft:KnowledgeDraft=knowledgeDraft();
  protected readonly scopes=computed(()=>knowledgeScopes(this.projects(),this.portfolios(),this.userId(),this.roles()));
  protected readonly canCreate=computed(()=>this.scopes().length>0);
  protected readonly unresolved=computed(()=>this.selected()?.comments.filter(comment=>!comment.resolved).length??0);
  protected readonly blocks=computed(()=>parseMarkdown(this.preview()?.contentMarkdown??this.selected()?.contentMarkdown??''));

  ngOnInit(){this.load();}
  protected load(){this.loading.set(true);this.error.set(null);this.api.context(this.query()).pipe(finalize(()=>this.loading.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:({documents,portfolios,roles})=>{this.documents.set(documents.items);this.portfolios.set(portfolios.items);this.roles.set(roles);this.sourceStatus.set(documents.sourceStatus);const next=documents.items.find(item=>item.id===this.selected()?.id)??documents.items[0];next?this.select(next):this.newDocument();},error:error=>this.fail(error,'Dokümanlar yüklenemedi.')});}
  protected search(value:string){this.query.set(value);this.load();}
  protected select(item:KnowledgeSummary){this.busy.set(true);this.error.set(null);this.api.detail(item.id).pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:document=>{this.selected.set(document);this.preview.set(null);this.draft=knowledgeDraft(undefined,document);this.tab.set('content');this.commentBody='';this.loadLinks(this.scopeFor(document));},error:error=>this.fail(error,'Doküman ayrıntısı yüklenemedi.')});}
  protected newDocument(){if(!this.canCreate()){this.selected.set(null);return;}const scope=this.scopes()[0];this.selected.set(null);this.preview.set(null);this.draft=knowledgeDraft(scope);this.tab.set('edit');this.loadLinks(scope);}
  protected scopeChanged(){const scope=this.currentScope();if(!scope)return;this.draft.scopeType=scope.type;this.draft.scopeId=scope.id;this.draft.workItemIds=[];this.loadLinks(scope);}
  protected save(){const scope=this.currentScope(),message=knowledgeError(this.draft,scope);if(this.selected()&&!this.selected()?.canEdit)return;if(message)return this.error.set(message);if(!scope)return;this.mutate(this.api.save(this.draft,scope),this.draft.id?'Yeni doküman sürümü kaydedildi.':'Doküman oluşturuldu.',document=>{this.selected.set(document);this.load();});}
  protected edit(){const document=this.selected();if(!document?.canEdit)return;this.draft=knowledgeDraft(undefined,document);this.tab.set('edit');}
  protected cancelEdit(){if(this.selected())this.tab.set('content');}
  protected showVersion(version:number){const document=this.selected();if(!document)return;this.mutate(this.api.version(document.id,version),'',value=>{this.preview.set(value);this.tab.set('content');});}
  protected showCurrent(){this.preview.set(null);this.tab.set('content');}
  protected addComment(){const document=this.selected(),body=this.commentBody.trim();if(!document?.canComment||!body)return;this.mutate(this.api.comment(document,body),'Yorum eklendi.',value=>{this.selected.set(value);this.commentBody='';});}
  protected resolve(comment:KnowledgeComment){const document=this.selected();if(!document||comment.resolved||!this.canResolve(comment))return;this.mutate(this.api.resolve(document,comment.id),'Yorum çözüldü.',value=>this.selected.set(value));}
  protected archive(){const document=this.selected();if(!document?.canEdit||!confirm('Bu dokümanı arşivlemek istiyor musunuz?'))return;this.mutate(this.api.archive(document),'Doküman arşivlendi.',()=>{this.selected.set(null);this.load();});}
  protected canResolve(comment:KnowledgeComment){const document=this.selected();return!!document&&!comment.resolved&&(document.canEdit||comment.authorUserId===this.userId());}
  protected toggle(field:'workItemIds'|'userIds',id:string,on:boolean){const values=this.draft[field];this.draft[field]=on?[...new Set([...values,id])]:values.filter(value=>value!==id);}
  protected workItemName(id:string){return this.linkOptions().workItems.find(item=>item.id===id)?.label??'Erişilemeyen iş';}
  protected userName(id:string){return this.linkOptions().users.find(item=>item.id===id)?.label??'Erişilemeyen kullanıcı';}
  protected external(href?:string){return!!href&&(href.startsWith('http://')||href.startsWith('https://'));}
  private currentScope(){return this.scopes().find(scope=>scope.key===this.draft.scopeKey)??(this.selected()?this.scopeFor(this.selected()!):null);}
  private scopeFor(document:KnowledgeDocument):KnowledgeScope{return{key:`${document.scopeType}:${document.scopeId}`,type:document.scopeType as KnowledgeScope['type'],id:document.scopeId,label:document.scopeName,projectIds:[]};}
  private loadLinks(scope:KnowledgeScope){this.api.links(scope).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:value=>this.linkOptions.set(value),error:error=>this.fail(error,'Bağlantı seçenekleri yüklenemedi.')});}
  private mutate<T>(request:import('rxjs').Observable<T>,message:string,done:(value:T)=>void){if(this.busy())return;this.busy.set(true);this.error.set(null);request.pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:value=>{if(message)this.notice.set(message);done(value);},error:error=>this.fail(error,'İşlem tamamlanamadı.')});}
  private fail(error:any,fallback:string){this.error.set(error?.message??fallback);}
}
