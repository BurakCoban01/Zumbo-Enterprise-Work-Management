using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private static CustomFieldDefinitionDocument NormalizeField(
        CustomFieldDefinitionRequest request,
        IReadOnlyCollection<IssueTypeDefinitionDocument> issueTypes)
    {
        var key = NormalizeKey(request.Key);
        var name = request.Name?.Trim() ?? string.Empty;
        if (!KeyPattern().IsMatch(key) || name.Length is < 1 or > 100)
        {
            throw new ValidationException("Custom field keys and names are invalid.");
        }

        var type = Canonical(WorkItemFieldTypes.All, request.Type, "custom field type");
        var options = (request.Options ?? []).Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
        if (options.Count > 100 || options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
        {
            throw new ValidationException($"Custom field '{key}' options must be unique and cannot exceed 100.");
        }
        if (type == WorkItemFieldTypes.Select && options.Count == 0
            || type != WorkItemFieldTypes.Select && options.Count > 0)
        {
            throw new ValidationException($"Custom field '{key}' options do not match its type.");
        }
        if (options.Any(option => option.Length > 200 || option.Any(char.IsControl)))
        {
            throw new ValidationException($"Custom field '{key}' contains an invalid option.");
        }
        if (request.Minimum is not null && request.Maximum is not null && request.Minimum > request.Maximum)
        {
            throw new ValidationException($"Custom field '{key}' number range is invalid.");
        }
        if (type == WorkItemFieldTypes.Text && (request.MaxLength ?? 1_000) is < 1 or > 4_000)
        {
            throw new ValidationException($"Custom field '{key}' text limit must be between 1 and 4000.");
        }
        if (type == WorkItemFieldTypes.Text && request.Indexed && (request.MaxLength ?? 1_000) > 200)
        {
            throw new ValidationException($"Indexed text field '{key}' limit cannot exceed 200 characters.");
        }

        var appliesTo = (request.AppliesToIssueTypes ?? issueTypes.Select(item => item.Key).ToList())
            .Select(NormalizeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (appliesTo.Count == 0 || appliesTo.Any(keyValue =>
                issueTypes.All(item => !item.Key.Equals(keyValue, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException($"Custom field '{key}' issue type scope is invalid.");
        }

        return new CustomFieldDefinitionDocument
        {
            Key = key,
            Name = name,
            Type = type,
            Required = request.Required,
            Indexed = request.Indexed,
            MaxLength = type == WorkItemFieldTypes.Text ? request.MaxLength ?? 1_000 : null,
            Minimum = type == WorkItemFieldTypes.Number ? request.Minimum : null,
            Maximum = type == WorkItemFieldTypes.Number ? request.Maximum : null,
            Options = options,
            AppliesToIssueTypes = appliesTo,
            Position = Math.Clamp(request.Position, 0, 10_000)
        };
    }
}
