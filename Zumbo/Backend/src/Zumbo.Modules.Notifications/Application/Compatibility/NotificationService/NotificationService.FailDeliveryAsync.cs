using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private async Task FailDeliveryAsync(
        NotificationDocument notification,
        Exception exception,
        EmailNotificationOptions configuration,
        CancellationToken ct)
    {
        var leaseToken = notification.EmailLeaseToken;
        notification.EmailAttempts++;
        notification.EmailLastError = exception.Message.Length > 500
            ? exception.Message[..500]
            : exception.Message;
        var deadLetter = notification.EmailAttempts >= Math.Clamp(configuration.MaxAttempts, 1, 20);
        notification.EmailStatus = deadLetter
            ? NotificationEmailStatuses.DeadLetter
            : NotificationEmailStatuses.Pending;
        notification.EmailDeadLetteredAt = deadLetter ? clock.UtcNow : null;
        var retryDelay = RetryDelay(
            notification.EmailAttempts,
            configuration.BaseRetrySeconds,
            configuration.MaximumRetrySeconds,
            configuration.RetryJitterRatio);
        notification.EmailNextAttemptAt = deadLetter ? null : clock.UtcNow.Add(retryDelay);
        ClearLease(notification);
        await notifications.ReplaceByFilterAsync(
            x => x.Id == notification.Id
                && x.EmailStatus == NotificationEmailStatuses.Processing
                && x.EmailLeaseToken == leaseToken,
            notification,
            ct);
    }
}
