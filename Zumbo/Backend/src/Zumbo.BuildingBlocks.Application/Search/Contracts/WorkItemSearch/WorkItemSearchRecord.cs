namespace Zumbo.BuildingBlocks.Application.Search;

public sealed record WorkItemSearchRecord(
    string Id,
    string ProjectId,
    string BoardId,
    string Title,
    string Description,
    string Status,
    string Priority,
    string? AssigneeUserId,
    IReadOnlyCollection<string> Labels,
    string Type = "Task",
    string CustomFieldSearchText = "",
    IReadOnlyCollection<string>? CustomFieldExactValues = null,
    string OrganizationId = "");
