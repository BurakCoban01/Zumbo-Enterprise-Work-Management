namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class GetIssueTypeHierarchySlice(WorkItemTypeSchemaPolicyAccess access)
{
    internal async Task<string> HandleAsync(GetIssueTypeHierarchyQuery query, CancellationToken ct) =>
        WorkItemTypeSchemaDefinitionPolicy.FindActiveIssueType(
            await access.LoadOrDefaultAsync(query.ProjectId, ct),
            query.IssueTypeKey).HierarchyLevel;
}
