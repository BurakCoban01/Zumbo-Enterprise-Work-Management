using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeSubmissionDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    public int FormVersion { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string AccessPolicy { get; set; } = string.Empty;
    public string SubmittedByUserId { get; set; } = "public";
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string ConfirmationCode { get; set; } = string.Empty;
    public string State { get; set; } = IntakeSubmissionStates.Processing;
    public string WorkItemId { get; set; } = string.Empty;
    public List<IntakeSubmissionValueDocument> Values { get; set; } = [];
    public List<IntakeSubmissionAttachmentDocument> Attachments { get; set; } = [];
    public string? TriageNote { get; set; }
    public string? TriagedByUserId { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
