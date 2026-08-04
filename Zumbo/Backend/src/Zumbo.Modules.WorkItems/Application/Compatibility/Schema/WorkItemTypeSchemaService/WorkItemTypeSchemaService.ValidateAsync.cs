using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    public async Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct)
    {
        var schema = await LoadOrDefaultAsync(projectId, ct);
        var issueType = FindActiveIssueType(schema, issueTypeKey);
        return new ValidatedWorkItemShape(
            issueType.Key,
            issueType.HierarchyLevel,
            schema.SchemaVersion,
            ValidateValues(schema, issueType.Key, values));
    }
}
