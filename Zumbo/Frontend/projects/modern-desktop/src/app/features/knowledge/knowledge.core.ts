import { ProjectSummary } from '../../shell/desktop-shell.models';
import { KnowledgeBlock,KnowledgeDocument,KnowledgeDraft,KnowledgePortfolio,KnowledgeRole,KnowledgeScope,KnowledgeSegment } from './knowledge.models';

export function knowledgeScopes(projects: readonly ProjectSummary[],portfolios: readonly KnowledgePortfolio[],userId: string,roles: readonly KnowledgeRole[]): KnowledgeScope[] {
  const result: KnowledgeScope[]=[];
  for(const project of projects){
    const roleName=project.members?.find(member=>member.userId===userId)?.role;
    const role=roles.find(item=>item.isActive&&item.name===roleName);
    if(role?.permissions.some(permission=>permission==='*'||permission==='BoardManage'))result.push({key:`Project:${project.id}`,type:'Project',id:project.id,label:`${project.key} · ${project.name}`,projectIds:[project.id]});
  }
  for(const portfolio of portfolios)for(const initiative of portfolio.initiatives){
    if(portfolio.canEdit||initiative.canUpdateStatus||initiative.ownerUserId===userId)result.push({key:`Initiative:${initiative.id}`,type:'Initiative',id:initiative.id,label:`${portfolio.name} · ${initiative.name}`,projectIds:initiative.projectIds});
  }
  return result;
}

export function knowledgeDraft(scope?: KnowledgeScope,item?: KnowledgeDocument): KnowledgeDraft {
  return {id:item?.id,scopeKey:item?`${item.scopeType}:${item.scopeId}`:scope?.key??'',scopeType:item?.scopeType??scope?.type??'',scopeId:item?.scopeId??scope?.id??'',title:item?.title??'',contentMarkdown:item?.contentMarkdown??'',tagsText:item?.tags.join(', ')??'',workItemIds:[...(item?.workItemIds??[])],userIds:[...(item?.userIds??[])],changeSummary:'',version:item?.version};
}

export function knowledgeError(draft: KnowledgeDraft,scope: KnowledgeScope|null): string|null {
  const title=draft.title.trim();
  if(!scope)return'Proje veya initiative kapsamı seçin.';
  if(!title)return'Doküman başlığı gereklidir.';
  if(title.length>160)return'Doküman başlığı 160 karakteri aşamaz.';
  if(draft.contentMarkdown.length>40000)return'İçerik 40.000 karakteri aşamaz.';
  if(!draft.changeSummary.trim())return'Sürüm özeti gereklidir.';
  if(draft.workItemIds.length>50)return'En fazla 50 iş bağlanabilir.';
  if(draft.userIds.length>30)return'En fazla 30 kullanıcı bağlanabilir.';
  return null;
}

export function tags(value:string): string[] { const seen=new Set<string>();return value.split(',').map(item=>item.trim()).filter(item=>{const key=item.toLocaleLowerCase('tr-TR');if(!item||seen.has(key))return false;seen.add(key);return true;}); }
export function safeLink(value:string): string|null { const normalized=value.trim();if(normalized.startsWith('/')||normalized.startsWith('#'))return normalized;try{const url=new URL(normalized);return url.protocol==='http:'||url.protocol==='https:'?normalized:null;}catch{return null;} }

export function parseMarkdown(value:string): KnowledgeBlock[] {
  const lines=value.replace(/\r\n?/g,'\n').split('\n'),blocks:KnowledgeBlock[]=[];let index=0;
  while(index<lines.length){const line=lines[index];if(!line.trim()){index++;continue;}
    if(line.startsWith('```')){const language=line.slice(3).trim(),code:string[]=[];index++;while(index<lines.length&&!lines[index].startsWith('```'))code.push(lines[index++]);if(index<lines.length)index++;blocks.push({type:'code',language,text:code.join('\n')});continue;}
    const heading=line.match(/^(#{1,3})\s+(.+)$/);if(heading){blocks.push({type:'heading',level:heading[1].length,segments:parseInline(heading[2])});index++;continue;}
    const list=line.match(/^(\s*)([-*]|\d+\.)\s+(.+)$/);if(list){const ordered=/\d+\./.test(list[2]),items:KnowledgeSegment[][]=[];while(index<lines.length){const item=lines[index].match(/^(\s*)([-*]|\d+\.)\s+(.+)$/);if(!item||/\d+\./.test(item[2])!==ordered)break;items.push(parseInline(item[3]));index++;}blocks.push({type:'list',ordered,items});continue;}
    if(/^>\s?/.test(line)){blocks.push({type:'quote',segments:parseInline(line.replace(/^>\s?/,''))});index++;continue;}
    const paragraph=[line.trim()];index++;while(index<lines.length&&lines[index].trim()&&!/^(#{1,3})\s+|^```|^(\s*)([-*]|\d+\.)\s+|^>\s?/.test(lines[index]))paragraph.push(lines[index++].trim());blocks.push({type:'paragraph',segments:parseInline(paragraph.join(' '))});
  }return blocks;
}

function parseInline(value:string): KnowledgeSegment[] { const result:KnowledgeSegment[]=[],pattern=/(\[([^\]]+)\]\(([^)\s]+)\)|`([^`]+)`|\*\*([^*]+)\*\*)/g;let cursor=0,match:RegExpExecArray|null;while((match=pattern.exec(value))){if(match.index>cursor)result.push({type:'text',text:value.slice(cursor,match.index)});if(match[2]){const href=safeLink(match[3]);result.push(href?{type:'link',text:match[2],href}:{type:'text',text:match[2]});}else if(match[4])result.push({type:'code',text:match[4]});else result.push({type:'strong',text:match[5]});cursor=pattern.lastIndex;}if(cursor<value.length)result.push({type:'text',text:value.slice(cursor)});return result; }
