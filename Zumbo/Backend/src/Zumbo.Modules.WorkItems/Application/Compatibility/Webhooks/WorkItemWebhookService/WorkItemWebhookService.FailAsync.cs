using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private async Task FailAsync(
        WebhookDeliveryDocument delivery,
        string leaseToken,
        Exception exception,
        CancellationToken ct)
    {
        delivery.Attempts++;
        delivery.LastErrorCode = exception is WebhookDeliveryException known
            ? known.SafeCode
            : "DELIVERY_FAILED";
        delivery.UpdatedAtUtc = clock.UtcNow;
        if (delivery.Attempts >= Math.Clamp(options.Value.MaximumAttempts, 1, 20))
        {
            delivery.Status = WebhookDeliveryStatuses.DeadLetter;
            delivery.DeadLetteredAtUtc = clock.UtcNow;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatuses.Pending;
            delivery.NextAttemptAtUtc = clock.UtcNow.Add(RetryDelay(delivery.Attempts));
        }
        ClearLease(delivery);
        await deliveries.ReplaceByFilterAsync(
            x => x.Id == delivery.Id
                && x.Status == WebhookDeliveryStatuses.Processing
                && x.LeaseToken == leaseToken,
            delivery,
            ct);
    }
}
