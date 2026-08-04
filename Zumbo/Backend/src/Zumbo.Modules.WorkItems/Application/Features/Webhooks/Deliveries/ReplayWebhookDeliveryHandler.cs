using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed class ReplayWebhookDeliveryHandler(
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<WebhookDeliveryResponse> HandleAsync(
        ReplayWebhookDeliveryCommand command,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var delivery = await deliveries.SelectAsync(
            item => item.Id == command.DeliveryId
                && item.OrganizationId == organizationId
                && item.Status == WebhookDeliveryStatuses.DeadLetter,
            ct) ?? throw DeliveryNotFound();
        var oldErrorCode = delivery.LastErrorCode;
        delivery.Status = WebhookDeliveryStatuses.Pending;
        delivery.Attempts = 0;
        delivery.NextAttemptAtUtc = clock.UtcNow;
        delivery.LastErrorCode = null;
        delivery.DeadLetteredAtUtc = null;
        delivery.UpdatedAtUtc = clock.UtcNow;
        ClearLease(delivery);
        var result = await deliveries.ReplaceByFilterAsync(
            item => item.Id == command.DeliveryId
                && item.OrganizationId == organizationId
                && item.Status == WebhookDeliveryStatuses.DeadLetter,
            delivery,
            ct);
        if (result.MatchedCount != 1)
        {
            throw new ConflictException(
                "WEBHOOK_DELIVERY_CONFLICT",
                "Webhook delivery changed concurrently; retry the operation.");
        }

        await audit.WriteAsync(
            "WebhookDeliveryReplayed",
            "WebhookDelivery",
            delivery.Id,
            oldErrorCode ?? WebhookDeliveryStatuses.DeadLetter,
            WebhookDeliveryStatuses.Pending,
            string.IsNullOrWhiteSpace(command.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : command.CorrelationId,
            ct);
        return DeliveryResponseMapper.ToResponse(delivery);
    }

    private static NotFoundException DeliveryNotFound() => new(
        "WEBHOOK_DELIVERY_NOT_FOUND",
        "Webhook delivery was not found.");

    private static void ClearLease(WebhookDeliveryDocument document)
    {
        document.LeaseToken = null;
        document.ClaimedBy = null;
        document.LeaseUntilUtc = null;
    }
}
