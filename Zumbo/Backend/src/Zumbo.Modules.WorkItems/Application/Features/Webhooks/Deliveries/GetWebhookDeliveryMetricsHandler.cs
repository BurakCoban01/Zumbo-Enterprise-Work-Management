using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed class GetWebhookDeliveryMetricsHandler(
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookAuthorization authorization,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<WebhookDeliveryMetrics> HandleAsync(
        GetWebhookDeliveryMetricsQuery query,
        CancellationToken ct)
    {
        _ = query;
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var pending = await deliveries.CountByFilterAsync(
            item => item.OrganizationId == organizationId
                && item.Status == WebhookDeliveryStatuses.Pending,
            ct);
        var processing = await deliveries.CountByFilterAsync(
            item => item.OrganizationId == organizationId
                && item.Status == WebhookDeliveryStatuses.Processing,
            ct);
        var delivered = await deliveries.CountByFilterAsync(
            item => item.OrganizationId == organizationId
                && item.Status == WebhookDeliveryStatuses.Delivered,
            ct);
        var deadLetter = await deliveries.CountByFilterAsync(
            item => item.OrganizationId == organizationId
                && item.Status == WebhookDeliveryStatuses.DeadLetter,
            ct);
        var oldest = (await deliveries.ListByFilterAsync(
            item => item.OrganizationId == organizationId
                && item.Status == WebhookDeliveryStatuses.Pending,
            item => item.NextAttemptAtUtc,
            pageSize: 1,
            cancellationToken: ct)).SingleOrDefault();
        return new WebhookDeliveryMetrics(
            pending,
            processing,
            delivered,
            deadLetter,
            oldest?.NextAttemptAtUtc,
            clock.UtcNow);
    }
}
