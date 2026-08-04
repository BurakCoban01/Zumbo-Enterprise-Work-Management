using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentWebhookResult> ReceiveWebhookAsync(
        string connectionId,
        DevelopmentWebhookRequest request,
        CancellationToken ct)
    {
        if (request.Payload.Length is < 1
            || request.Payload.Length > DevelopmentIntegrationLimits.MaximumWebhookPayloadBytes)
        {
            throw new ValidationException("Development webhook payload size is not supported.");
        }
        var deliveryId = Required(request.DeliveryId, "Webhook delivery id", 200);
        var eventName = Required(request.EventName, "Webhook event name", 120);
        var connection = await connections.SelectAsync(
            item => item.Id == connectionId && item.IsConnected,
            ct) ?? throw new UnauthorizedException("Development webhook could not be verified.");
        if (!VerifyWebhook(connection, request))
            throw new UnauthorizedException("Development webhook could not be verified.");

        var now = clock.UtcNow;
        _ = await receipts.DeleteByFilterAsync(
            item => item.ConnectionId == connection.Id
                && item.ExpiresAtUtc <= now.UtcDateTime,
            ct);
        var receiptId = StableId(connection.Id, deliveryId);
        var normalized = DevelopmentWebhookSecurity.Normalize(
            connection.Provider,
            eventName,
            request.Payload);
        var receipt = new DevelopmentWebhookReceiptDocument
        {
            Id = receiptId,
            OrganizationId = connection.OrganizationId,
            ConnectionId = connection.Id,
            DeliveryId = deliveryId,
            ProviderEvent = eventName,
            PayloadSha256 = Hash(request.Payload),
            ReceivedAtUtc = now,
            ExpiresAtUtc = now.AddDays(
                DevelopmentIntegrationLimits.DeliveryRetentionDays).UtcDateTime
        };
        try
        {
            await receipts.CreateAsync(receipt, ct);
        }
        catch (DocumentConflictException)
        {
            var existing = await receipts.SelectAsync(
                item => item.Id == receiptId
                    && item.ConnectionId == connection.Id,
                ct);
            if (existing is null
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(existing.PayloadSha256),
                    Encoding.ASCII.GetBytes(receipt.PayloadSha256)))
            {
                throw new ConflictException(
                    "DEVELOPMENT_WEBHOOK_DELIVERY_COLLISION",
                    "The webhook delivery id was already used with different content.");
            }
            return new DevelopmentWebhookResult("Duplicate", 0, true);
        }

        await webhookQueue.EnqueueAsync(new DevelopmentWebhookEvent(
            receipt.Id,
            connection.Id,
            connection.LifecycleVersion,
            connection.OrganizationId,
            deliveryId,
            eventName,
            normalized), ct);
        return new DevelopmentWebhookResult("Accepted", 0, false);
    }

}
