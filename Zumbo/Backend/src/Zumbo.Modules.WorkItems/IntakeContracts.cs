namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFieldDefinitionRequest(
    string Key,
    string Label,
    string Type,
    bool Required = false,
    string? HelpText = null,
    IReadOnlyCollection<string>? Options = null);

public sealed record IntakeCustomFieldMappingRequest(
    string IntakeFieldKey,
    string WorkItemFieldKey);

public sealed record IntakeFieldMappingRequest(
    string TitleFieldKey,
    string? DescriptionFieldKey = null,
    string? PriorityFieldKey = null,
    string? DueDateFieldKey = null,
    IReadOnlyCollection<IntakeCustomFieldMappingRequest>? CustomFields = null);

public sealed record IntakeFormDefinitionRequest(
    string AccessPolicy,
    string BoardId,
    string WorkItemType,
    string DefaultPriority,
    string ConfirmationMessage,
    IReadOnlyCollection<IntakeFieldDefinitionRequest> Fields,
    IntakeFieldMappingRequest Mapping);

public sealed record CreateIntakeFormRequest(
    string ProjectId,
    string Name,
    string? Description,
    IntakeFormDefinitionRequest Definition);

public sealed record UpdateIntakeFormRequest(
    string Name,
    string? Description,
    IntakeFormDefinitionRequest Definition);

public sealed record IntakeFieldDefinitionResponse(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? HelpText,
    IReadOnlyCollection<string> Options);

public sealed record IntakeCustomFieldMappingResponse(
    string IntakeFieldKey,
    string WorkItemFieldKey);

public sealed record IntakeFieldMappingResponse(
    string TitleFieldKey,
    string? DescriptionFieldKey,
    string? PriorityFieldKey,
    string? DueDateFieldKey,
    IReadOnlyCollection<IntakeCustomFieldMappingResponse> CustomFields);

public sealed record IntakeFormDefinitionResponse(
    string AccessPolicy,
    string BoardId,
    string WorkItemType,
    string DefaultPriority,
    string ConfirmationMessage,
    IReadOnlyCollection<IntakeFieldDefinitionResponse> Fields,
    IntakeFieldMappingResponse Mapping);

public sealed record IntakeFormResponse(
    string Id,
    string ProjectId,
    string Name,
    string Description,
    string State,
    string? PublicId,
    int PublishedVersion,
    IntakeFormDefinitionResponse Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    long Version) : Zumbo.BuildingBlocks.Application.Persistence.IVersionedResource;

public sealed record PublishedIntakeFormResponse(
    string FormId,
    int Version,
    string Name,
    string Description,
    string AccessPolicy,
    string ConfirmationMessage,
    IReadOnlyCollection<IntakeFieldDefinitionResponse> Fields);

public sealed record IntakeSubmissionValueRequest(string FieldKey, string? Value);

public sealed record CreateIntakeSubmissionRequest(
    IReadOnlyCollection<IntakeSubmissionValueRequest> Values,
    string? Website = null);

public sealed record IntakeAttachmentUpload(
    string FieldKey,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);

public sealed record IntakeSubmissionConfirmationResponse(
    string SubmissionId,
    string ConfirmationCode,
    string Message,
    string State,
    string? WorkItemId);

public sealed record IntakeSubmissionAttachmentResponse(
    string Id,
    string FieldKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string SecurityState);

public sealed record IntakeSubmissionResponse(
    string Id,
    string FormId,
    int FormVersion,
    string ProjectId,
    string State,
    string ConfirmationCode,
    string WorkItemId,
    IReadOnlyCollection<IntakeSubmissionValueDocument> Values,
    IReadOnlyCollection<IntakeSubmissionAttachmentResponse> Attachments,
    string? TriageNote,
    string? TriagedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version) : Zumbo.BuildingBlocks.Application.Persistence.IVersionedResource;

public sealed record IntakeSubmissionPage(
    IReadOnlyCollection<IntakeSubmissionResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record TriageIntakeSubmissionRequest(string State, string? Note);

public sealed record IntakeRouteAuthorization(
    string OrganizationId,
    string ProjectId,
    string BoardId);

public interface IIntakeRoutePolicy
{
    Task<IntakeRouteAuthorization> ValidateAsync(
        string organizationId,
        string projectId,
        string boardId,
        CancellationToken ct);
}

public sealed record IntakeWorkItemCreation(
    string OrganizationId,
    string SubmissionId,
    CreateWorkItemRequest Request,
    string Description,
    IReadOnlyCollection<StoredAttachment> Attachments,
    string CorrelationId);

public interface IIntakeWorkItemCreator
{
    Task<WorkItemResponse> CreateAsync(
        IntakeWorkItemCreation creation,
        CancellationToken ct);
}
