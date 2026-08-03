using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    public async Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct)
    {
        var schema = await LoadOrDefaultAsync(projectId, ct);
        string? normalizedIssueType = null;
        if (!string.IsNullOrWhiteSpace(issueTypeKey))
        {
            normalizedIssueType = FindActiveIssueType(schema, issueTypeKey).Key;
        }

        string? normalizedFieldKey = null;
        string? normalizedFieldValue = null;
        if (!string.IsNullOrWhiteSpace(customFieldKey))
        {
            var key = NormalizeKey(customFieldKey);
            var field = schema.CustomFields.SingleOrDefault(item =>
                item.Indexed && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                throw new ValidationException($"Custom field '{key}' is not indexed in the project schema.");
            }
            normalizedFieldKey = field.Key;
            normalizedFieldValue = NormalizeSearchValue(field, customFieldValue!);
        }

        return new ValidatedWorkItemSearchFilter(
            normalizedIssueType,
            normalizedFieldKey,
            normalizedFieldValue);
    }
}
