namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class ValidateWorkItemShapeSlice(WorkItemTypeSchemaPolicyAccess access)
{
    internal async Task<ValidatedWorkItemShape> HandleAsync(
        ValidateWorkItemShapeQuery query,
        CancellationToken ct)
    {
        var schema = await access.LoadOrDefaultAsync(query.ProjectId, ct);
        var issueType = WorkItemTypeSchemaDefinitionPolicy.FindActiveIssueType(schema, query.IssueTypeKey);
        return new ValidatedWorkItemShape(
            issueType.Key,
            issueType.HierarchyLevel,
            schema.SchemaVersion,
            WorkItemTypeSchemaDefinitionPolicy.ValidateValues(schema, issueType.Key, query.Values));
    }
}
