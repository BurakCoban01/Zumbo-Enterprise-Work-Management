using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookDeliveryMetrics> GetMetricsAsync(CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var pending = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Pending, ct);
        var processing = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Processing, ct);
        var delivered = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Delivered, ct);
        var deadLetter = await deliveries.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.DeadLetter, ct);
        var oldest = (await deliveries.ListByFilterAsync(
            x => x.OrganizationId == organizationId && x.Status == WebhookDeliveryStatuses.Pending,
            x => x.NextAttemptAtUtc,
            pageSize: 1,
            cancellationToken: ct)).SingleOrDefault();
        return new WebhookDeliveryMetrics(
            pending, processing, delivered, deadLetter, oldest?.NextAttemptAtUtc, clock.UtcNow);
    }
}
