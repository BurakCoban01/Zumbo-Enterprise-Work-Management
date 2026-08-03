using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class SprintCompletionSnapshotDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string SprintId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public decimal CommittedPoints { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CarryoverSprintId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public long Version { get; set; }
}
