import { CommonModule } from '@angular/common';
import { Component,DestroyRef,OnInit,computed,inject,input,signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { GOAL_HEALTH,GOAL_STATUSES,goalDraft,goalError,healthLabel,initiativeOptions,keyResultDraft,keyResultError,statusLabel,updateError } from './goal.core';
import { Goal,GoalDraft,GoalRollup,GoalStatusDraft,GoalTab,GoalUser,InitiativeOption,KeyResult,KeyResultDraft,KeyResultProgressDraft } from './goal.models';
import { GoalService } from './goal.service';
@Component({selector:'zumbo-goal-page',imports:[CommonModule,FormsModule,ZumboIconComponent],providers:[GoalService],templateUrl:'./goal.page.html',styleUrls:['./goal.page.scss','./goal-layout.scss','./goal-responsive.scss','./goal-theme.scss']})
export class GoalPage implements OnInit{
 readonly projects=input.required<readonly ProjectSummary[]>();readonly userId=input.required<string>();private readonly api=inject(GoalService);private readonly destroyRef=inject(DestroyRef);
 protected readonly loading=signal(true);protected readonly busy=signal(false);protected readonly error=signal<string|null>(null);protected readonly notice=signal<string|null>(null);
 protected readonly goals=signal<readonly Goal[]>([]);protected readonly users=signal<readonly GoalUser[]>([]);protected readonly options=signal<readonly InitiativeOption[]>([]);protected readonly selected=signal<Goal|null>(null);protected readonly rollup=signal<GoalRollup|null>(null);protected readonly tab=signal<GoalTab>('key-results');protected readonly activeResult=signal<KeyResult|null>(null);protected readonly resultEditorOpen=signal(false);
 protected draft:GoalDraft=goalDraft();protected resultDraft:KeyResultDraft=keyResultDraft('');protected statusDraft:GoalStatusDraft={status:'Active',health:'OnTrack',confidence:null,note:''};protected progressDraft:KeyResultProgressDraft={currentValue:0,confidence:null,note:''};
 protected readonly statuses=GOAL_STATUSES;protected readonly healthStates=GOAL_HEALTH;protected readonly statusLabel=statusLabel;protected readonly healthLabel=healthLabel;
 protected readonly activeGoals=computed(()=>this.goals().filter(x=>x.status==='Active').length);protected readonly averageProgress=computed(()=>this.goals().length?Math.round(this.goals().reduce((n,x)=>n+x.progress,0)/this.goals().length):0);
 ngOnInit(){this.load();}
 protected load(){this.loading.set(true);this.error.set(null);this.api.context().pipe(finalize(()=>this.loading.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:({page,portfolios,users})=>{this.goals.set(page.items);this.users.set(users);this.options.set(initiativeOptions(portfolios.items));const next=page.items.find(x=>x.id===this.selected()?.id)??page.items[0];next?this.select(next):this.create();},error:e=>this.fail(e,'Hedefler yüklenemedi.')});}
 protected select(item:Goal){this.busy.set(true);this.api.detail(item.id).pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:({goal,rollup})=>{this.selected.set(goal);this.rollup.set(rollup);this.draft=goalDraft(goal);this.newResult(false);this.statusDraft={status:goal.status,health:goal.health,confidence:goal.confidence??null,note:''};},error:e=>this.fail(e,'Hedef ayrıntısı yüklenemedi.')});}
 protected create(){this.selected.set(null);this.rollup.set(null);this.draft=goalDraft();this.tab.set('definition');}
 protected saveGoal(){const m=goalError(this.draft);if(m)return this.error.set(m);this.mutate(this.api.saveGoal(this.draft,this.options()),this.draft.id?'Hedef güncellendi.':'Hedef oluşturuldu.',g=>{this.selected.set(g);this.load();});}
 protected archive(){const g=this.selected();if(!g?.canEdit||!confirm('Bu hedefi arşivlemek istiyor musunuz?'))return;this.mutate(this.api.archive(g),'Hedef arşivlendi.',()=>{this.selected.set(null);this.load();});}
 protected newResult(open=true){this.resultDraft=keyResultDraft(this.userId());this.activeResult.set(null);this.resultEditorOpen.set(open);}
 protected editResult(r:KeyResult){this.resultDraft=keyResultDraft(this.userId(),r);this.activeResult.set(null);this.resultEditorOpen.set(true);}
 protected prepareProgress(r:KeyResult){if(!r.canUpdate)return;this.activeResult.set(r);this.resultEditorOpen.set(false);this.progressDraft={currentValue:r.currentValue,confidence:r.confidence??null,note:''};}
 protected saveResult(){const g=this.selected(),m=keyResultError(this.resultDraft);if(!g?.canEdit)return;if(m)return this.error.set(m);this.mutate(this.api.saveKeyResult(g,this.resultDraft),'Key result kaydedildi.',x=>this.refresh(x));}
 protected publishProgress(){const g=this.selected(),r=this.activeResult(),m=updateError(this.progressDraft.note,this.progressDraft.confidence);if(!g||!r?.canUpdate)return;if(m)return this.error.set(m);this.mutate(this.api.progress(g,r.id,this.progressDraft),'İlerleme yayımlandı.',x=>this.refresh(x));}
 protected publishStatus(){const g=this.selected(),m=updateError(this.statusDraft.note,this.statusDraft.confidence);if(!g?.canUpdateStatus)return;if(m)return this.error.set(m);this.mutate(this.api.status(g,this.statusDraft),'Hedef durumu yayımlandı.',x=>this.refresh(x));}
 protected toggle(field:'viewerUserIds'|'initiativeKeys'|'projectIds',id:string,on:boolean){const values=this.draft[field];this.draft[field]=on?[...new Set([...values,id])]:values.filter(x=>x!==id);}
 protected userName(id:string){const u=this.users().find(x=>x.id===id);return u?.username||u?.email||'Bilinmeyen kullanıcı';}
 private refresh(g:Goal){this.selected.set(g);this.select(g);}
 private mutate<T>(request:import('rxjs').Observable<T>,message:string,done:(x:T)=>void){if(this.busy())return;this.busy.set(true);this.error.set(null);request.pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:x=>{this.notice.set(message);done(x);},error:e=>this.fail(e,'İşlem tamamlanamadı.')});}
 private fail(e:any,fallback:string){this.error.set(e?.message??fallback);}
}
