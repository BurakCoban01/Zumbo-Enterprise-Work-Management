namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemExportRow(
    string Id,
    string BoardId,
    string Title,
    string Description,
    string Type,
    string Priority,
    string Status,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? ParentId,
    string? TeamId,
    IReadOnlyCollection<string> Labels,
    IReadOnlyCollection<WorkItemCustomFieldValueDocument> CustomFields,
    bool Archived,
    long Version);
