import { ArchiveCollection, ArchiveGroup, ArchiveItem, ArchiveKind } from './archive.models';

const labels:Readonly<Record<ArchiveKind,string>>={projects:'Projeler',teams:'Ekipler',boards:'Panolar','work-items':'İşler'};
export function archiveGroups(value:ArchiveCollection,query:string):readonly ArchiveGroup[]{
  const term=query.trim().toLocaleLowerCase('tr-TR');
  const groups:readonly [ArchiveKind,readonly ArchiveItem[]][]=[
    ['projects',value.projects.map(item=>({id:item.id,title:item.name,detail:`${item.key} · ${item.visibility??'Kurum içi'}`,source:item}))],
    ['teams',value.teams.map(item=>({id:item.id,title:item.name,detail:`${item.members.length} üye`,source:item}))],
    ['boards',value.boards.map(item=>({id:item.id,title:item.name,detail:item.type??'Pano',source:item}))],
    ['work-items',value.workItems.map(item=>({id:item.id,title:item.title,detail:`${item.type} · ${item.status} · ${item.priority}`,source:item}))]
  ];
  return groups.map(([kind,items])=>({kind,label:labels[kind],items:term?items.filter(item=>`${item.title} ${item.detail}`.toLocaleLowerCase('tr-TR').includes(term)):items}));
}
export function archiveTotal(groups:readonly ArchiveGroup[]):number{return groups.reduce((sum,group)=>sum+group.items.length,0);}
export function canRestoreArchive(kind:ArchiveKind,permissions:readonly string[]):boolean{const required:Readonly<Record<ArchiveKind,string>>={projects:'ProjectManage',teams:'TeamManage',boards:'BoardManage','work-items':'WorkItemDelete'};return permissions.includes('*')||permissions.includes(required[kind]);}
