using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService
{
    public async Task<WebhookDeliveryMetrics> GetMetricsAsync(CancellationToken ct)
        => await metricsHandler.HandleAsync(new GetWebhookDeliveryMetricsQuery(), ct);

    public async Task<WebhookDeliveryPage> ListDeliveriesAsync(
        string subscriptionId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
        => await listDeliveriesHandler.HandleAsync(
            new ListWebhookDeliveriesQuery(subscriptionId, cursor, pageSize),
            ct);

    public async Task<WebhookDeliveryResponse> GetDeliveryAsync(string id, CancellationToken ct)
        => await getDeliveryHandler.HandleAsync(new GetWebhookDeliveryQuery(id), ct);

    public async Task<WebhookDeliveryResponse> ReplayAsync(
        string id,
        CancellationToken ct,
        string? correlationId = null)
        => await replayHandler.HandleAsync(
            new ReplayWebhookDeliveryCommand(id, correlationId),
            ct);

    public async Task<WebhookDeliveryResponse> QueueTestDeliveryAsync(
        string id,
        CancellationToken ct,
        string? correlationId = null)
    {
        return await queueTestDeliveryHandler.HandleAsync(
            new QueueTestDeliveryCommand(id, correlationId),
            ct);
    }

    public async Task QueueAsync(
        string sourceEventId,
        string organizationId,
        WorkItemWebhookEvent message,
        CancellationToken ct)
    {
        await queueDeliveryHandler.HandleAsync(
            new QueueDeliveryCommand(sourceEventId, organizationId, message),
            ct);
    }

    public async Task<int> DispatchAsync(int batchSize, string workerId, CancellationToken ct)
    {
        return await dispatchDeliveriesHandler.HandleAsync(
            new DispatchDeliveriesCommand(batchSize, workerId),
            ct);
    }

    private static void ClearLease(WebhookDeliveryDocument document)
    {
        document.LeaseToken = null;
        document.ClaimedBy = null;
        document.LeaseUntilUtc = null;
    }

    private static NotFoundException DeliveryNotFound() => new(
        "WEBHOOK_DELIVERY_NOT_FOUND", "Webhook delivery was not found.");

    private async Task FailAsync(
        WebhookDeliveryDocument delivery,
        string leaseToken,
        Exception exception,
        CancellationToken ct)
    {
        delivery.Attempts++;
        delivery.LastErrorCode = exception is WebhookDeliveryException known
            ? known.SafeCode
            : "DELIVERY_FAILED";
        delivery.UpdatedAtUtc = clock.UtcNow;
        if (delivery.Attempts >= Math.Clamp(options.Value.MaximumAttempts, 1, 20))
        {
            delivery.Status = WebhookDeliveryStatuses.DeadLetter;
            delivery.DeadLetteredAtUtc = clock.UtcNow;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatuses.Pending;
            delivery.NextAttemptAtUtc = clock.UtcNow.Add(RetryDelay(delivery.Attempts));
        }
        ClearLease(delivery);
        await deliveries.ReplaceByFilterAsync(
            x => x.Id == delivery.Id
                && x.Status == WebhookDeliveryStatuses.Processing
                && x.LeaseToken == leaseToken,
            delivery,
            ct);
    }

    private async Task<IReadOnlyList<WebhookSubscriptionDocument>> ListActiveSubscriptionsAsync(
        string organizationId,
        CancellationToken ct)
    {
        var result = new List<WebhookSubscriptionDocument>();
        string? cursor = null;
        do
        {
            var page = await subscriptions.ListByCursorAsync(
                x => x.OrganizationId == organizationId && x.IsActive,
                cursor,
                200,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Clamp(options.Value.BaseRetrySeconds, 1, 3600));
        var maximumDelay = TimeSpan.FromSeconds(Math.Clamp(options.Value.MaximumRetrySeconds, 1, 86_400));
        if (retryJitter is not null)
        {
            return new DurableMessageRetryPolicy(
                baseDelay,
                maximumDelay,
                Math.Clamp(options.Value.RetryJitterRatio, 0, 1),
                retryJitter).DelayForAttempt(attempt);
        }

        var exponent = Math.Min(attempt - 1, 20);
        return TimeSpan.FromSeconds(Math.Min(
            baseDelay.TotalSeconds * Math.Pow(2, exponent),
            maximumDelay.TotalSeconds));
    }

    private static WebhookDeliveryResponse ToResponse(WebhookDeliveryDocument document) => new(
        document.Id,
        document.SubscriptionId,
        document.EventScope,
        document.PayloadSha256,
        document.Status,
        document.Attempts,
        document.NextAttemptAtUtc,
        document.LastErrorCode,
        document.DeliveredAtUtc,
        document.DeadLetteredAtUtc,
        document.CreatedAtUtc,
        document.Version);
}
