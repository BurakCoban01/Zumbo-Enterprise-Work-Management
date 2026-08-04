using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class DurableWorkItemEventPublisher(
    IDurableEventOutbox outbox,
    IClock clock,
    ICurrentUser currentUser,
    IAuditRequestContext auditRequestContext,
    IHttpContextAccessor httpContextAccessor) :
    IWorkItemAuditPublisher,
    IWorkItemNotificationPublisher,
    IWorkItemSearchPublisher,
    IWorkItemRealtimePublisher,
    IWorkItemCacheInvalidationPublisher,
    IWorkItemRecurrenceEventPublisher,
    IWorkItemBulkJobEventPublisher,
    IWorkItemAutomationEventPublisher,
    IDevelopmentWebhookQueue
{
    public Task WriteAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct)
    {
        var metadata = auditRequestContext.GetMetadata();
        var deduplicationKey = Key("audit", action, entityId, correlationId);
        return EnqueueAsync(
            WorkItemDurableEventTypes.Audit,
            new WorkItemAuditEvent(
                currentUser.UserId ?? "system",
                action,
                entityType,
                entityId,
                oldValue,
                newValue,
                metadata.IpAddress,
                metadata.UserAgent,
                correlationId,
                clock.UtcNow,
                deduplicationKey),
            correlationId,
            deduplicationKey,
            ct);
    }

    public Task NotifyAsync(
        string userId,
        string type,
        string message,
        CancellationToken ct,
        string? deduplicationKey = null)
    {
        var correlationId = CorrelationId();
        var key = string.IsNullOrWhiteSpace(deduplicationKey)
            ? Key("notification", userId, type, message, correlationId)
            : deduplicationKey.Trim();
        return EnqueueAsync(
            WorkItemDurableEventTypes.Notification,
            new WorkItemNotificationEvent(userId, type, message, key),
            correlationId,
            key,
            ct);
    }

    public Task IndexAsync(WorkItemSearchRecord record, CancellationToken ct)
    {
        var correlationId = CorrelationId();
        return EnqueueAsync(
            WorkItemDurableEventTypes.SearchUpsert,
            new WorkItemSearchUpsertEvent(record),
            correlationId,
            Key("search-upsert", record.Id, correlationId),
            ct);
    }

    public Task DeleteAsync(string workItemId, CancellationToken ct)
    {
        var correlationId = CorrelationId();
        return EnqueueAsync(
            WorkItemDurableEventTypes.SearchDelete,
            new WorkItemSearchDeleteEvent(workItemId),
            correlationId,
            Key("search-delete", workItemId, correlationId),
            ct);
    }

    public async Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct)
    {
        var dedupe = Key("realtime", change.EventType, change.WorkItemId, change.CorrelationId);
        await EnqueueAsync(
            WorkItemDurableEventTypes.Realtime,
            change,
            change.CorrelationId,
            dedupe,
            ct);
        await EnqueueAsync(
            WorkItemDurableEventTypes.Webhook,
            new WorkItemWebhookEvent(
                change.EventType,
                change.WorkItemId,
                change.ProjectId,
                change.CorrelationId,
                change.OccurredAt,
                change.BoardId,
                change.WorkItem,
                change.ResourceVersion),
            change.CorrelationId,
            Key("webhook", change.EventType, change.WorkItemId, change.CorrelationId),
            ct);
    }

    public Task InvalidateProjectAsync(string projectId, CancellationToken ct)
    {
        var correlationId = CorrelationId();
        return EnqueueAsync(
            WorkItemDurableEventTypes.CacheInvalidation,
            new WorkItemCacheInvalidationEvent(projectId),
            correlationId,
            Key("cache", projectId, correlationId),
            ct);
    }

    public Task PublishAsync(WorkItemRecurrenceDueEvent message, CancellationToken ct)
    {
        var correlationId = "recurrence:" + message.OccurrenceId;
        return EnqueueAsync(
            WorkItemDurableEventTypes.RecurrenceOccurrence,
            message,
            correlationId,
            Key("recurrence-occurrence", message.OccurrenceId),
            ct,
            message.OrganizationId);
    }

    public Task PublishAsync(WorkItemBulkJobDueEvent message, CancellationToken ct)
    {
        var correlationId = $"bulk-job:{message.JobId}:{message.DispatchSequence}";
        return EnqueueAsync(
            WorkItemDurableEventTypes.BulkJob,
            message,
            correlationId,
            Key("bulk-job", message.JobId, message.DispatchSequence.ToString()),
            ct,
            message.OrganizationId);
    }

    public Task PublishAsync(WorkItemAutomationEvent message, CancellationToken ct) =>
        EnqueueAsync(
            WorkItemDurableEventTypes.Automation,
            message,
            message.CorrelationId,
            Key("automation", message.EventType, message.TriggerId, message.WorkItemId),
            ct,
            message.OrganizationId);

    public Task EnqueueAsync(DevelopmentWebhookEvent message, CancellationToken ct) =>
        EnqueueAsync(
            WorkItemDurableEventTypes.DevelopmentWebhook,
            message,
            "development-webhook:" + message.DeliveryId,
            Key("development-webhook", message.ConnectionId, message.DeliveryId),
            ct,
            message.OrganizationId);

    private Task EnqueueAsync<TPayload>(
        string eventType,
        TPayload payload,
        string correlationId,
        string deduplicationKey,
        CancellationToken ct,
        string? tenantId = null) =>
        outbox.EnqueueAsync(
            DurableEventEnvelope.Create(
                "WorkItems",
                eventType,
                1,
                tenantId ?? currentUser.OrganizationId ?? "system",
                correlationId,
                JsonSerializer.Serialize(payload),
                clock.UtcNow,
                deduplicationKey),
            ct);

    private string CorrelationId() =>
        httpContextAccessor.HttpContext?.TraceIdentifier
        ?? Activity.Current?.TraceId.ToString()
        ?? Guid.NewGuid().ToString("N");

    private static string Key(params string?[] values)
    {
        var source = string.Join('|', values.Select(value => value ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
