export type PublicIntakeFieldType='Text'|'LongText'|'Email'|'Number'|'Date'|'Choice'|'Checkbox'|'Attachment';
export interface PublicIntakeField{readonly key:string;readonly label:string;readonly type:PublicIntakeFieldType;readonly required:boolean;readonly helpText?:string|null;readonly options:readonly string[];}
export interface PublicIntakeForm{readonly formId:string;readonly version:number;readonly name:string;readonly description:string;readonly accessPolicy:string;readonly confirmationMessage:string;readonly fields:readonly PublicIntakeField[];}
export interface PublicIntakeModel{values:Record<string,string|boolean>;files:Record<string,readonly File[]>;website:string;}
export interface PublicIntakeConfirmation{readonly submissionId:string;readonly confirmationCode:string;readonly message:string;readonly state:string;}
