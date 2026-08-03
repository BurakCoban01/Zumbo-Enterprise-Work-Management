using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookDeliveryPage> ListDeliveriesAsync(
        string subscriptionId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        await FindOwnedAsync(subscriptionId, ct);
        var organizationId = RequireOrganization();
        var page = await deliveries.ListByCursorAsync(
            x => x.OrganizationId == organizationId && x.SubscriptionId == subscriptionId,
            string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim(),
            Math.Clamp(pageSize, 1, 100),
            ct);
        return new WebhookDeliveryPage(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }
}
