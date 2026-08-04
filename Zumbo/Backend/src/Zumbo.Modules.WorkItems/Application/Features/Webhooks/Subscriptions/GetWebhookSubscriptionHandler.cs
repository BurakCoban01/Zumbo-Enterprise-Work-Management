using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed class GetWebhookSubscriptionHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IWebhookAuthorization authorization,
    ICurrentUser currentUser)
{
    public async Task<WebhookSubscriptionResponse> HandleAsync(
        GetWebhookSubscriptionQuery query,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var document = await subscriptions.SelectAsync(
            item => item.Id == query.SubscriptionId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "WEBHOOK_SUBSCRIPTION_NOT_FOUND",
                "Webhook subscription was not found.");
        return SubscriptionResponseMapper.ToResponse(document);
    }
}
