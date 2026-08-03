using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

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
}
