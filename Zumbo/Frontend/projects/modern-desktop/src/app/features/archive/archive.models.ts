import { BoardSummary, ProjectSummary } from '../../shell/desktop-shell.models';
import { ProjectWorkItem } from '../work-items/project-work-item.models';

export type ArchiveKind = 'projects' | 'teams' | 'boards' | 'work-items';
export interface ArchivedTeam { readonly id:string;readonly name:string;readonly members:readonly unknown[];readonly version:number; }
export interface ArchiveContext { readonly organizationId:string;readonly projectId:string|null;readonly roleNames:readonly string[]; }
export interface ArchiveRole { readonly name:string;readonly permissions:readonly string[];readonly isActive:boolean; }
export interface ArchiveCollection { readonly projects:readonly ProjectSummary[];readonly teams:readonly ArchivedTeam[];readonly boards:readonly BoardSummary[];readonly workItems:readonly ProjectWorkItem[];readonly permissions:readonly string[];readonly failed:readonly ArchiveKind[]; }
export interface ArchiveGroup { readonly kind:ArchiveKind;readonly label:string;readonly items:readonly ArchiveItem[]; }
export interface ArchiveItem { readonly id:string;readonly title:string;readonly detail:string;readonly source:ProjectSummary|ArchivedTeam|BoardSummary|ProjectWorkItem; }
export interface ArchiveRestoreEvent { readonly kind:ArchiveKind;readonly id:string; }
