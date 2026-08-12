export type IntakeTab = 'forms' | 'submit' | 'triage';
export type IntakeFieldType = 'Text' | 'LongText' | 'Email' | 'Number' | 'Date' | 'Choice' | 'Checkbox' | 'Attachment';

export interface IntakeRole {
  readonly name: string;
  readonly displayName: string;
  readonly permissions: readonly string[];
  readonly isActive: boolean;
}

export interface IntakeIssueType { readonly key: string; readonly name: string; readonly active: boolean; readonly position: number; }
export interface IntakeCustomField { readonly key: string; readonly name: string; readonly active?: boolean; readonly position: number; }
export interface IntakeSchema { readonly issueTypes: readonly IntakeIssueType[]; readonly customFields?: readonly IntakeCustomField[]; }
export interface IntakeField { readonly key: string; readonly label: string; readonly type: IntakeFieldType; readonly required: boolean; readonly helpText?: string | null; readonly options: readonly string[]; }
export interface IntakeCustomMapping { readonly intakeFieldKey: string; readonly workItemFieldKey: string; }
export interface IntakeMapping { readonly titleFieldKey: string; readonly descriptionFieldKey?: string | null; readonly priorityFieldKey?: string | null; readonly dueDateFieldKey?: string | null; readonly customFields: readonly IntakeCustomMapping[]; }
export interface IntakeDefinition { readonly accessPolicy: 'Internal' | 'Public'; readonly boardId: string; readonly workItemType: string; readonly defaultPriority: string; readonly confirmationMessage: string; readonly fields: readonly IntakeField[]; readonly mapping: IntakeMapping; }
export interface IntakeForm { readonly id: string; readonly projectId: string; readonly name: string; readonly description: string; readonly state: 'Draft' | 'Published' | 'Archived'; readonly publicId?: string | null; readonly publishedVersion: number; readonly draft: IntakeDefinition; readonly version: number; }
export interface PublishedIntakeForm { readonly formId: string; readonly version: number; readonly name: string; readonly description: string; readonly accessPolicy: string; readonly confirmationMessage: string; readonly fields: readonly IntakeField[]; }

export interface IntakeFieldDraft { key: string; label: string; type: IntakeFieldType; required: boolean; helpText: string; optionsText: string; }
export interface IntakeMappingDraft { titleFieldKey: string; descriptionFieldKey: string; priorityFieldKey: string; dueDateFieldKey: string; customFields: IntakeCustomMapping[]; }
export interface IntakeDefinitionDraft { accessPolicy: 'Internal' | 'Public'; boardId: string; workItemType: string; defaultPriority: string; confirmationMessage: string; fields: IntakeFieldDraft[]; mapping: IntakeMappingDraft; }
export interface IntakeFormDraft { id?: string | null; projectId: string; name: string; description: string; state: string; definition: IntakeDefinitionDraft; }
export interface IntakeSubmissionModel { values: Record<string, string | boolean>; files: Record<string, readonly File[]>; website: string; }
export interface IntakeSubmissionConfirmation { readonly submissionId: string; readonly confirmationCode: string; readonly message: string; readonly state: string; readonly workItemId?: string | null; }
export interface IntakeSubmissionValue { readonly fieldKey: string; readonly value?: string | null; }
export interface IntakeAttachment { readonly id: string; readonly fileName: string; readonly securityState: string; }
export interface IntakeSubmission { readonly id: string; readonly formId: string; readonly formVersion: number; readonly state: string; readonly confirmationCode: string; readonly workItemId?: string | null; readonly values: readonly IntakeSubmissionValue[]; readonly attachments: readonly IntakeAttachment[]; readonly triageNote?: string | null; readonly createdAt: string; readonly version: number; }
export interface IntakePage<T> { readonly items: readonly T[]; readonly totalCount: number; readonly page: number; readonly pageSize: number; }
export interface IntakeContext { readonly forms: readonly IntakeForm[]; readonly roles: readonly IntakeRole[]; readonly schema: IntakeSchema; }
