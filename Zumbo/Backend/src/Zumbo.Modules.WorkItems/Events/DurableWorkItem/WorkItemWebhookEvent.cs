using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;
public sealed record WorkItemWebhookEvent(
    string EventType,
    string WorkItemId,
    string ProjectId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string? BoardId = null,
    WorkItemRealtimeItem? WorkItem = null,
    long ResourceVersion = 0);
