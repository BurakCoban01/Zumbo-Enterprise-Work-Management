using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

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
}
