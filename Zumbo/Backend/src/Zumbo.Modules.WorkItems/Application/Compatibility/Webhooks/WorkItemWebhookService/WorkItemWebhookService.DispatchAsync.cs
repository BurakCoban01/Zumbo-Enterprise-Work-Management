using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<int> DispatchAsync(int batchSize, string workerId, CancellationToken ct)
    {
        if (!options.Value.Enabled) return 0;
        var now = clock.UtcNow;
        var candidates = await deliveries.ListByFilterAsync(
            x => (x.Status == WebhookDeliveryStatuses.Pending && x.NextAttemptAtUtc <= now)
                || (x.Status == WebhookDeliveryStatuses.Processing && x.LeaseUntilUtc <= now),
            x => x.NextAttemptAtUtc,
            pageSize: Math.Clamp(batchSize, 1, 100),
            cancellationToken: ct);
        var delivered = 0;
        foreach (var candidate in candidates)
        {
            var token = Guid.NewGuid().ToString("N");
            candidate.Status = WebhookDeliveryStatuses.Processing;
            candidate.LeaseToken = token;
            candidate.ClaimedBy = workerId;
            candidate.LeaseUntilUtc = now.AddSeconds(Math.Clamp(options.Value.LeaseSeconds, 5, 900));
            candidate.UpdatedAtUtc = now;
            var claim = await deliveries.ReplaceByFilterAsync(
                x => x.Id == candidate.Id
                    && ((x.Status == WebhookDeliveryStatuses.Pending && x.NextAttemptAtUtc <= now)
                        || (x.Status == WebhookDeliveryStatuses.Processing && x.LeaseUntilUtc <= now)),
                candidate,
                ct);
            if (claim.MatchedCount != 1) continue;

            try
            {
                var subscription = await subscriptions.SelectAsync(
                    x => x.Id == candidate.SubscriptionId
                        && x.OrganizationId == candidate.OrganizationId
                        && x.IsActive,
                    ct) ?? throw new WebhookDeliveryException("SUBSCRIPTION_UNAVAILABLE");
                await targetPolicy.ValidateAsync(candidate.TargetUrl, ct);
                var timestamp = clock.UtcNow.ToUnixTimeSeconds();
                var signature = Sign(secretProtector.Unprotect(subscription.CurrentSecretProtected), timestamp, candidate.Payload);
                string? previousSignature = null;
                int? previousVersion = null;
                if (subscription.PreviousSecretValidUntilUtc > clock.UtcNow
                    && subscription.PreviousSecretProtected is not null)
                {
                    previousVersion = subscription.PreviousSecretVersion;
                    previousSignature = Sign(
                        secretProtector.Unprotect(subscription.PreviousSecretProtected),
                        timestamp,
                        candidate.Payload);
                }

                await sender.SendAsync(new WebhookSendRequest(
                    candidate.TargetUrl,
                    candidate.Payload,
                    candidate.Id,
                    timestamp,
                    subscription.SecretVersion,
                    signature,
                    previousVersion,
                    previousSignature), ct);
                candidate.Status = WebhookDeliveryStatuses.Delivered;
                candidate.DeliveredAtUtc = clock.UtcNow;
                candidate.LastErrorCode = null;
                candidate.UpdatedAtUtc = clock.UtcNow;
                ClearLease(candidate);
                var result = await deliveries.ReplaceByFilterAsync(
                    x => x.Id == candidate.Id
                        && x.Status == WebhookDeliveryStatuses.Processing
                        && x.LeaseToken == token,
                    candidate,
                    ct);
                if (result.MatchedCount == 1) delivered++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await FailAsync(candidate, token, exception, ct);
            }
        }
        return delivered;
    }
}
