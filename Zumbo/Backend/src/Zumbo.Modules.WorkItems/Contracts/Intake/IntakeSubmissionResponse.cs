namespace Zumbo.Modules.WorkItems;

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
