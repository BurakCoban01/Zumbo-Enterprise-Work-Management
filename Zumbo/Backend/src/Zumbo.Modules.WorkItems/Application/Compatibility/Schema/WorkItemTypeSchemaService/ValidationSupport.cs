using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Schema;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService
{
private static void EnsureApplies(CustomFieldDefinitionDocument field, string issueTypeKey)
    {
        if (!field.AppliesToIssueTypes.Contains(issueTypeKey, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Custom field '{field.Key}' does not apply to issue type '{issueTypeKey}'.");
        }
    }

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

private static IssueTypeDefinitionDocument FindActiveIssueType(
        WorkItemTypeSchemaDocument schema,
        string? issueTypeKey)
    {
        var key = issueTypeKey?.Trim().Equals("sub-task", StringComparison.OrdinalIgnoreCase) == true
            ? "Subtask"
            : string.IsNullOrWhiteSpace(issueTypeKey) ? "Task" : issueTypeKey.Trim();
        return schema.IssueTypes.SingleOrDefault(item =>
                item.Active && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Issue type '{key}' is not active in the project schema.");
    }

private static string NormalizeSearchValue(CustomFieldDefinitionDocument field, string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ValidationException($"Custom field '{field.Key}' search value is required.");
        }

        return field.Type switch
        {
            WorkItemFieldTypes.Text when normalized.Length <= (field.MaxLength ?? 1_000) => normalized,
            WorkItemFieldTypes.Number when decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number) => number.ToString(CultureInfo.InvariantCulture),
            WorkItemFieldTypes.Boolean when bool.TryParse(normalized, out var boolean) =>
                boolean ? "true" : "false",
            WorkItemFieldTypes.Date when DateOnly.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WorkItemFieldTypes.Select when field.Options.Any(option =>
                option.Equals(normalized, StringComparison.OrdinalIgnoreCase)) =>
                field.Options.Single(option => option.Equals(normalized, StringComparison.OrdinalIgnoreCase)),
            _ => throw new ValidationException(
                $"Custom field '{field.Key}' search value does not match type '{field.Type}'.")
        };
    }

private static WorkItemCustomFieldValueDocument NormalizeValue(
        CustomFieldDefinitionDocument field,
        WorkItemCustomFieldValueRequest value)
    {
        var populated = new object?[]
        {
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateValue,
            value.OptionKey
        }.Count(item => item is not null);
        if (populated != 1)
        {
            throw new ValidationException($"Custom field '{field.Key}' requires exactly one typed value.");
        }

        var result = new WorkItemCustomFieldValueDocument
        {
            FieldKey = field.Key,
            Type = field.Type,
            Indexed = field.Indexed
        };
        switch (field.Type)
        {
            case WorkItemFieldTypes.Text when value.TextValue is not null:
                var text = value.TextValue.Trim();
                if (text.Length == 0 || text.Length > (field.MaxLength ?? 1_000))
                {
                    throw new ValidationException($"Custom field '{field.Key}' text value is outside its length limit.");
                }
                if (text.Any(char.IsControl))
                {
                    throw new ValidationException($"Custom field '{field.Key}' text value contains control characters.");
                }
                result.TextValue = text;
                result.SearchValue = text;
                break;
            case WorkItemFieldTypes.Number when value.NumberValue is not null:
                if ((field.Minimum is not null && value.NumberValue < field.Minimum)
                    || (field.Maximum is not null && value.NumberValue > field.Maximum))
                {
                    throw new ValidationException($"Custom field '{field.Key}' number value is outside its range.");
                }
                result.NumberValue = value.NumberValue;
                result.SearchValue = value.NumberValue.Value.ToString(CultureInfo.InvariantCulture);
                break;
            case WorkItemFieldTypes.Boolean when value.BooleanValue is not null:
                result.BooleanValue = value.BooleanValue;
                result.SearchValue = value.BooleanValue.Value ? "true" : "false";
                break;
            case WorkItemFieldTypes.Date when value.DateValue is not null:
                result.DateValueUtc = new DateTimeOffset(value.DateValue.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                result.SearchValue = value.DateValue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            case WorkItemFieldTypes.Select when value.OptionKey is not null:
                var option = value.OptionKey.Trim();
                if (!field.Options.Contains(option, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ValidationException($"Custom field '{field.Key}' option is not allowed.");
                }
                result.OptionKey = field.Options.Single(item => item.Equals(option, StringComparison.OrdinalIgnoreCase));
                result.SearchValue = result.OptionKey;
                break;
            default:
                throw new ValidationException($"Custom field '{field.Key}' value does not match type '{field.Type}'.");
        }

        return result;
    }

private static WorkItemCustomFieldValueRequest ToRequest(WorkItemCustomFieldValueDocument value) => new(
        value.FieldKey,
        value.TextValue,
        value.NumberValue,
        value.BooleanValue,
        value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
        value.OptionKey);

private static void ValidateStoredValues(
        WorkItemTypeSchemaDocument schema,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueDocument> values)
    {
        foreach (var value in values)
        {
            var field = schema.CustomFields.SingleOrDefault(item => item.Key == value.FieldKey)
                ?? throw new ConflictException(
                    "WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID",
                    $"Existing custom field '{value.FieldKey}' would be removed.");
            EnsureApplies(field, issueTypeKey);
            if (field.Type != value.Type)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID",
                    $"Existing custom field '{value.FieldKey}' would change type.");
            }


            try
            {
                _ = NormalizeValue(field, ToRequest(value));
            }
            catch (ValidationException exception)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID",
                    $"Existing custom field '{value.FieldKey}' would violate the new rules: {exception.Message}");
            }
        }

        EnsureRequiredValues(schema, issueTypeKey, values.Select(item => item.FieldKey));
    }

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
