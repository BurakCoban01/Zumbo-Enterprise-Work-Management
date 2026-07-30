using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemDurableEventTypes
{
    public const string Audit = "work-item.audit.v1";
    public const string Notification = "work-item.notification.v1";
    public const string SearchUpsert = "work-item.search-upsert.v1";
    public const string SearchDelete = "work-item.search-delete.v1";
    public const string Realtime = "work-item.realtime.v1";
    public const string CacheInvalidation = "work-item.cache-invalidation.v1";
    public const string Webhook = "work-item.webhook.v1";
    public const string RecurrenceOccurrence = "work-item.recurrence-occurrence.v1";
    public const string BulkJob = "work-item.bulk-job.v1";
    public const string Automation = "work-item.automation.v1";
    public const string DevelopmentWebhook = "work-item.development-webhook.v1";
}

public sealed record WorkItemAuditEvent(
    string ActorUserId,
    string Action,
    string EntityType,
    string EntityId,
    string? OldValue,
    string? NewValue,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string DeduplicationKey);

public sealed record WorkItemNotificationEvent(
    string UserId,
    string Type,
    string Message,
    string DeduplicationKey);

public sealed record WorkItemSearchUpsertEvent(WorkItemSearchRecord Record);
public sealed record WorkItemSearchDeleteEvent(string WorkItemId);
public sealed record WorkItemCacheInvalidationEvent(string ProjectId);
public sealed record WorkItemWebhookEvent(
    string EventType,
    string WorkItemId,
    string ProjectId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string? BoardId = null,
    WorkItemRealtimeItem? WorkItem = null,
    long ResourceVersion = 0);

public sealed record WorkItemRecurrenceDueEvent(
    string OrganizationId,
    string ProjectId,
    string RecurrenceId,
    string OccurrenceId,
    DateTimeOffset ScheduledForUtc);

public sealed record WorkItemAutomationEvent(
    string OrganizationId,
    string ProjectId,
    string EventType,
    string TriggerId,
    string WorkItemId,
    string ActorUserId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string?> Fields,
    string? RootRunId,
    int ChainDepth,
    IReadOnlyCollection<string> VisitedRuleIds);

public sealed record WorkItemAutomationChainContext(
    string RootRunId,
    int ChainDepth,
    IReadOnlyCollection<string> VisitedRuleIds);

public interface IWorkItemAutomationChainContextAccessor
{
    WorkItemAutomationChainContext? Current { get; }
    IDisposable Push(WorkItemAutomationChainContext context);
}

public sealed class WorkItemAutomationChainContextAccessor : IWorkItemAutomationChainContextAccessor
{
    private WorkItemAutomationChainContext? current;

    public WorkItemAutomationChainContext? Current => current;

    public IDisposable Push(WorkItemAutomationChainContext context)
    {
        var previous = current;
        current = context;
        return new RestoreScope(() => current = previous);
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            restore();
        }
    }
}

public interface IWorkItemRecurrenceEventPublisher
{
    Task PublishAsync(WorkItemRecurrenceDueEvent message, CancellationToken ct);
}

public interface IWorkItemAutomationEventPublisher
{
    Task PublishAsync(WorkItemAutomationEvent message, CancellationToken ct);
}

public interface IWorkItemAuditPublisher
{
    Task WriteAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public interface IWorkItemOperationsAuditWriter
{
    Task WriteAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public interface IWorkItemNotificationPublisher
{
    Task NotifyAsync(
        string userId,
        string type,
        string message,
        CancellationToken ct,
        string? deduplicationKey = null);
}

public interface IWorkItemSearchPublisher
{
    Task IndexAsync(WorkItemSearchRecord record, CancellationToken ct);
    Task DeleteAsync(string workItemId, CancellationToken ct);
}

public interface IWorkItemCacheInvalidationPublisher
{
    Task InvalidateProjectAsync(string projectId, CancellationToken ct);
}
