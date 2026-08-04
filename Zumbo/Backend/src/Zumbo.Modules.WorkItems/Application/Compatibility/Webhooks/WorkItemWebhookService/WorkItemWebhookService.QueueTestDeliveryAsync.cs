using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookDeliveryResponse> QueueTestDeliveryAsync(
        string id,
        CancellationToken ct,
        string? correlationId = null)
    {
        var subscription = await FindOwnedAsync(id, ct);
        if (!subscription.IsActive)
            throw new ConflictException(
                "WEBHOOK_SUBSCRIPTION_DISABLED",
                "Enable the webhook subscription before sending a test delivery.");

        var now = clock.UtcNow;
        var deliveryId = Hash($"{subscription.Id}:test:{Guid.NewGuid():N}");
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            specVersion = "1.0",
            id = deliveryId,
            type = "webhook.test",
            source = "zumbo/integrations",
            subject = $"webhooks/{subscription.Id}",
            time = now,
            tenantId = subscription.OrganizationId,
            data = new { test = true }
        }, JsonOptions);
        var delivery = await deliveries.CreateAsync(new WebhookDeliveryDocument
        {
            Id = deliveryId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.Id,
            SourceEventId = deliveryId,
            EventScope = "webhook.test",
            TargetUrl = subscription.TargetUrl,
            Payload = payload,
            PayloadSha256 = Hash(payload),
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await WriteAuditAsync(
            "WebhookTestDeliveryQueued",
            "WebhookDelivery",
            delivery.Id,
            null,
            subscription.Id,
            correlationId,
            ct);
        return ToResponse(delivery);
    }
}
