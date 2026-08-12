import { ProjectSummary } from '../../shell/desktop-shell.models';

export type ProjectDirectoryMode = 'mine' | 'favorites' | 'recent' | 'all';
export type ProjectDirectorySort = 'name' | 'key' | 'recent';

export interface ProjectRoleSummary {
  readonly name: string;
  readonly displayName: string;
  readonly isActive: boolean;
  readonly isProtected: boolean;
  readonly permissions: readonly string[];
}

export interface UpdateProjectRequest {
  readonly name: string;
  readonly visibility: string;
}

export type ProjectUpdate = ProjectSummary;
