using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class AutomationTriggerDocument
{
    public string Type { get; set; } = string.Empty;
    public string? EventType { get; set; }
    public int? IntervalMinutes { get; set; }
    public DateTimeOffset? StartAtUtc { get; set; }
}
