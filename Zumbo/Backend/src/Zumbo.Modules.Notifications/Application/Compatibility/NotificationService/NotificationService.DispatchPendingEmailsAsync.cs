using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task<int> DispatchPendingEmailsAsync(
        int batchSize,
        CancellationToken ct,
        string? workerId = null)
    {
        var configuration = emailOptions.Value;
        var now = clock.UtcNow;
        var owner = string.IsNullOrWhiteSpace(workerId)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : workerId.Trim();
        var candidates = await notifications.ListByFilterAsync(
            x => (x.EmailStatus == NotificationEmailStatuses.Pending && x.EmailNextAttemptAt <= now)
                || (x.EmailStatus == NotificationEmailStatuses.Processing && x.EmailLeaseUntil <= now),
            x => x.EmailNextAttemptAt!,
            pageSize: Math.Clamp(batchSize, 1, 100),
            cancellationToken: ct);
        var claimed = new List<NotificationDocument>();
        foreach (var candidate in candidates)
        {
            var token = Guid.NewGuid().ToString("N");
            candidate.EmailStatus = NotificationEmailStatuses.Processing;
            candidate.EmailLeaseToken = token;
            candidate.EmailClaimedBy = owner;
            candidate.EmailLeaseUntil = now.AddSeconds(Math.Clamp(configuration.LeaseSeconds, 5, 900));
            var result = await notifications.ReplaceByFilterAsync(
                x => x.Id == candidate.Id
                    && ((x.EmailStatus == NotificationEmailStatuses.Pending && x.EmailNextAttemptAt <= now)
                        || (x.EmailStatus == NotificationEmailStatuses.Processing && x.EmailLeaseUntil <= now)),
                candidate,
                ct);
            if (result.MatchedCount == 1) claimed.Add(candidate);
        }

        var delivered = 0;
        var groups = claimed.GroupBy(notification =>
            notification.EmailDeliveryMode == NotificationDeliveryModes.DailyDigest
                ? $"digest:{notification.OrganizationId}:{notification.UserId}"
                : notification.Id,
            StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var deliveries = group.ToList();
            try
            {
                var digest = deliveries.Count > 1
                    || deliveries[0].EmailDeliveryMode == NotificationDeliveryModes.DailyDigest;
                await emailSender.SendAsync(
                    deliveries[0].EmailAddress!,
                    digest ? "Zumbo: Daily digest" : $"Zumbo: {deliveries[0].Type}",
                    digest
                        ? string.Join(Environment.NewLine, deliveries.Select(x => $"[{x.Type}] {x.Message}"))
                        : deliveries[0].Message,
                    ct);
                foreach (var notification in deliveries)
                {
                    var leaseToken = notification.EmailLeaseToken;
                    notification.EmailStatus = NotificationEmailStatuses.Sent;
                    notification.EmailSentAt = clock.UtcNow;
                    notification.EmailLastError = null;
                    ClearLease(notification);
                    var result = await notifications.ReplaceByFilterAsync(
                        x => x.Id == notification.Id
                            && x.EmailStatus == NotificationEmailStatuses.Processing
                            && x.EmailLeaseToken == leaseToken,
                        notification,
                        ct);
                    if (result.MatchedCount == 1) delivered++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var notification in deliveries)
                    await FailDeliveryAsync(notification, ex, configuration, ct);
            }
        }

        return delivered;
    }
}
