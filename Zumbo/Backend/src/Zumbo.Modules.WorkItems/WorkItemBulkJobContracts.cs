namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemBulkJobOptions
{
    public int BatchSize { get; init; } = 25;
    public int MaxInputItems { get; init; } = 5_000;
    public int MaxInputBytes { get; init; } = 5 * 1024 * 1024;
    public int MaxExportItems { get; init; } = 10_000;
    public long MaxArtifactBytes { get; init; } = 25 * 1024 * 1024;
}

public sealed record WorkItemImportRow(
    string SourceKey,
    string BoardId,
    string Title,
    string Type,
    string? Priority,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? ParentId = null,
    string? TeamId = null,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? CustomFields = null);

public sealed record WorkItemExportRow(
    string Id,
    string BoardId,
    string Title,
    string Description,
    string Type,
    string Priority,
    string Status,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? ParentId,
    string? TeamId,
    IReadOnlyCollection<string> Labels,
    IReadOnlyCollection<WorkItemCustomFieldValueDocument> CustomFields,
    bool Archived,
    long Version);

public sealed record CreateWorkItemImportJobRequest(
    string ProjectId,
    IReadOnlyCollection<WorkItemImportRow> Items,
    bool DryRun = false);

public sealed record CreateWorkItemExportJobRequest(
    string ProjectId,
    bool DryRun = false,
    bool IncludeArchived = false);

public sealed record CreateWorkItemBulkJobRequest(
    string ProjectId,
    string Operation,
    IReadOnlyCollection<string> WorkItemIds,
    string? Value = null,
    bool DryRun = false);

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
    long Version);

public sealed record WorkItemBulkJobPage(
    IReadOnlyCollection<WorkItemBulkJobResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record WorkItemBulkJobDueEvent(
    string OrganizationId,
    string ProjectId,
    string JobId,
    string RequestedByUserId,
    int DispatchSequence);

public interface IWorkItemBulkJobEventPublisher
{
    Task PublishAsync(WorkItemBulkJobDueEvent message, CancellationToken ct);
}

public sealed record StoredWorkItemBulkArtifact(
    string FileName,
    string ContentType,
    long SizeBytes,
    string StoragePath,
    string ChecksumSha256);

public interface IWorkItemBulkArtifactStorage
{
    Task<StoredWorkItemBulkArtifact> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken ct);

    Task<Stream> OpenReadAsync(
        string storagePath,
        string contentType,
        string expectedChecksumSha256,
        long maxSizeBytes,
        CancellationToken ct);

    Task DeleteAsync(string storagePath, CancellationToken ct);
}

public sealed record WorkItemBulkArtifactFile(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);
