using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private static IReadOnlyCollection<WorkItemCustomFieldValueDocument> ValidateValues(
        WorkItemTypeSchemaDocument schema,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? requested)
    {
        var values = requested ?? [];
        if (values.Count > 100
            || values.GroupBy(value => NormalizeKey(value.FieldKey), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw new ValidationException("Custom field values must contain at most 100 unique field keys.");
        }

        var result = new List<WorkItemCustomFieldValueDocument>(values.Count);
        foreach (var value in values)
        {
            var key = NormalizeKey(value.FieldKey);
            var field = schema.CustomFields.SingleOrDefault(item =>
                    item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Custom field '{key}' is not defined.");
            EnsureApplies(field, issueTypeKey);
            result.Add(NormalizeValue(field, value));
        }

        EnsureRequiredValues(schema, issueTypeKey, result.Select(item => item.FieldKey));
        return result.OrderBy(item =>
                schema.CustomFields.Single(field => field.Key == item.FieldKey).Position)
            .ThenBy(item => item.FieldKey, StringComparer.Ordinal)
            .ToList();
    }
}
