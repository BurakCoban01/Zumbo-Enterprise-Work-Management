using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static List<string> NormalizeIds(
        IReadOnlyCollection<string> values,
        int maximum,
        string label)
    {
        var result = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Required(value, label, 128))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (result.Count > maximum)
            throw new ValidationException($"{label} list cannot exceed {maximum} entries.");
        return result;
    }
}
