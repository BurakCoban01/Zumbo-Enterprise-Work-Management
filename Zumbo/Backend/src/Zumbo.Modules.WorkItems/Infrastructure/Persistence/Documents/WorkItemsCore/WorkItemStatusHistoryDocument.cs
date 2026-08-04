using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemStatusHistoryDocument
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = "system";
    public DateTimeOffset ChangedAt { get; set; }
}
