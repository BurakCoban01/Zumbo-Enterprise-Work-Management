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

    private List<IntakeSubmissionValueDocument> NormalizeValues(
        IntakeFormDefinitionDocument definition,
        IReadOnlyCollection<IntakeSubmissionValueRequest>? requestedValues)
    {
        var requests = requestedValues?.ToList() ?? [];
        if (requests.Count > options.MaxValues || requests.Count > definition.Fields.Count)
        {
            throw new ValidationException("Submission contains too many values.");
        }

        var duplicate = requests
            .Select(x => RequiredKey(x.FieldKey))
            .GroupBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new ValidationException($"Submission field '{duplicate.Key}' is duplicated.");
        }

        var byKey = requests.ToDictionary(
            x => RequiredKey(x.FieldKey),
            x => x.Value ?? string.Empty,
            StringComparer.Ordinal);
        var unknown = byKey.Keys.FirstOrDefault(
            key => definition.Fields.All(field => field.Key != key));
        if (unknown is not null)
        {
            throw new ValidationException($"Submission field '{unknown}' is not defined.");
        }

        var result = new List<IntakeSubmissionValueDocument>();
        var totalCharacters = 0;
        foreach (var field in definition.Fields.Where(x => x.Type != IntakeFieldTypes.Attachment))
        {
            byKey.TryGetValue(field.Key, out var raw);
            var normalized = NormalizeValue(field, raw ?? string.Empty);
            if (field.Required && string.IsNullOrWhiteSpace(normalized))
            {
                throw new ValidationException($"Field '{field.Label}' is required.");
            }
            if (normalized.Length > options.MaxValueCharacters)
            {
                throw new ValidationException(
                    $"Field '{field.Label}' cannot exceed {options.MaxValueCharacters} characters.");
            }
            totalCharacters += normalized.Length;
            if (totalCharacters > options.MaxTotalValueCharacters)
            {
                throw new ValidationException("Submission values exceed the total size limit.");
            }
            if (normalized.Length > 0 || field.Type == IntakeFieldTypes.Checkbox)
            {
                result.Add(new IntakeSubmissionValueDocument
                {
                    FieldKey = field.Key,
                    Value = normalized
                });
            }
        }
        return result;
    }
}
