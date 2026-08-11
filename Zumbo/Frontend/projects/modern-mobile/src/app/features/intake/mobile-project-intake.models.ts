export type MobileProjectIntakeTab = 'forms' | 'submit' | 'triage';
export type MobileProjectIntakeFieldType = 'Text' | 'LongText' | 'Email' | 'Number' | 'Date' | 'Choice' | 'Checkbox' | 'Attachment';

export interface MobileProjectIntakeRole { readonly name: string; readonly permissions: readonly string[]; readonly isActive: boolean; }
export interface MobileProjectIntakeProject { readonly id: string; readonly key: string; readonly name: string; readonly members?: readonly { readonly userId: string; readonly role: string }[]; }
export interface MobileProjectIntakeBoard { readonly id: string; readonly name: string; }
export interface MobileProjectIntakeSchemaItem { readonly key: string; readonly name: string; readonly active?: boolean; readonly position: number; }
export interface MobileProjectIntakeSchema { readonly issueTypes: readonly MobileProjectIntakeSchemaItem[]; readonly customFields?: readonly MobileProjectIntakeSchemaItem[]; }

export interface MobileProjectIntakeField { readonly key: string; readonly label: string; readonly type: MobileProjectIntakeFieldType; readonly required: boolean; readonly helpText?: string | null; readonly options: readonly string[]; }
export interface MobileProjectIntakeMappingItem { readonly intakeFieldKey: string; readonly workItemFieldKey: string; }
export interface MobileProjectIntakeMapping { readonly titleFieldKey: string; readonly descriptionFieldKey?: string | null; readonly priorityFieldKey?: string | null; readonly dueDateFieldKey?: string | null; readonly customFields: readonly MobileProjectIntakeMappingItem[]; }
export interface MobileProjectIntakeDefinition { readonly accessPolicy: 'Internal' | 'Public'; readonly boardId: string; readonly workItemType: string; readonly defaultPriority: string; readonly confirmationMessage: string; readonly fields: readonly MobileProjectIntakeField[]; readonly mapping: MobileProjectIntakeMapping; }
export interface MobileProjectIntakeForm { readonly id: string; readonly projectId: string; readonly name: string; readonly description: string; readonly state: 'Draft' | 'Published' | 'Archived'; readonly publicId?: string | null; readonly publishedVersion: number; readonly draft: MobileProjectIntakeDefinition; readonly version: number; }
export interface MobileProjectIntakePublishedForm { readonly formId: string; readonly version: number; readonly name: string; readonly description: string; readonly accessPolicy: string; readonly confirmationMessage: string; readonly fields: readonly MobileProjectIntakeField[]; }

export interface MobileProjectIntakeFieldDraft { key: string; label: string; type: MobileProjectIntakeFieldType; required: boolean; helpText: string; optionsText: string; }
export interface MobileProjectIntakeDraft { id?: string | null; projectId: string; name: string; description: string; state: string; definition: { accessPolicy: 'Internal' | 'Public'; boardId: string; workItemType: string; defaultPriority: string; confirmationMessage: string; fields: MobileProjectIntakeFieldDraft[]; mapping: { titleFieldKey: string; descriptionFieldKey: string; priorityFieldKey: string; dueDateFieldKey: string; customFields: MobileProjectIntakeMappingItem[]; }; }; }
export interface MobileProjectIntakeSubmissionModel { values: Record<string, string | boolean>; files: Record<string, readonly File[]>; website: string; }
export interface MobileProjectIntakeConfirmation { readonly submissionId: string; readonly confirmationCode: string; readonly message: string; readonly state: string; readonly workItemId?: string | null; }
export interface MobileProjectIntakeSubmission { readonly id: string; readonly formId: string; readonly formVersion: number; readonly state: string; readonly confirmationCode: string; readonly workItemId?: string | null; readonly values: readonly { readonly fieldKey: string; readonly value?: string | null; }[]; readonly attachments: readonly { readonly id: string; readonly fileName: string; readonly securityState: string; }[]; readonly triageNote?: string | null; readonly createdAt: string; readonly version: number; }
export interface MobileProjectIntakePage<T> { readonly items: readonly T[]; readonly totalCount: number; readonly page: number; readonly pageSize: number; }
export interface MobileProjectIntakeContext { readonly project: MobileProjectIntakeProject; readonly boards: readonly MobileProjectIntakeBoard[]; readonly roles: readonly MobileProjectIntakeRole[]; readonly schema: MobileProjectIntakeSchema; readonly forms: readonly MobileProjectIntakeForm[]; }
