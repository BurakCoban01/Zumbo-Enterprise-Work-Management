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

    private static string Required(string value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(label + " is required.");
        }
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ValidationException($"{label} cannot exceed {maximumLength} characters.");
        }
        return normalized;
    }
}
