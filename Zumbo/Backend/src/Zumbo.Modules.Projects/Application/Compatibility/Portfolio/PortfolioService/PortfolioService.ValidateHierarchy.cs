using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private static void ValidateHierarchy(IReadOnlyCollection<InitiativeDocument> initiatives)
    {
        var byId = initiatives.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var initiative in initiatives)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { initiative.Id };
            var current = initiative;
            var depth = 1;
            while (current.ParentInitiativeId is not null)
            {
                if (!byId.TryGetValue(current.ParentInitiativeId, out current!))
                    throw new ValidationException("Parent initiative must belong to the same portfolio.");
                if (!seen.Add(current.Id))
                    throw new ValidationException("Initiative hierarchy cannot contain cycles.");
                depth++;
                if (depth > MaximumHierarchyDepth)
                    throw new ValidationException($"Initiative hierarchy cannot exceed {MaximumHierarchyDepth} levels.");
            }
        }
    }
}
