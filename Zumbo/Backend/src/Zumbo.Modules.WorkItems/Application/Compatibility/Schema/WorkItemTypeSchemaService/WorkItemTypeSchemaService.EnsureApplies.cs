using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private static void EnsureApplies(CustomFieldDefinitionDocument field, string issueTypeKey)
    {
        if (!field.AppliesToIssueTypes.Contains(issueTypeKey, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Custom field '{field.Key}' does not apply to issue type '{issueTypeKey}'.");
        }
    }
}
