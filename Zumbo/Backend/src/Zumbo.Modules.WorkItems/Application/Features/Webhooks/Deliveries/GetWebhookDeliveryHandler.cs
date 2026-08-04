using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed class GetWebhookDeliveryHandler(
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookAuthorization authorization,
    ICurrentUser currentUser)
{
    public async Task<WebhookDeliveryResponse> HandleAsync(
        GetWebhookDeliveryQuery query,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var delivery = await deliveries.SelectAsync(
            item => item.Id == query.DeliveryId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "WEBHOOK_DELIVERY_NOT_FOUND",
                "Webhook delivery was not found.");
        return DeliveryResponseMapper.ToResponse(delivery);
    }
}
