using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemTypeSchemaPolicy
{
    Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct);

    Task<string> HierarchyLevelAsync(string projectId, string issueTypeKey, CancellationToken ct);

    Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct);
}
