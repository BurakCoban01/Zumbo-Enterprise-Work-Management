export type AutomationTab = 'rules' | 'runs' | 'schedules' | 'templates' | 'activity';

export interface AutomationRole { readonly name: string; readonly permissions: readonly string[]; readonly isActive: boolean; }
export interface AutomationUser { readonly id: string; readonly username?: string | null; readonly email?: string | null; }
export interface AutomationIssueType { readonly key: string; readonly name: string; readonly active: boolean; readonly position: number; }
export interface AutomationSchema { readonly issueTypes: readonly AutomationIssueType[]; }
export interface AutomationPage<T> { readonly items: readonly T[]; readonly page: number; readonly pageSize: number; readonly total?: number; readonly totalCount?: number; }

export interface AutomationAction { readonly type: string; readonly value?: string | null; }
export interface AutomationCondition { readonly kind: string; readonly field?: string | null; readonly operator?: string | null; readonly value?: string | null; readonly children: readonly AutomationCondition[]; }
export interface AutomationTrigger { readonly type: 'Event' | 'Schedule'; readonly eventType?: string | null; readonly intervalMinutes?: number | null; readonly startAtUtc?: string | null; }
export interface AutomationRuleDefinition { readonly number: number; readonly state: string; readonly name: string; readonly description?: string | null; readonly trigger: AutomationTrigger; readonly condition?: AutomationCondition | null; readonly actions: readonly AutomationAction[]; readonly maximumExecutionsPerHour: number; readonly maximumChainDepth: number; }
export interface AutomationRuleSummary { readonly id: string; readonly projectId: string; readonly name: string; readonly triggerType: string; readonly eventType?: string | null; readonly active: boolean; readonly archived: boolean; readonly nextRunAtUtc?: string | null; readonly publishedVersion: number; readonly hasDraft: boolean; readonly version: number; }
export interface AutomationRule extends Omit<AutomationRuleSummary, 'name' | 'triggerType' | 'eventType'> { readonly definition?: AutomationRuleDefinition | null; }
export interface AutomationRuleDraft { id?: string | null; name: string; description: string; triggerType: 'Event' | 'Schedule'; eventType: string; intervalMinutes: number; startAtLocal: string; conditionMode: 'All' | 'Any'; conditions: AutomationConditionDraft[]; actions: AutomationActionDraft[]; maximumExecutionsPerHour: number; maximumChainDepth: number; }
export interface AutomationConditionDraft { field: string; operator: string; value: string; }
export interface AutomationActionDraft { type: string; value: string; }
export interface AutomationDryRun { readonly ruleId: string; readonly ruleVersion: number; readonly triggerMatched: boolean; readonly conditionMatched: boolean; readonly plannedActions: readonly AutomationAction[]; readonly outcome: string; }

export interface AutomationRunStep { readonly index: number; readonly actionType: string; readonly status: string; readonly attempt: number; readonly failureCategory?: string | null; readonly completedAtUtc?: string | null; }
export interface AutomationRun { readonly id: string; readonly projectId: string; readonly ruleId: string; readonly ruleVersion: number; readonly ruleName: string; readonly triggerType: string; readonly eventType?: string | null; readonly sourceId?: string | null; readonly chainDepth: number; readonly status: string; readonly outcome: string; readonly attempt: number; readonly maximumAttempts: number; readonly failureCategory?: string | null; readonly createdAtUtc: string; readonly nextAttemptAtUtc?: string | null; readonly steps: readonly AutomationRunStep[]; readonly version: number; }

export interface WorkTemplate { readonly id: string; readonly projectId: string; readonly boardId: string; readonly name: string; readonly title: string; readonly description: string; readonly type: string; readonly priority: string; readonly assigneeUserId?: string | null; readonly teamId?: string | null; readonly dueAfterDays?: number | null; readonly labels: readonly string[]; readonly customFields: readonly unknown[]; readonly archived: boolean; readonly version: number; }
export interface WorkTemplateDraft { id?: string | null; version?: number; boardId: string; name: string; title: string; description: string; type: string; priority: string; assigneeUserId: string; dueAfterDays: number | null; labelsText: string; customFields: readonly unknown[]; }
export interface WorkRecurrence { readonly id: string; readonly projectId: string; readonly templateId: string; readonly frequency: string; readonly interval: number; readonly startAtUtc: string; readonly endAtUtc?: string | null; readonly nextRunAtUtc?: string | null; readonly maxOccurrences: number; readonly scheduledOccurrences: number; readonly generatedOccurrences: number; readonly active: boolean; readonly archived: boolean; readonly version: number; }
export interface WorkRecurrenceDraft { templateId: string; frequency: string; interval: number; startAtLocal: string; endAtLocal: string; maxOccurrences: number; }
export interface WorkRecurrencePreview { readonly frequency: string; readonly interval: number; readonly occurrencesUtc: readonly string[]; }
export interface WorkRecurrenceOccurrence { readonly id: string; readonly scheduledForUtc: string; readonly status: string; readonly createdWorkItemId?: string | null; readonly version: number; }
export interface AutomationAudit { readonly id: string; readonly action: string; readonly actorUserId?: string | null; readonly createdAt: string; }
export interface AutomationContext { readonly rules: readonly AutomationRuleSummary[]; readonly runs: readonly AutomationRun[]; readonly templates: readonly WorkTemplate[]; readonly recurrences: readonly WorkRecurrence[]; readonly roles: readonly AutomationRole[]; readonly users: readonly AutomationUser[]; readonly schema: AutomationSchema; }
