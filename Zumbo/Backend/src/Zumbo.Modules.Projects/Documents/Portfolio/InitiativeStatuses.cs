using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public static class InitiativeStatuses
{
    public const string Planned = "Planned";
    public const string Active = "Active";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Planned, Active, Paused, Completed, Cancelled],
        StringComparer.Ordinal);
}
