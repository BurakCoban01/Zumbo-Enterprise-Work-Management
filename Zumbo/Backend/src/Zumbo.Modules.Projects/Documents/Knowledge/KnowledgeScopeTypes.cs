using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public static class KnowledgeScopeTypes
{
    public const string Project = "Project";
    public const string Initiative = "Initiative";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Project, Initiative],
        StringComparer.Ordinal);
}
