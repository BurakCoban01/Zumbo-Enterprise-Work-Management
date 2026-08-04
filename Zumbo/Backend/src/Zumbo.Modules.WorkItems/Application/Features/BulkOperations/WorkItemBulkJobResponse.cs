namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemBulkJobResponse(
    string Id,
    string ProjectId,
    string Type,
    string? Operation,
    bool DryRun,
    string State,
    int TotalItems,
    int ProcessedItems,
    int SucceededItems,
    int FailedItems,
    bool CancelRequested,
    bool HasResult,
    bool HasErrorFile,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArtifactsExpireAt,
    long Version);
