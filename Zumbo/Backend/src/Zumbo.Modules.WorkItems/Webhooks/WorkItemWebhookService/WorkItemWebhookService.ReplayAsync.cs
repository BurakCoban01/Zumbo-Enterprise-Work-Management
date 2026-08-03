using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookDeliveryResponse> ReplayAsync(
        string id,
        CancellationToken ct,
        string? correlationId = null)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var delivery = await deliveries.SelectAsync(
            x => x.Id == id
                && x.OrganizationId == organizationId
                && x.Status == WebhookDeliveryStatuses.DeadLetter,
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
            x => x.Id == id
                && x.OrganizationId == organizationId
                && x.Status == WebhookDeliveryStatuses.DeadLetter,
            delivery,
            ct);
        if (result.MatchedCount != 1) throw new ConflictException(
            "WEBHOOK_DELIVERY_CONFLICT", "Webhook delivery changed concurrently; retry the operation.");
        await WriteAuditAsync(
            "WebhookDeliveryReplayed",
            "WebhookDelivery",
            delivery.Id,
            oldErrorCode ?? WebhookDeliveryStatuses.DeadLetter,
            WebhookDeliveryStatuses.Pending,
            correlationId,
            ct);
        return ToResponse(delivery);
    }
}
