namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemImportRow(
    string SourceKey,
    string BoardId,
    string Title,
    string Type,
    string? Priority,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? ParentId = null,
    string? TeamId = null,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? CustomFields = null);
