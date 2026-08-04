using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public static class AutomationRunStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Skipped = "Skipped";
    public const string RetryScheduled = "RetryScheduled";
    public const string DeadLetter = "DeadLetter";
}
