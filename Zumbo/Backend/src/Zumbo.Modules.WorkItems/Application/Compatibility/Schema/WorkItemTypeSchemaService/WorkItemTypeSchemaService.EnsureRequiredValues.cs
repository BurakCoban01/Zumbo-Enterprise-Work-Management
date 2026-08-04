using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private static void EnsureRequiredValues(
        WorkItemTypeSchemaDocument schema,
        string issueTypeKey,
        IEnumerable<string> populatedKeys)
    {
        var populated = populatedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = schema.CustomFields.FirstOrDefault(field =>
            field.Required
            && field.AppliesToIssueTypes.Contains(issueTypeKey, StringComparer.OrdinalIgnoreCase)
            && !populated.Contains(field.Key));
        if (missing is not null)
        {
            throw new ValidationException($"Required custom field '{missing.Key}' is missing.");
        }
    }
}
