using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

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
}
