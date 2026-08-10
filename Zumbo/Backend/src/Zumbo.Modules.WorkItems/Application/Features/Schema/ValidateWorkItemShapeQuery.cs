namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed record ValidateWorkItemShapeQuery(
    string ProjectId,
    string IssueTypeKey,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? Values);
