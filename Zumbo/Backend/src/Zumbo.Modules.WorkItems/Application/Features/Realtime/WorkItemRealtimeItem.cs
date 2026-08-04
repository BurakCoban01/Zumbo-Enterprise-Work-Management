namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemRealtimeItem(
    string Id,
    string ProjectId,
    string BoardId,
    string ColumnId,
    string Title,
    string Type,
    string Priority,
    string Status,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? SprintId,
    decimal EstimatePoints,
    DateTimeOffset? CompletedAt,
    long Rank,
    long Version);
