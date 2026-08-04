using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class DevelopmentLinkKinds
{
    public const string Branch = "Branch";
    public const string Commit = "Commit";
    public const string PullRequest = "PullRequest";
    public const string Build = "Build";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Branch, Commit, PullRequest, Build],
        StringComparer.Ordinal);
}
