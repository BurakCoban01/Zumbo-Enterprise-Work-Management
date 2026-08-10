namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed class WorkItemTypeSchemaPolicyAdapter(
    ValidateWorkItemShapeHandler validateShape,
    GetIssueTypeHierarchyHandler getHierarchy,
    ValidateWorkItemSearchFilterHandler validateSearchFilter) : IWorkItemTypeSchemaPolicy
{
    public Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct) =>
        validateShape.HandleAsync(new ValidateWorkItemShapeQuery(projectId, issueTypeKey, values), ct);

    public Task<string> HierarchyLevelAsync(
        string projectId,
        string issueTypeKey,
        CancellationToken ct) =>
        getHierarchy.HandleAsync(new GetIssueTypeHierarchyQuery(projectId, issueTypeKey), ct);

    public Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct) =>
        validateSearchFilter.HandleAsync(new ValidateWorkItemSearchFilterQuery(
            projectId,
            issueTypeKey,
            customFieldKey,
            customFieldValue), ct);
}
