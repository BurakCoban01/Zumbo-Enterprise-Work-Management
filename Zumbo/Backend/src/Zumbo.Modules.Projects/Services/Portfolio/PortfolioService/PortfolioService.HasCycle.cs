using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private static bool HasCycle(
        string node,
        IReadOnlyDictionary<string, List<string>> targets,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(node)) return false;
        if (!visiting.Add(node)) return true;
        if (targets.TryGetValue(node, out var next)
            && next.Any(target => HasCycle(target, targets, visiting, visited)))
        {
            return true;
        }
        visiting.Remove(node);
        visited.Add(node);
        return false;
    }
}
