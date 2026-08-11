export type OrganizationAccessMode='organization'|'access';
export interface DepartmentMember{readonly userId:string;readonly position:string}
export interface Department{readonly id:string;name:string;parentDepartmentId?:string|null;readonly members:readonly DepartmentMember[]}
export interface Organization{readonly id:string;readonly tenantKey:string;readonly ownerUserId:string;name:string;readonly departments:readonly Department[];readonly status:string;readonly version:number}
export interface SettingsUser{readonly id:string;readonly username:string;readonly email:string;readonly organizationId:string;readonly roles:readonly string[];readonly version:number}
export interface AuditEntry{readonly id?:string;readonly action:string;readonly createdAt:string}
