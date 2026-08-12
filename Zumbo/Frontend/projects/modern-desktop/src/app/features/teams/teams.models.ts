export type TeamTab='members'|'activity'|'settings';
export interface Team { readonly id:string;readonly organizationId:string;readonly name:string;readonly members:readonly TeamMember[];readonly archived:boolean;readonly version:number; }
export interface TeamMember { readonly id:string;readonly userId?:string|null;readonly email:string;readonly role:string;readonly status:string;readonly invitationExpiresAt?:string|null;readonly respondedAt?:string|null; }
export interface TeamRole { readonly name:string;readonly displayName?:string;readonly permissions:readonly string[];readonly isActive:boolean; }
export interface TeamAudit { readonly id:string;readonly actorUserId:string;readonly action:string;readonly entityType:string;readonly entityId:string;readonly createdAt:string; }
export interface TeamDraft { name:string; }
export interface TeamInviteDraft { email:string;role:'Admin'|'Member'; }
export interface TeamUserContext { readonly id:string;readonly organizationId:string;readonly roleNames:readonly string[]; }
