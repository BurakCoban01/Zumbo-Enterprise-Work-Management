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

public sealed class WorkItemAuditDurableHandler(
    AuditService audit) : IDurableEventHandler
{
    public string ConsumerName => "work-item-audit-v1";
    public string EventType => WorkItemDurableEventTypes.Audit;

    public async Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = DurablePayload.Read<WorkItemAuditEvent>(message);
        await audit.WriteAsAsync(
            payload.ActorUserId,
            payload.Action,
            payload.EntityType,
            payload.EntityId,
            payload.OldValue,
            payload.NewValue,
            payload.CorrelationId,
            new AuditRequestMetadata(payload.IpAddress, payload.UserAgent),
            payload.OccurredAtUtc,
            payload.DeduplicationKey,
            cancellationToken);
    }
}

public sealed class WorkItemNotificationDurableHandler(
    NotificationService notifications) : IDurableEventHandler
{
    public string ConsumerName => "work-item-notification-v1";
    public string EventType => WorkItemDurableEventTypes.Notification;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = DurablePayload.Read<WorkItemNotificationEvent>(message);
        return notifications.NotifyAsync(
            payload.UserId,
            payload.Type,
            payload.Message,
            cancellationToken,
            payload.DeduplicationKey);
    }
}

public sealed class WorkItemSearchUpsertDurableHandler(
    IWorkItemSearchIndex search) : IDurableEventHandler
{
    public string ConsumerName => "work-item-search-upsert-v1";
    public string EventType => WorkItemDurableEventTypes.SearchUpsert;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        search.IndexAsync(DurablePayload.Read<WorkItemSearchUpsertEvent>(message).Record, cancellationToken);
}

public sealed class WorkItemSearchDeleteDurableHandler(
    IWorkItemSearchIndex search) : IDurableEventHandler
{
    public string ConsumerName => "work-item-search-delete-v1";
    public string EventType => WorkItemDurableEventTypes.SearchDelete;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        search.DeleteAsync(DurablePayload.Read<WorkItemSearchDeleteEvent>(message).WorkItemId, cancellationToken);
}

public sealed class WorkItemRealtimeDurableHandler(
    SignalRWorkItemRealtimePublisher publisher) : IDurableEventHandler
{
    public string ConsumerName => "work-item-realtime-v1";
    public string EventType => WorkItemDurableEventTypes.Realtime;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        publisher.PublishAsync(DurablePayload.Read<WorkItemRealtimeChange>(message), cancellationToken);
}

public sealed class WorkItemCacheInvalidationDurableHandler(
    IWorkItemReadModelCache cache) : IDurableEventHandler
{
    public string ConsumerName => "work-item-cache-v1";
    public string EventType => WorkItemDurableEventTypes.CacheInvalidation;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        cache.InvalidateProjectAsync(
            DurablePayload.Read<WorkItemCacheInvalidationEvent>(message).ProjectId,
            cancellationToken);
}

public interface IWorkItemWebhookDelivery
{
    Task DeliverAsync(WorkItemWebhookEvent message, CancellationToken cancellationToken);

    Task DeliverAsync(
        string sourceEventId,
        string organizationId,
        WorkItemWebhookEvent message,
        CancellationToken cancellationToken) =>
        DeliverAsync(message, cancellationToken);
}

public sealed class WorkItemWebhookDurableHandler(
    IWorkItemWebhookDelivery delivery) : IDurableEventHandler
{
    public string ConsumerName => "work-item-webhook-v1";
    public string EventType => WorkItemDurableEventTypes.Webhook;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        delivery.DeliverAsync(
            message.Id,
            message.TenantId,
            DurablePayload.Read<WorkItemWebhookEvent>(message),
            cancellationToken);
}

public sealed class DevelopmentWebhookDurableHandler(
    DevelopmentIntegrationService service) : IDurableEventHandler
{
    public string ConsumerName => "work-item-development-webhook-v1";
    public string EventType => WorkItemDurableEventTypes.DevelopmentWebhook;

    public Task HandleAsync(
        DurableEventEnvelope message,
        CancellationToken cancellationToken) =>
        service.ProcessWebhookAsync(
            DurablePayload.Read<DevelopmentWebhookEvent>(message),
            cancellationToken);
}

public sealed class WorkItemRecurrenceDurableHandler(
    RecurringWorkItemGenerator generator) : IDurableEventHandler
{
    public string ConsumerName => "work-item-recurrence-v1";
    public string EventType => WorkItemDurableEventTypes.RecurrenceOccurrence;

    public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken) =>
        generator.GenerateAsync(DurablePayload.Read<WorkItemRecurrenceDueEvent>(message), cancellationToken);
}

public sealed class WorkItemBulkJobDurableHandler(
    WorkItemBulkJobProcessor processor,
    IUserRepository users,
    IHttpContextAccessor httpContextAccessor) : IDurableEventHandler
{
    public string ConsumerName => "work-item-bulk-job-v1";
    public string EventType => WorkItemDurableEventTypes.BulkJob;

    public async Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
    {
        var payload = DurablePayload.Read<WorkItemBulkJobDueEvent>(message);
        var actor = await users.GetByIdAsync(payload.RequestedByUserId, cancellationToken);
        var roles = actor is { IsActive: true } ? actor.Roles : [];
        var previous = httpContextAccessor.HttpContext;
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [
                new(System.Security.Claims.ClaimTypes.NameIdentifier, payload.RequestedByUserId),
                new("organizationId", payload.OrganizationId),
                .. roles.Select(role => new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role, role))
            ], "BulkJob"));
        context.TraceIdentifier = message.CorrelationId;
        httpContextAccessor.HttpContext = context;
        try
        {
            await processor.ProcessAsync(payload, cancellationToken);
        }
        finally
        {
            httpContextAccessor.HttpContext = previous;
        }
    }
}

internal static class DurablePayload
{
    internal static T Read<T>(DurableEventEnvelope message) =>
        JsonSerializer.Deserialize<T>(message.Payload)
        ?? throw new InvalidOperationException($"Durable event '{message.Id}' contains an invalid {typeof(T).Name} payload.");
}
