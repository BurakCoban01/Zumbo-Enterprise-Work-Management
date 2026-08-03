using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class AutomationRunStepDocument
{
    public int Index { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Status { get; set; } = AutomationStepStates.Pending;
    public int Attempt { get; set; }
    public string? FailureCategory { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
