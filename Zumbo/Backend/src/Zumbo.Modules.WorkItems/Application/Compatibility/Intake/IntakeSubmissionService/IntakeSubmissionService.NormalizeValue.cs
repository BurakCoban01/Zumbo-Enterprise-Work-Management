using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class IntakeSubmissionService{

    private static string NormalizeValue(IntakeFieldDefinitionDocument field, string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return field.Type switch
        {
            IntakeFieldTypes.Text or IntakeFieldTypes.LongText => normalized,
            IntakeFieldTypes.Email => NormalizeEmail(normalized),
            IntakeFieldTypes.Number => decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : throw new ValidationException($"Field '{field.Label}' requires a number."),
            IntakeFieldTypes.Date => DateOnly.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : throw new ValidationException($"Field '{field.Label}' requires an ISO date."),
            IntakeFieldTypes.Choice => field.Options.FirstOrDefault(
                option => option.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Field '{field.Label}' contains an unknown option."),
            IntakeFieldTypes.Checkbox => bool.TryParse(normalized, out var selected)
                ? selected.ToString().ToLowerInvariant()
                : throw new ValidationException($"Field '{field.Label}' requires true or false."),
            _ => throw new ValidationException($"Field '{field.Label}' cannot contain a text value.")
        };
    }
}
