import { CommonModule } from '@angular/common';
import { Component,DestroyRef,OnInit,computed,inject,input,signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { activeCount,auditLabel,canManageTeam,emailError,hasPermission,isTeamOwner,pendingCount,roleLabel,statusLabel,teamNameError } from './teams.core';
import { Team,TeamAudit,TeamDraft,TeamInviteDraft,TeamRole,TeamTab,TeamUserContext } from './teams.models';
import { TeamsService } from './teams.service';
@Component({selector:'zumbo-teams-page',imports:[CommonModule,FormsModule,ZumboIconComponent],providers:[TeamsService],templateUrl:'./teams.page.html',styleUrls:['./teams.page.scss','./teams-layout.scss','./teams-responsive.scss','./teams-theme.scss']})
export class TeamsPage implements OnInit {
  readonly context=input.required<TeamUserContext>();
  private readonly api=inject(TeamsService);private readonly destroyRef=inject(DestroyRef);
  protected readonly loading=signal(true);protected readonly busy=signal(false);protected readonly error=signal<string|null>(null);protected readonly notice=signal<string|null>(null);
  protected readonly teams=signal<readonly Team[]>([]);protected readonly roles=signal<readonly TeamRole[]>([]);protected readonly selected=signal<Team|null>(null);protected readonly audit=signal<readonly TeamAudit[]>([]);protected readonly tab=signal<TeamTab>('members');protected readonly creating=signal(false);
  protected draft:TeamDraft={name:''};protected inviteDraft:TeamInviteDraft={email:'',role:'Member'};
  protected readonly canCreate=computed(()=>hasPermission(this.roles(),this.context(),'TeamManage'));
  protected readonly canManage=computed(()=>{const team=this.selected();return!!team&&canManageTeam(team,this.context().id);});
  protected readonly owner=computed(()=>{const team=this.selected();return!!team&&isTeamOwner(team,this.context().id);});
  protected readonly canReadAudit=computed(()=>hasPermission(this.roles(),this.context(),'AuditRead'));
  protected readonly totalMembers=computed(()=>this.teams().reduce((total,team)=>total+activeCount(team),0));
  protected readonly totalInvites=computed(()=>this.teams().reduce((total,team)=>total+pendingCount(team),0));
  protected readonly roleLabel=roleLabel;protected readonly statusLabel=statusLabel;protected readonly auditLabel=auditLabel;protected readonly activeCount=activeCount;protected readonly pendingCount=pendingCount;
  ngOnInit(){this.load();}
  protected load(){this.loading.set(true);this.error.set(null);this.api.context(this.context()).pipe(finalize(()=>this.loading.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:({teams,roles})=>{this.teams.set(teams);this.roles.set(roles);const next=teams.find(team=>team.id===this.selected()?.id)??teams[0];if(next)this.select(next);else this.startCreate();},error:error=>this.fail(error,'Ekipler yüklenemedi.')});}
  protected select(team:Team){this.selected.set(team);this.creating.set(false);this.draft={name:team.name};this.inviteDraft={email:'',role:'Member'};this.tab.set('members');this.loadAudit(team);}
  protected startCreate(){if(!this.canCreate()){this.selected.set(null);return;}this.selected.set(null);this.creating.set(true);this.draft={name:''};this.tab.set('settings');this.audit.set([]);}
  protected cancelCreate(){this.creating.set(false);const team=this.teams()[0];if(team)this.select(team);}
  protected save(){const message=teamNameError(this.draft.name);if(message)return this.error.set(message);const team=this.selected();if(team&&!this.canManage())return;const request=team?this.api.update(team,this.draft.name):this.api.create(this.context(),this.draft.name);this.mutate(request,team?'Ekip kaydedildi.':'Ekip oluşturuldu.',value=>this.accept(value));}
  protected invite(){const team=this.selected(),message=emailError(this.inviteDraft.email);if(!team||!this.canManage())return;if(message)return this.error.set(message);this.mutate(this.api.invite(team,this.inviteDraft),'Ekip daveti oluşturuldu.',value=>{this.inviteDraft={email:'',role:'Member'};this.accept(value);});}
  protected changeRole(member:Team['members'][number],role:string){const team=this.selected();if(!team||!this.owner()||!member.userId||member.role==='Owner'||!['Admin','Member'].includes(role))return;this.mutate(this.api.changeRole(team,member.userId,role),'Ekip rolü güncellendi.',value=>this.accept(value));}
  protected remove(member:Team['members'][number]){const team=this.selected(),key=member.userId??member.email;if(!team||!this.canManage()||member.role==='Owner'||!confirm(`${member.email} ekipten kaldırılsın mı?`))return;this.mutate(this.api.remove(team,key),'Ekip üyesi veya daveti kaldırıldı.',value=>this.accept(value));}
  protected transfer(member:Team['members'][number]){const team=this.selected();if(!team||!this.owner()||!member.userId||member.status!=='Active'||!confirm(`Ekip sahipliği ${member.email} kullanıcısına devredilsin mi?`))return;this.mutate(this.api.transfer(team,member.userId),'Ekip sahipliği devredildi.',value=>this.accept(value));}
  protected archive(){const team=this.selected();if(!team||!this.owner()||!confirm('Bu ekip arşivlensin mi?'))return;this.mutate(this.api.archive(team),'Ekip arşivlendi.',()=>{this.selected.set(null);this.load();});}
  private accept(team:Team){this.teams.update(items=>items.some(item=>item.id===team.id)?items.map(item=>item.id===team.id?team:item):[...items,team]);this.select(team);}
  private loadAudit(team:Team){this.api.audit(team.id,this.roles(),this.context()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(value=>this.audit.set(value));}
  private mutate<T>(request:import('rxjs').Observable<T>,message:string,done:(value:T)=>void){if(this.busy())return;this.busy.set(true);this.error.set(null);request.pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:value=>{this.notice.set(message);done(value);},error:error=>this.fail(error,'İşlem tamamlanamadı.')});}
  private fail(error:any,fallback:string){this.error.set(error?.message??fallback);}
}
