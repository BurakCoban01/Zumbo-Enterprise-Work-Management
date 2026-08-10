namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed record NormalizedWorkItemTemplate(
    string BoardId,
    string Name,
    string Title,
    string Description,
    string Type,
    int SchemaVersion,
    List<WorkItemCustomFieldValueDocument> CustomFields,
    string Priority,
    string? AssigneeUserId,
    string? TeamId,
    int? DueAfterDays,
    List<string> Labels);
