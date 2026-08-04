using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemBulkJobDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Operation { get; set; }
    public string? OperationValue { get; set; }
    public bool DryRun { get; set; }
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string State { get; set; } = WorkItemBulkJobStates.Pending;
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SucceededItems { get; set; }
    public int FailedItems { get; set; }
    public int NextItemIndex { get; set; }
    public int DispatchSequence { get; set; }
    public bool CancelRequested { get; set; }
    public bool IncludeArchived { get; set; }
    public string? ResultStoragePath { get; set; }
    public string? ResultFileName { get; set; }
    public string? ResultContentType { get; set; }
    public string? ResultChecksumSha256 { get; set; }
    public long? ResultSizeBytes { get; set; }
    public string? ErrorStoragePath { get; set; }
    public string? ErrorFileName { get; set; }
    public string? ErrorContentType { get; set; }
    public string? ErrorChecksumSha256 { get; set; }
    public long? ErrorSizeBytes { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long Version { get; set; }
}
