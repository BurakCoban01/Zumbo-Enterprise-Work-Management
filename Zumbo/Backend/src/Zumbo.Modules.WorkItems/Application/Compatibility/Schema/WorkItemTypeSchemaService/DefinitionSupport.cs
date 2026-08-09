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
private static string Canonical(IReadOnlySet<string> supported, string? value, string description) =>
        supported.SingleOrDefault(item => item.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ValidationException($"Unsupported {description}.");

private static WorkItemTypeSchemaDocument Default(string projectId, DateTimeOffset now)
    {
        var issueTypes = new[]
        {
            new IssueTypeDefinitionDocument { Key = "Epic", Name = "Epic", HierarchyLevel = IssueTypeHierarchyLevels.Epic, Position = 0 },
            new IssueTypeDefinitionDocument { Key = "Story", Name = "Story", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 10 },
            new IssueTypeDefinitionDocument { Key = "Task", Name = "Task", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 20 },
            new IssueTypeDefinitionDocument { Key = "Bug", Name = "Bug", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 30 },
            new IssueTypeDefinitionDocument { Key = "Subtask", Name = "Subtask", HierarchyLevel = IssueTypeHierarchyLevels.Subtask, Position = 40 }
        };
        return new WorkItemTypeSchemaDocument
        {
            Id = projectId,
            ProjectId = projectId,
            SchemaVersion = 1,
            IssueTypes = issueTypes.ToList(),
            Layouts = issueTypes.Select(item => new IssueTypeLayoutDocument { IssueTypeKey = item.Key }).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

[GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

private static WorkItemTypeSchemaDocument Normalize(
        string projectId,
        UpsertWorkItemTypeSchemaRequest request,
        DateTimeOffset now)
    {
        if (request.IssueTypes is null || request.IssueTypes.Count is < 1 or > 50)
        {
            throw new ValidationException("A work item schema requires between 1 and 50 issue types.");
        }

        var issueTypes = request.IssueTypes.Select(NormalizeIssueType).ToList();
        if (issueTypes.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Issue type keys must be unique.");
        }

        var fields = (request.CustomFields ?? []).Select(item => NormalizeField(item, issueTypes)).ToList();
        if (fields.Count > 100
            || fields.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Custom field keys must be unique and cannot exceed 100 definitions.");
        }

        var layouts = NormalizeLayouts(request.Layouts, issueTypes, fields);
        return new WorkItemTypeSchemaDocument
        {
            Id = projectId,
            ProjectId = projectId,
            IssueTypes = issueTypes.OrderBy(item => item.Position).ThenBy(item => item.Key, StringComparer.Ordinal).ToList(),
            CustomFields = fields.OrderBy(item => item.Position).ThenBy(item => item.Key, StringComparer.Ordinal).ToList(),
            Layouts = layouts,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

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

private static IssueTypeDefinitionDocument NormalizeIssueType(IssueTypeDefinitionRequest request)
    {
        var key = NormalizeKey(request.Key);
        var name = request.Name?.Trim() ?? string.Empty;
        if (!KeyPattern().IsMatch(key) || name.Length is < 1 or > 100)
        {
            throw new ValidationException("Issue type keys and names are invalid.");
        }

        var hierarchy = Canonical(IssueTypeHierarchyLevels.All, request.HierarchyLevel, "issue type hierarchy level");
        return new IssueTypeDefinitionDocument
        {
            Key = key,
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty,
            HierarchyLevel = hierarchy,
            Active = request.Active,
            Position = Math.Clamp(request.Position, 0, 10_000)
        };
    }

private static string NormalizeKey(string? key) => key?.Trim() ?? string.Empty;

private static List<IssueTypeLayoutDocument> NormalizeLayouts(
        IReadOnlyCollection<IssueTypeLayoutRequest>? requested,
        IReadOnlyCollection<IssueTypeDefinitionDocument> issueTypes,
        IReadOnlyCollection<CustomFieldDefinitionDocument> fields)
    {
        var layouts = requested?.ToList() ?? issueTypes.Select(issueType => new IssueTypeLayoutRequest(
            issueType.Key,
            fields.Where(field => field.AppliesToIssueTypes.Contains(issueType.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(field => field.Position)
                .Select(field => field.Key)
                .ToList())).ToList();
        if (layouts.GroupBy(item => NormalizeKey(item.IssueTypeKey), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)
            || layouts.Count != issueTypes.Count)
        {
            throw new ValidationException("Every issue type requires exactly one layout.");
        }

        return issueTypes.Select(issueType =>
        {
            var layout = layouts.SingleOrDefault(item =>
                    NormalizeKey(item.IssueTypeKey).Equals(issueType.Key, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Issue type '{issueType.Key}' layout is missing.");
            var keys = layout.FieldKeys.Select(NormalizeKey).ToList();
            if (keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count
                || keys.Any(key => fields.All(field =>
                    !field.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                    || !field.AppliesToIssueTypes.Contains(issueType.Key, StringComparer.OrdinalIgnoreCase))))
            {
                throw new ValidationException($"Issue type '{issueType.Key}' layout contains an invalid field.");
            }
            return new IssueTypeLayoutDocument { IssueTypeKey = issueType.Key, FieldKeys = keys };
        }).ToList();
    }
}
