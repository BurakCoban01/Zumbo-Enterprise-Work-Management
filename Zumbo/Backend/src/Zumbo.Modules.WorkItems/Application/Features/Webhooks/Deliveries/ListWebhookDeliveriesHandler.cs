using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed class ListWebhookDeliveriesHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookAuthorization authorization,
    ICurrentUser currentUser)
{
    public async Task<WebhookDeliveryPage> HandleAsync(
        ListWebhookDeliveriesQuery query,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        _ = await subscriptions.SelectAsync(
            item => item.Id == query.SubscriptionId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "WEBHOOK_SUBSCRIPTION_NOT_FOUND",
                "Webhook subscription was not found.");
        var page = await deliveries.ListByCursorAsync(
            item => item.OrganizationId == organizationId
                && item.SubscriptionId == query.SubscriptionId,
            string.IsNullOrWhiteSpace(query.Cursor) ? null : query.Cursor.Trim(),
            Math.Clamp(query.PageSize, 1, 100),
            ct);
        return new WebhookDeliveryPage(
            page.Items.Select(DeliveryResponseMapper.ToResponse).ToList(),
            page.NextCursor);
    }
}
