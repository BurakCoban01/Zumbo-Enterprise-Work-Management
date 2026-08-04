using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed class ListWebhookSubscriptionsHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IWebhookAuthorization authorization,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<WebhookSubscriptionResponse>> HandleAsync(
        ListWebhookSubscriptionsQuery query,
        CancellationToken ct)
    {
        _ = query;
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var result = new List<WebhookSubscriptionResponse>();
        string? cursor = null;
        do
        {
            var page = await subscriptions.ListByCursorAsync(
                item => item.OrganizationId == organizationId,
                cursor,
                200,
                ct);
            result.AddRange(page.Items.Select(SubscriptionResponseMapper.ToResponse));
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }
}
