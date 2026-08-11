import { Team,TeamMember,TeamRole,TeamUserContext } from './teams.models';
export function hasPermission(roles:readonly TeamRole[],context:TeamUserContext,permission:string){return roles.some(role=>role.isActive&&context.roleNames.includes(role.name)&&role.permissions.some(value=>value==='*'||value===permission));}
export function membership(team:Team,userId:string){return team.members.find(member=>member.userId===userId&&member.status==='Active')??null;}
export function canManageTeam(team:Team,userId:string){const role=membership(team,userId)?.role;return role==='Owner'||role==='Admin';}
export function isTeamOwner(team:Team,userId:string){return membership(team,userId)?.role==='Owner';}
export function teamNameError(value:string){const name=value.trim();if(!name)return'Ekip adı zorunludur.';if(name.length>100)return'Ekip adı 100 karakteri aşamaz.';return null;}
export function emailError(value:string){const email=value.trim();if(!email)return'E-posta adresi zorunludur.';if(!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))return'Geçerli bir e-posta adresi girin.';return null;}
export function roleLabel(value:string){return({Owner:'Sahip',Admin:'Yönetici',Member:'Üye'} as Record<string,string>)[value]??value;}
export function statusLabel(value:string){return({Active:'Aktif',Invited:'Davet edildi',Declined:'Reddedildi',Revoked:'İptal edildi',Expired:'Süresi doldu'} as Record<string,string>)[value]??value;}
const TEAM_AUDIT_LABELS: Record<string, string> = {
  TeamArchived: 'Ekip arşivlendi',
  TeamCreated: 'Ekip oluşturuldu',
  TeamInviteAccepted: 'Ekip daveti kabul edildi',
  TeamInviteDeclined: 'Ekip daveti reddedildi',
  TeamInviteExpired: 'Ekip davetinin süresi doldu',
  TeamInviteRevoked: 'Ekip daveti iptal edildi',
  TeamMemberInvited: 'Ekip üyesi davet edildi',
  TeamMemberRemoved: 'Ekip üyesi kaldırıldı',
  TeamMemberRoleChanged: 'Ekip üyesinin rolü değiştirildi',
  TeamOwnershipTransferred: 'Ekip sahipliği devredildi',
  TeamRestored: 'Ekip geri yüklendi',
  TeamUpdated: 'Ekip güncellendi'
};

export function auditLabel(value:string){
  return TEAM_AUDIT_LABELS[value] ?? value.replace(/([a-z])([A-Z])/g,'$1 $2').replace(/[._-]+/g,' ').trim();
}
export function activeCount(team:Team){return team.members.filter(member=>member.status==='Active').length;}
export function pendingCount(team:Team){return team.members.filter(member=>member.status==='Invited').length;}
