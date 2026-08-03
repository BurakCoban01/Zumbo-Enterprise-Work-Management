using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public static class PortfolioDependencyStatuses
{
    public const string Active = "Active";
    public const string Resolved = "Resolved";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Active, Resolved],
        StringComparer.Ordinal);
}
