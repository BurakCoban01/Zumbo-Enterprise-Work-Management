import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonBackButton, IonContent, IonHeader, IonIcon, IonRefresher, IonRefresherContent, IonSegment, IonSegmentButton, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { arrowBackOutline, arrowForwardOutline, calendarOutline, warningOutline } from 'ionicons/icons';
import { finalize } from 'rxjs';
import { normalizeApiError, ZumboRealtimeService, ZumboSessionService } from '@zumbo/modern-shared';
import { MobileWorkspaceStore } from '../../shell/mobile-workspace.store';
import { MobileProjectHubData, MobileProjectHubTab } from './mobile-project-hub.models';
import { MobileProjectHubService } from './mobile-project-hub.service';

@Component({selector:'zumbo-mobile-project-hub',imports:[RouterLink,IonBackButton,IonContent,IonHeader,IonIcon,IonRefresher,IonRefresherContent,IonSegment,IonSegmentButton,IonTitle,IonToolbar],providers:[MobileProjectHubService],templateUrl:'./mobile-project-hub.page.html',styleUrls:['./mobile-project-hub.page.scss','./mobile-project-hub-board.scss']})
export class MobileProjectHubPage {
  private readonly route=inject(ActivatedRoute); private readonly api=inject(MobileProjectHubService); private readonly session=inject(ZumboSessionService); private readonly realtime=inject(ZumboRealtimeService); private readonly destroyRef=inject(DestroyRef);
  private loadInFlight=false;
  protected readonly store=inject(MobileWorkspaceStore); protected readonly projectId=this.route.snapshot.paramMap.get('projectId')||'';
  protected readonly project=computed(()=>this.store.projects().find(item=>item.id===this.projectId)); protected readonly tab=signal<MobileProjectHubTab>('overview');
  protected readonly data=signal<MobileProjectHubData|null>(null); protected readonly selectedStatus=signal(''); protected readonly loading=signal(true); protected readonly busy=signal<string|null>(null); protected readonly error=signal<string|null>(null);
  protected readonly statuses=computed(()=>[...(this.data()?.workflow.statuses??[])].sort((a,b)=>(a.position??0)-(b.position??0)));
  protected readonly boardItems=computed(()=>this.data()?.tasks.filter(item=>item.status===this.selectedStatus())??[]);
  protected readonly activeSprint=computed(()=>this.data()?.sprints.find(item=>item.status==='Active')??null);
  protected readonly topRisks=computed(()=>this.data()?.risks.slice(0,5)??[]);
  protected readonly canMove=computed(()=>this.hasPermission('WorkItemMove'));
  protected readonly online=signal(navigator.onLine);

  constructor(){addIcons({arrowBackOutline,arrowForwardOutline,calendarOutline,warningOutline}); const update=()=>this.online.set(navigator.onLine); window.addEventListener('online',update);window.addEventListener('offline',update);this.destroyRef.onDestroy(()=>{window.removeEventListener('online',update);window.removeEventListener('offline',update);});this.realtime.changes$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(change=>{if(change.projectId===this.projectId)this.load(undefined,false);});this.realtime.resync$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(()=>this.load(undefined,false));void this.realtime.connect(this.projectId).catch(()=>undefined);this.load();}
  protected selectTab(event:CustomEvent):void{this.tab.set((event.detail.value||'overview') as MobileProjectHubTab);}
  protected load(done?:()=>void,showLoading=true):void{if(!this.projectId||this.loadInFlight){done?.();return;}this.loadInFlight=true;if(showLoading)this.loading.set(true);this.error.set(null);void this.store.load().then(()=>{this.api.load(this.projectId).pipe(finalize(()=>{this.loadInFlight=false;this.loading.set(false);done?.();})).subscribe({next:value=>{this.data.set(value);this.realtime.synchronize(value.tasks);if(!this.statuses().some(item=>item.name===this.selectedStatus()))this.selectedStatus.set(this.statuses()[0]?.name||'');if(value.failures.length)this.error.set(`${value.failures.join(', ')} geçici olarak alınamadı.`);},error:()=>this.error.set('Proje merkezi yüklenemedi.')});}).catch(()=>{this.loadInFlight=false;this.loading.set(false);this.error.set('Proje bulunamadı.');done?.();});}
  protected refresh(event:Event):void{this.load(()=>void(event.target as unknown as{complete():Promise<void>}).complete());}
  protected setStatus(value:string):void{this.selectedStatus.set(value);}
  protected move(itemId:string,direction:-1|1):void{const snapshot=this.data();if(!snapshot||this.busy()||!this.online()||!this.canMove())return;const item=snapshot.tasks.find(value=>value.id===itemId);const index=this.statuses().findIndex(value=>value.name===item?.status);const target=this.statuses()[index+direction];if(!item||!target)return;this.busy.set(item.id);this.error.set(null);this.data.set({...snapshot,tasks:snapshot.tasks.map(value=>value.id===item.id?{...value,status:target.name}:value)});this.api.changeStatus(item.id,target.name).pipe(finalize(()=>this.busy.set(null))).subscribe({next:value=>{this.realtime.remember(value);this.data.update(current=>current?{...current,tasks:current.tasks.map(existing=>existing.id===value.id?value:existing)}:current);},error:response=>{this.data.set(snapshot);this.error.set(normalizeApiError(response).message||'İş taşınamadı; önceki durum geri yüklendi.');}});}
  protected canMoveDirection(status:string,direction:-1|1):boolean{const index=this.statuses().findIndex(item=>item.name===status);return this.canMove()&&this.online()&&!!this.statuses()[index+direction];}
  protected statusCount(status:string):number{return this.data()?.tasks.filter(item=>item.status===status).length??0;}
  protected date(value?:string|null):string{return value?new Intl.DateTimeFormat('tr-TR',{day:'2-digit',month:'short'}).format(new Date(value)):'Tarih yok';}
  protected sprintStatus(value:string):string{return({Planned:'Planlandı',Active:'Aktif',Completed:'Tamamlandı'} as Record<string,string>)[value]??value;}
  private hasPermission(permission:string):boolean{const userId=this.session.currentUser()?.id;const roleName=this.project()?.members?.find(member=>member.userId===userId)?.role;const role=this.data()?.roles.find(item=>item.name===roleName&&item.isActive);return !!role&&(role.permissions.includes('*')||role.permissions.includes(permission));}
}
