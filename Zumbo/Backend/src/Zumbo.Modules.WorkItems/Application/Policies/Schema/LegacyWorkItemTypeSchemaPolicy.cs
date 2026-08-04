using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class LegacyWorkItemTypeSchemaPolicy : IWorkItemTypeSchemaPolicy
{
    public Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct)
    {
        var normalized = LegacyCanonical(issueTypeKey);
        return Task.FromResult(new ValidatedWorkItemShape(
            normalized,
            LegacyHierarchy(normalized),
            1,
            []));
    }

    public Task<string> HierarchyLevelAsync(string projectId, string issueTypeKey, CancellationToken ct) =>
        Task.FromResult(LegacyHierarchy(issueTypeKey));

    public Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(customFieldKey))
        {
            throw new ValidationException("Custom field search requires a project work item schema.");
        }

        return Task.FromResult(new ValidatedWorkItemSearchFilter(
            string.IsNullOrWhiteSpace(issueTypeKey) ? null : LegacyCanonical(issueTypeKey),
            null,
            null));
    }

    private static string LegacyHierarchy(string issueTypeKey) => issueTypeKey.Trim() switch
    {
        "Epic" => IssueTypeHierarchyLevels.Epic,
        "Subtask" => IssueTypeHierarchyLevels.Subtask,
        _ => IssueTypeHierarchyLevels.Standard
    };

    private static string LegacyCanonical(string? issueTypeKey) => issueTypeKey?.Trim().ToLowerInvariant() switch
    {
        "epic" => "Epic",
        "story" => "Story",
        "task" => "Task",
        "bug" => "Bug",
        "subtask" or "sub-task" => "Subtask",
        null or "" => "Task",
        _ => issueTypeKey!.Trim()
    };
}
