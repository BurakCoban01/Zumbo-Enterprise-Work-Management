using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private static void ValidateDependencyGraph(
        IReadOnlyCollection<PortfolioProjectDependencyDocument> dependencies)
    {
        var active = dependencies
            .Where(item => item.Status == PortfolioDependencyStatuses.Active)
            .ToList();
        if (active.GroupBy(
                item => $"{item.SourceProjectId}\u001f{item.TargetProjectId}",
                StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Active portfolio dependencies must be unique.");
        }
        var targets = active
            .GroupBy(item => item.SourceProjectId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.TargetProjectId).ToList(),
                StringComparer.Ordinal);
        foreach (var start in targets.Keys)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (HasCycle(start, targets, visiting, visited))
                throw new ValidationException("Active portfolio dependencies cannot contain cycles.");
        }
    }
}
