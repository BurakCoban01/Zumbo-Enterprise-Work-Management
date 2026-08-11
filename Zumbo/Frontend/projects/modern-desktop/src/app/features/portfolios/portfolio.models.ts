export type PortfolioTab = 'roadmap' | 'initiatives' | 'updates' | 'dependencies';

export interface PortfolioUser { readonly id: string; readonly username?: string | null; readonly email?: string | null; }
export interface PortfolioPageResponse { readonly items: readonly Portfolio[]; readonly page: number; readonly pageSize: number; readonly totalCount: number; }
export interface Portfolio {
  readonly id: string; readonly ownerUserId: string; readonly name: string; readonly description?: string | null;
  readonly viewerUserIds: readonly string[]; readonly initiatives: readonly Initiative[]; readonly dependencies: readonly PortfolioDependency[];
  readonly canEdit: boolean; readonly archived: boolean; readonly updatedAt: string; readonly version: number;
}
export interface Initiative {
  readonly id: string; readonly name: string; readonly summary?: string | null; readonly parentInitiativeId?: string | null;
  readonly ownerUserId: string; readonly status: string; readonly health: string; readonly confidence?: number | null;
  readonly targetAt?: string | null; readonly projectIds: readonly string[]; readonly milestoneLinks: readonly MilestoneLink[];
  readonly statusUpdates: readonly InitiativeStatusUpdate[]; readonly canUpdateStatus: boolean; readonly statusUpdateRetentionLimit: number;
}
export interface MilestoneLink { readonly projectId: string; readonly milestoneId: string; }
export interface InitiativeStatusUpdate { readonly id: string; readonly status: string; readonly health: string; readonly confidence?: number | null; readonly note: string; readonly authorUserId: string; readonly createdAt: string; }
export interface PortfolioDependency { readonly id: string; readonly sourceProjectId: string; readonly targetProjectId: string; readonly description: string; readonly status: string; readonly requiredBy?: string | null; }
export interface PortfolioRoadmap {
  readonly portfolioId: string; readonly sourceStatus: string; readonly generatedAt: string; readonly unavailableProjectIds: readonly string[];
  readonly initiatives: readonly RoadmapInitiative[]; readonly dependencies: readonly PortfolioDependency[];
}
export interface RoadmapInitiative {
  readonly id: string; readonly name: string; readonly parentInitiativeId?: string | null; readonly ownerUserId: string;
  readonly status: string; readonly health: string; readonly confidence?: number | null; readonly targetAt?: string | null;
  readonly totalWorkItems: number; readonly completedWorkItems: number; readonly overdueWorkItems: number; readonly progress: number;
  readonly projects: readonly RoadmapProject[];
}
export interface RoadmapProject { readonly id: string; readonly key: string; readonly name: string; readonly totalWorkItems: number; readonly completedWorkItems: number; readonly overdueWorkItems: number; readonly progress: number; }
export interface PortfolioDraft { id?: string; name: string; description: string; viewerUserIds: string[]; version?: number; }
export interface InitiativeDraft { id?: string; name: string; summary: string; parentInitiativeId: string; ownerUserId: string; status: string; health: string; confidence: number | null; targetAt: string; projectIds: string[]; }
export interface StatusDraft { status: string; health: string; confidence: number | null; note: string; }
export interface DependencyDraft { id?: string; sourceProjectId: string; targetProjectId: string; description: string; status: string; requiredBy: string; }
export interface InitiativeTreeRow { readonly item: Initiative; readonly depth: number; }
