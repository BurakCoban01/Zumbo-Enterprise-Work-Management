namespace Zumbo.Modules.WorkItems;

public static class WorkItemRealtimeProtocol
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record WorkItemRealtimeChange(
    string EventType,
    string WorkItemId,
    string ProjectId,
    string BoardId,
    WorkItemRealtimeItem WorkItem,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    int SchemaVersion = WorkItemRealtimeProtocol.CurrentSchemaVersion,
    long ResourceVersion = 0);

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

public interface IWorkItemRealtimePublisher
{
    Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct);
}
