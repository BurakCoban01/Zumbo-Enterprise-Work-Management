using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed class QueueTestDeliveryHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);

    public async Task<WebhookDeliveryResponse> HandleAsync(
        QueueTestDeliveryCommand command,
        CancellationToken ct)
    {
        var subscription = await FindOwnedAsync(command.SubscriptionId, ct);
        if (!subscription.IsActive)
        {
            throw new ConflictException(
                "WEBHOOK_SUBSCRIPTION_DISABLED",
                "Enable the webhook subscription before sending a test delivery.");
        }

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
        await audit.WriteAsync(
            "WebhookTestDeliveryQueued",
            "WebhookDelivery",
            delivery.Id,
            null,
            subscription.Id,
            string.IsNullOrWhiteSpace(command.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : command.CorrelationId,
            ct);
        return DeliveryResponseMapper.ToResponse(delivery);
    }

    private async Task<WebhookSubscriptionDocument> FindOwnedAsync(
        string id,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await subscriptions.SelectAsync(
            item => item.Id == id && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
            "WEBHOOK_SUBSCRIPTION_NOT_FOUND",
            "Webhook subscription was not found.");
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
