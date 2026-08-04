using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

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
