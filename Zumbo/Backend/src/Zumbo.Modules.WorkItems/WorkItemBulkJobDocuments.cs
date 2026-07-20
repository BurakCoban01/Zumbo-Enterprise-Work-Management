using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemBulkJobTypes
{
    public const string Import = "Import";
    public const string Export = "Export";
    public const string Bulk = "Bulk";
}

public static class WorkItemBulkOperations
{
    public const string Move = "Move";
    public const string Assign = "Assign";
    public const string Archive = "Archive";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "move" => Move,
        "assign" => Assign,
        "archive" => Archive,
        _ => throw new Zumbo.SharedKernel.ValidationException("Bulk operation must be Move, Assign, or Archive.")
    };
}

public static class WorkItemBulkJobStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string CompletedWithErrors = "CompletedWithErrors";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    public static bool IsTerminal(string state) => state is Completed or CompletedWithErrors or Cancelled;
}

public static class WorkItemBulkJobItemStates
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

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

public sealed class WorkItemBulkJobItemDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public int ItemIndex { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string State { get; set; } = WorkItemBulkJobItemStates.Pending;
    public string? ResultReference { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public long Version { get; set; }
}
