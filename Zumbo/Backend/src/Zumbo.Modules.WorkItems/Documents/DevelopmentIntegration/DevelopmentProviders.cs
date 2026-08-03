using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class DevelopmentProviders
{
    public const string GitHub = "GitHub";
    public const string GitLab = "GitLab";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [GitHub, GitLab],
        StringComparer.Ordinal);
}
