using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed class DispatchDeliveriesHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookSecretProtector secretProtector,
    IWebhookTargetPolicy targetPolicy,
    IWebhookSender sender,
    IOptions<WebhookOptions> options,
    IClock clock,
    IDurableMessageJitter? retryJitter = null)
{
    public async Task<int> HandleAsync(
        DispatchDeliveriesCommand command,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return 0;
        }

        var now = clock.UtcNow;
        var candidates = await deliveries.ListByFilterAsync(
            item => (item.Status == WebhookDeliveryStatuses.Pending
                     && item.NextAttemptAtUtc <= now)
                || (item.Status == WebhookDeliveryStatuses.Processing
                    && item.LeaseUntilUtc <= now),
            item => item.NextAttemptAtUtc,
            pageSize: Math.Clamp(command.BatchSize, 1, 100),
            cancellationToken: ct);
        var delivered = 0;
        foreach (var candidate in candidates)
        {
            var token = Guid.NewGuid().ToString("N");
            candidate.Status = WebhookDeliveryStatuses.Processing;
            candidate.LeaseToken = token;
            candidate.ClaimedBy = command.WorkerId;
            candidate.LeaseUntilUtc = now.AddSeconds(
                Math.Clamp(options.Value.LeaseSeconds, 5, 900));
            candidate.UpdatedAtUtc = now;
            var claim = await deliveries.ReplaceByFilterAsync(
                item => item.Id == candidate.Id
                    && ((item.Status == WebhookDeliveryStatuses.Pending
                         && item.NextAttemptAtUtc <= now)
                        || (item.Status == WebhookDeliveryStatuses.Processing
                            && item.LeaseUntilUtc <= now)),
                candidate,
                ct);
            if (claim.MatchedCount != 1)
            {
                continue;
            }

            try
            {
                var subscription = await subscriptions.SelectAsync(
                    item => item.Id == candidate.SubscriptionId
                        && item.OrganizationId == candidate.OrganizationId
                        && item.IsActive,
                    ct) ?? throw new WebhookDeliveryException(
                    "SUBSCRIPTION_UNAVAILABLE");
                await targetPolicy.ValidateAsync(candidate.TargetUrl, ct);
                var timestamp = clock.UtcNow.ToUnixTimeSeconds();
                var signature = Sign(
                    secretProtector.Unprotect(subscription.CurrentSecretProtected),
                    timestamp,
                    candidate.Payload);
                string? previousSignature = null;
                int? previousVersion = null;
                if (subscription.PreviousSecretValidUntilUtc > clock.UtcNow
                    && subscription.PreviousSecretProtected is not null)
                {
                    previousVersion = subscription.PreviousSecretVersion;
                    previousSignature = Sign(
                        secretProtector.Unprotect(
                            subscription.PreviousSecretProtected),
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
                    item => item.Id == candidate.Id
                        && item.Status == WebhookDeliveryStatuses.Processing
                        && item.LeaseToken == token,
                    candidate,
                    ct);
                if (result.MatchedCount == 1)
                {
                    delivered++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await FailAsync(candidate, token, exception, ct);
            }
        }

        return delivered;
    }

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
            delivery.NextAttemptAtUtc = clock.UtcNow.Add(
                RetryDelay(delivery.Attempts));
        }

        ClearLease(delivery);
        await deliveries.ReplaceByFilterAsync(
            item => item.Id == delivery.Id
                && item.Status == WebhookDeliveryStatuses.Processing
                && item.LeaseToken == leaseToken,
            delivery,
            ct);
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var baseDelay = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.BaseRetrySeconds, 1, 3600));
        var maximumDelay = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.MaximumRetrySeconds, 1, 86_400));
        if (retryJitter is not null)
        {
            return new DurableMessageRetryPolicy(
                baseDelay,
                maximumDelay,
                Math.Clamp(options.Value.RetryJitterRatio, 0, 1),
                retryJitter).DelayForAttempt(attempt);
        }

        var exponent = Math.Min(attempt - 1, 20);
        return TimeSpan.FromSeconds(Math.Min(
            baseDelay.TotalSeconds * Math.Pow(2, exponent),
            maximumDelay.TotalSeconds));
    }

    private static string Sign(
        string secret,
        long timestampUnixSeconds,
        string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{timestampUnixSeconds}.{payload}")))
            .ToLowerInvariant();
    }

    private static void ClearLease(WebhookDeliveryDocument document)
    {
        document.LeaseToken = null;
        document.ClaimedBy = null;
        document.LeaseUntilUtc = null;
    }
}
