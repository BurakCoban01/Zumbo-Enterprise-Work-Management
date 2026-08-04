using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class IssueTypeHierarchyLevels
{
    public const string Epic = "Epic";
    public const string Standard = "Standard";
    public const string Subtask = "Subtask";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Epic, Standard, Subtask],
        StringComparer.Ordinal);
}
