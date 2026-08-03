using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class SprintScopeSnapshotDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string SprintId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal EstimatePoints { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public long Version { get; set; }
}
