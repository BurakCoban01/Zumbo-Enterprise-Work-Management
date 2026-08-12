import { Component,computed,effect,inject,input,output,signal,untracked } from '@angular/core';
import { finalize } from 'rxjs';
import { ZumboIconComponent } from '../../shell/zumbo-icon.component';
import { archiveGroups,archiveTotal,canRestoreArchive } from './archive.core';
import { ArchiveCollection,ArchiveContext,ArchiveRestoreEvent } from './archive.models';
import { ArchiveService } from './archive.service';

@Component({selector:'zumbo-archive-page',imports:[ZumboIconComponent],providers:[ArchiveService],templateUrl:'./archive.page.html',styleUrls:['./archive.page.scss','./archive-responsive.scss']})
export class ArchivePage{
  private readonly api=inject(ArchiveService);
  readonly context=input.required<ArchiveContext>();
  readonly restored=output<ArchiveRestoreEvent>();
  protected readonly collection=signal<ArchiveCollection|null>(null);
  protected readonly loading=signal(true);protected readonly busyId=signal<string|null>(null);protected readonly error=signal<string|null>(null);protected readonly notice=signal<string|null>(null);protected readonly query=signal('');
  protected readonly groups=computed(()=>archiveGroups(this.collection()??{projects:[],teams:[],boards:[],workItems:[],permissions:[],failed:[]},this.query()));
  protected readonly total=computed(()=>archiveTotal(this.groups()));
  constructor(){effect(()=>{this.context();untracked(()=>this.load());});}
  protected load():void{this.loading.set(true);this.error.set(null);this.api.load(this.context()).pipe(finalize(()=>this.loading.set(false))).subscribe({next:value=>this.collection.set(value),error:()=>this.error.set('Arşiv şu anda yüklenemedi.')});}
  protected restore(event:ArchiveRestoreEvent):void{if(this.busyId())return;this.busyId.set(event.id);this.notice.set(null);this.api.restore(event.kind,event.id).pipe(finalize(()=>this.busyId.set(null))).subscribe({next:()=>{this.notice.set('Kayıt yeniden etkinleştirildi.');this.restored.emit(event);this.load();},error:()=>this.error.set('Kayıt geri yüklenemedi.')});}
  protected canRestore(kind:ArchiveRestoreEvent['kind']):boolean{return canRestoreArchive(kind,this.collection()?.permissions??[]);}
}
