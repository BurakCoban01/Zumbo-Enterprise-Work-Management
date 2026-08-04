using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public static class KeyResultDirections
{
    public const string Increase = "Increase";
    public const string Decrease = "Decrease";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Increase, Decrease],
        StringComparer.Ordinal);
}
