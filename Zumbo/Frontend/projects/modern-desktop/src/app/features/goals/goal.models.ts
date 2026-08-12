export type GoalTab = 'key-results' | 'updates' | 'sources' | 'definition';
export interface GoalUser { readonly id: string; readonly username?: string | null; readonly email?: string | null; }
export interface GoalPageResponse { readonly items: readonly Goal[]; readonly totalCount: number; }
export interface GoalLink { readonly portfolioId: string; readonly initiativeId: string; }
export interface Goal {
  readonly id:string; readonly ownerUserId:string; readonly name:string; readonly description?:string|null; readonly periodStart:string; readonly periodEnd:string;
  readonly status:string; readonly health:string; readonly confidence?:number|null; readonly progress:number; readonly viewerUserIds:readonly string[];
  readonly initiativeLinks:readonly GoalLink[]; readonly projectIds:readonly string[]; readonly keyResults:readonly KeyResult[]; readonly statusUpdates:readonly GoalStatusUpdate[];
  readonly canEdit:boolean; readonly canUpdateStatus:boolean; readonly archived:boolean; readonly updatedAt:string; readonly version:number; readonly statusUpdateRetentionLimit:number;
}
export interface KeyResult { readonly id:string; readonly ownerUserId:string; readonly name:string; readonly description?:string|null; readonly baselineValue:number; readonly targetValue:number; readonly currentValue:number; readonly unit:string; readonly direction:string; readonly progress:number; readonly confidence?:number|null; readonly progressUpdates:readonly KeyResultUpdate[]; readonly canUpdate:boolean; readonly progressUpdateRetentionLimit:number; }
export interface KeyResultUpdate { readonly id:string; readonly previousValue:number; readonly currentValue:number; readonly confidence?:number|null; readonly note:string; readonly authorUserId:string; readonly createdAt:string; }
export interface GoalStatusUpdate { readonly id:string; readonly status:string; readonly health:string; readonly confidence?:number|null; readonly note:string; readonly authorUserId:string; readonly createdAt:string; }
export interface GoalRollup { readonly goalId:string; readonly sourceStatus:string; readonly progress:number; readonly confidence?:number|null; readonly generatedAt:string; readonly initiatives:readonly GoalInitiativeSource[]; readonly projects:readonly GoalProjectSource[]; readonly unavailableSources:readonly string[]; }
export interface GoalInitiativeSource { readonly portfolioId:string; readonly id:string; readonly name:string; readonly status:string; readonly health:string; readonly confidence?:number|null; }
export interface GoalProjectSource { readonly id:string; readonly key:string; readonly name:string; }
export interface GoalPortfolio { readonly id:string; readonly name:string; readonly initiatives:readonly {readonly id:string; readonly name:string}[]; }
export interface InitiativeOption extends GoalLink { readonly key:string; readonly label:string; }
export interface GoalDraft { id?:string; name:string; description:string; periodStart:string; periodEnd:string; viewerUserIds:string[]; initiativeKeys:string[]; projectIds:string[]; version?:number; }
export interface KeyResultDraft { id?:string; name:string; description:string; ownerUserId:string; baselineValue:number; targetValue:number; initialValue:number; unit:string; direction:string; }
export interface GoalStatusDraft { status:string; health:string; confidence:number|null; note:string; }
export interface KeyResultProgressDraft { currentValue:number; confidence:number|null; note:string; }
