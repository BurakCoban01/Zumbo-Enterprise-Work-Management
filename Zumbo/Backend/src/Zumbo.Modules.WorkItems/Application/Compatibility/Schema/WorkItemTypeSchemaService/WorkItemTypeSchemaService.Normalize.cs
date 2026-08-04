using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

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
}
