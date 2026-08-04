using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

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
