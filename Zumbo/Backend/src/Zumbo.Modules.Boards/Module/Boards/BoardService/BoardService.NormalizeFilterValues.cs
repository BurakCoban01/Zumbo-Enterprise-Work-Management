using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private static List<string> NormalizeFilterValues(
        IReadOnlyCollection<string>? values,
        string field,
        int maximumCount)
    {
        var normalized = (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > maximumCount || normalized.Any(x => x.Length > 80))
        {
            throw new ValidationException($"Board filter {field} values exceed the allowed limits.");
        }

        return normalized;
    }
}
