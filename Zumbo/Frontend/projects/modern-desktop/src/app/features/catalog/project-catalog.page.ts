import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { canManageProjectCatalog, canReleaseProjectCatalog, normalizeProjectComponentNames, projectCatalogErrorMessage, projectCatalogLimits } from '@zumbo/modern-shared';
import { finalize } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { ComponentDraft, MilestoneDraft, ProjectCatalogData, ProjectCatalogProject, ProjectCatalogTab, ProjectComponent, ProjectMilestone, ProjectRelease, ProjectTemplate, ProjectVersion, ReleaseDraft, TemplateDraft } from './project-catalog.models';
import { ProjectCatalogService } from './project-catalog.service';

@Component({
  selector: 'zumbo-project-catalog-page', imports: [CommonModule, ZumboIconComponent], providers: [ProjectCatalogService],
  templateUrl: './project-catalog.page.html', styleUrls: ['./project-catalog.page.scss', './project-catalog-layout.scss', './project-catalog-theme.scss', './project-catalog-responsive.scss']
})
export class ProjectCatalogPage {
  readonly project = input.required<ProjectSummary>(); readonly contextReady = input(false); readonly userId = input.required<string>();
  readonly projectChange = output<ProjectSummary>();
  private readonly destroyRef = inject(DestroyRef); private contextId = '';
  protected readonly loading = signal(true); protected readonly busy = signal(false); protected readonly error = signal<string | null>(null); protected readonly notice = signal<string | null>(null);
  protected readonly data = signal<ProjectCatalogData | null>(null); protected readonly tab = signal<ProjectCatalogTab>('releases'); protected readonly confirm = signal<{kind:string;id:string}|null>(null);
  protected readonly versionName = signal(''); protected readonly releaseDraft = signal<ReleaseDraft>({ versionId: '', name: '', scheduledAt: '' });
  protected readonly milestoneDraft = signal<MilestoneDraft>({ name: '', dueAt: '' }); protected readonly componentDraft = signal<ComponentDraft>({ name: '', description: '' });
  protected readonly templateDraft = signal<TemplateDraft>({ name: '', isDefault: false, defaultComponentNamesText: '' }); protected readonly limits = projectCatalogLimits;
  protected readonly snapshot = computed(() => catalogSnapshot(this.data()?.project));
  protected readonly role = computed(() => this.data()?.project.members?.find(member => member.userId === this.userId())?.role ?? null);
  protected readonly canManage = computed(() => canManageProjectCatalog(this.role(), this.data()?.roles));
  protected readonly canRelease = computed(() => canReleaseProjectCatalog(this.role(), this.data()?.roles));
  protected readonly componentNames = computed(() => normalizeProjectComponentNames(this.templateDraft().defaultComponentNamesText));

  constructor(private readonly catalog: ProjectCatalogService) { effect(() => { const id = this.project().id; if (!this.contextReady() || id === this.contextId) return; this.contextId = id; this.load(); }); }
  protected load(): void { this.loading.set(true); this.error.set(null); this.catalog.load(this.project().id).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: data => this.data.set(data), error: () => this.error.set('Proje kataloğu yüklenemedi.') }); }
  protected setTab(tab: ProjectCatalogTab): void { this.tab.set(tab); this.error.set(null); this.confirm.set(null); }
  protected userName(id?: string | null): string { const user = this.data()?.users.find(item => item.id === id); return user?.username || user?.email || 'Sistem işlemi'; }
  protected versionLabel(id: string): string { return (this.data()?.project.versions ?? []).find(item => item.id === id)?.name ?? 'Bilinmeyen sürüm'; }
  protected auditLabel(action: string): string { const labels: Readonly<Record<string,string>> = { ProjectTemplateCreated:'Şablon oluşturuldu',ProjectTemplateUpdated:'Şablon güncellendi',ProjectTemplateArchived:'Şablon arşivlendi',ProjectComponentCreated:'Bileşen oluşturuldu',ProjectComponentUpdated:'Bileşen güncellendi',ProjectComponentArchived:'Bileşen arşivlendi',ProjectVersionCreated:'Sürüm oluşturuldu',ProjectVersionArchived:'Sürüm arşivlendi',ProjectReleaseCreated:'Yayın taslağı oluşturuldu',ProjectReleaseApproved:'Yayın onaylandı',ProjectReleasePublished:'Yayınlandı',ProjectMilestoneCreated:'Kilometre taşı oluşturuldu',ProjectMilestoneUpdated:'Kilometre taşı güncellendi',ProjectMilestoneCompleted:'Kilometre taşı tamamlandı' }; return labels[action] ?? 'Teslimat kaydı güncellendi'; }
  protected requestConfirm(kind: string, id: string): void { this.confirm.set({kind,id}); }
  protected isConfirm(kind: string,id: string): boolean { const value=this.confirm(); return value?.kind===kind&&value.id===id; }
  protected editTemplate(value: ProjectTemplate): void { this.templateDraft.set({ id:value.id,name:value.name,isDefault:value.isDefault,defaultComponentNamesText:value.defaultComponentNames.join('\n') }); }
  protected editComponent(value: ProjectComponent): void { this.componentDraft.set({id:value.id,name:value.name,description:value.description??''}); }
  protected editMilestone(value: ProjectMilestone): void { this.milestoneDraft.set({id:value.id,name:value.name,dueAt:dateInput(value.dueAt)}); }
  protected updateTemplate(field: keyof TemplateDraft,event: Event): void { const target=event.target as HTMLInputElement|HTMLTextAreaElement; this.templateDraft.update(value=>({...value,[field]:field==='isDefault'?(target as HTMLInputElement).checked:target.value})); }
  protected updateComponent(field: keyof ComponentDraft,event: Event): void { this.componentDraft.update(value=>({...value,[field]:(event.target as HTMLInputElement|HTMLTextAreaElement).value})); }
  protected updateMilestone(field: keyof MilestoneDraft,event: Event): void { this.milestoneDraft.update(value=>({...value,[field]:(event.target as HTMLInputElement).value})); }
  protected updateRelease(field: keyof ReleaseDraft,event: Event): void { this.releaseDraft.update(value=>({...value,[field]:(event.target as HTMLInputElement|HTMLSelectElement).value})); }
  protected saveTemplate(): void { const draft=this.templateDraft(), state=this.componentNames(); if(!draft.name.trim()||state.tooMany||state.tooLong)return; this.mutate(this.catalog.saveTemplate(this.project().id,draft,state.values),draft.id?'Şablon güncellendi.':'Şablon oluşturuldu.',()=>this.templateDraft.set({name:'',isDefault:false,defaultComponentNamesText:''}),'Şablon kaydedilemedi.'); }
  protected saveComponent(): void { const draft=this.componentDraft(); if(!draft.name.trim())return; this.mutate(this.catalog.saveComponent(this.project().id,draft),draft.id?'Bileşen güncellendi.':'Bileşen oluşturuldu.',()=>this.componentDraft.set({name:'',description:''}),'Bileşen kaydedilemedi.'); }
  protected createVersion(): void { if(!this.versionName().trim())return; this.mutate(this.catalog.createVersion(this.project().id,this.versionName()),'Sürüm oluşturuldu.',()=>this.versionName.set(''),'Sürüm oluşturulamadı.'); }
  protected createRelease(): void { const draft=this.releaseDraft(); if(!draft.versionId||!draft.name.trim())return; this.mutate(this.catalog.createRelease(this.project().id,draft),'Yayın taslağı oluşturuldu.',()=>this.releaseDraft.set({versionId:'',name:'',scheduledAt:''}),'Yayın taslağı oluşturulamadı.'); }
  protected saveMilestone(): void { const draft=this.milestoneDraft(); if(!draft.name.trim()||!draft.dueAt)return; this.mutate(this.catalog.saveMilestone(this.project().id,draft),draft.id?'Kilometre taşı güncellendi.':'Kilometre taşı oluşturuldu.',()=>this.milestoneDraft.set({name:'',dueAt:''}),'Kilometre taşı kaydedilemedi.'); }
  protected archiveTemplate(value:ProjectTemplate):void{this.mutate(this.catalog.archiveTemplate(this.project().id,value.id),'Şablon arşivlendi.',()=>this.templateDraft.set({name:'',isDefault:false,defaultComponentNamesText:''}),'Şablon arşivlenemedi.');}
  protected archiveComponent(value:ProjectComponent):void{this.mutate(this.catalog.archiveComponent(this.project().id,value.id),'Bileşen arşivlendi.',()=>this.componentDraft.set({name:'',description:''}),'Bileşen arşivlenemedi.');}
  protected archiveVersion(value:ProjectVersion):void{this.mutate(this.catalog.archiveVersion(this.project().id,value.id),'Sürüm arşivlendi.',()=>this.versionName.set(''),'Sürüm arşivlenemedi.');}
  protected approveRelease(value:ProjectRelease):void{this.mutate(this.catalog.approveRelease(this.project().id,value.id),'Yayın onaylandı.',()=>{},'Yayın onaylanamadı.');}
  protected publishRelease(value:ProjectRelease):void{this.mutate(this.catalog.publishRelease(this.project().id,value.id),'Yayınlandı ve sürüm tamamlandı.',()=>{},'Yayınlanamadı.');}
  protected completeMilestone(value:ProjectMilestone):void{this.mutate(this.catalog.completeMilestone(this.project().id,value.id),'Kilometre taşı tamamlandı.',()=>this.milestoneDraft.set({name:'',dueAt:''}),'Kilometre taşı tamamlanamadı.');}
  protected refreshAudit():void{this.catalog.refreshAudit(this.project().id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(audit=>this.data.update(data=>data?{...data,audit}:data));}
  private mutate(request: ReturnType<ProjectCatalogService['createVersion']>, message:string,reset:()=>void,fallback:string):void{if(this.busy()||!this.canManage())return;this.busy.set(true);this.error.set(null);this.notice.set(null);this.confirm.set(null);request.pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:project=>{this.data.update(data=>data?{...data,project}:data);this.projectChange.emit(project);reset();this.notice.set(message);this.refreshAudit();},error:error=>{this.error.set(projectCatalogErrorMessage(error,fallback));if(error?.code==='CONCURRENCY_CONFLICT')this.load();}});}
}
function dateInput(value:string):string{const date=new Date(value);if(Number.isNaN(date.getTime()))return'';const local=new Date(date.getTime()-date.getTimezoneOffset()*60000);return local.toISOString().slice(0,16);}
function catalogSnapshot(project:ProjectCatalogProject|null|undefined){const current=project??{templates:[],components:[],versions:[],releases:[],milestones:[]};return{templates:[...current.templates],activeTemplates:current.templates.filter(item=>!item.archived),components:[...current.components],activeComponents:current.components.filter(item=>!item.archived),versions:[...current.versions],plannedVersions:current.versions.filter(item=>item.status==='Planned'),releases:[...current.releases],milestones:[...current.milestones].sort((a,b)=>Date.parse(a.dueAt)-Date.parse(b.dueAt)),openMilestones:current.milestones.filter(item=>item.status==='Open')};}
