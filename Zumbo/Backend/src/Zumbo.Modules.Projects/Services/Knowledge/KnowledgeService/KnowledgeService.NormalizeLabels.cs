using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static List<string> NormalizeLabels(
        IReadOnlyCollection<string>? values,
        int maximum)
    {
        var normalized = (values
                ?? throw new ValidationException("Knowledge tag list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > maximum
            || normalized.Any(value => value.Length > 32))
        {
            throw new ValidationException("Knowledge tag list is outside the supported bounds.");
        }
        return normalized;
    }
}
