export interface MobileTeam{readonly id:string;readonly organizationId:string;readonly name:string;readonly members:readonly MobileTeamMember[];readonly archived:boolean;readonly version:number;}
export interface MobileTeamMember{readonly id:string;readonly userId?:string|null;readonly email:string;readonly role:string;readonly status:string;}
export interface MobileTeamRole{readonly name:string;readonly permissions:readonly string[];readonly isActive:boolean;}
export interface MobileTeamAudit{readonly id:string;readonly action:string;readonly createdAt:string;}
export interface MobileTeamContext{readonly teams:readonly MobileTeam[];readonly roles:readonly MobileTeamRole[];}
