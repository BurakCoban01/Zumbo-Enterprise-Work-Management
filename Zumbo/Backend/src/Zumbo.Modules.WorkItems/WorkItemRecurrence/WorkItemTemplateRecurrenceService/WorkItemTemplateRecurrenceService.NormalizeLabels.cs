using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    private static List<string> NormalizeLabels(IReadOnlyCollection<string>? labels)
    {
        var normalized = (labels ?? [])
            .Select(label => Required(label, "Template label", 50))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > 50)
        {
            throw new ValidationException("A template cannot contain more than 50 labels.");
        }
        return normalized;
    }
}
