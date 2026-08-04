namespace Zumbo.Modules.WorkItems;

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
