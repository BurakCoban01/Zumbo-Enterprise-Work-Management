using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class ValidateWorkItemSearchFilterSlice(WorkItemTypeSchemaPolicyAccess access)
{
    internal async Task<ValidatedWorkItemSearchFilter> HandleAsync(
        ValidateWorkItemSearchFilterQuery query,
        CancellationToken ct)
    {
        var schema = await access.LoadOrDefaultAsync(query.ProjectId, ct);
        string? normalizedIssueType = null;
        if (!string.IsNullOrWhiteSpace(query.IssueTypeKey))
        {
            normalizedIssueType = WorkItemTypeSchemaDefinitionPolicy.FindActiveIssueType(
                schema,
                query.IssueTypeKey).Key;
        }

        string? normalizedFieldKey = null;
        string? normalizedFieldValue = null;
        if (!string.IsNullOrWhiteSpace(query.CustomFieldKey))
        {
            var key = query.CustomFieldKey.Trim();
            var field = schema.CustomFields.SingleOrDefault(item =>
                item.Indexed && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                throw new ValidationException($"Custom field '{key}' is not indexed in the project schema.");
            }
            normalizedFieldKey = field.Key;
            normalizedFieldValue = WorkItemTypeSchemaDefinitionPolicy.NormalizeSearchValue(
                field,
                query.CustomFieldValue!);
        }

        return new ValidatedWorkItemSearchFilter(
            normalizedIssueType,
            normalizedFieldKey,
            normalizedFieldValue);
    }
}
