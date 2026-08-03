namespace Zumbo.Modules.WorkItems;

public sealed record IntakeWorkItemCreation(
    string OrganizationId,
    string SubmissionId,
    CreateWorkItemRequest Request,
    string Description,
    IReadOnlyCollection<StoredAttachment> Attachments,
    string CorrelationId);
