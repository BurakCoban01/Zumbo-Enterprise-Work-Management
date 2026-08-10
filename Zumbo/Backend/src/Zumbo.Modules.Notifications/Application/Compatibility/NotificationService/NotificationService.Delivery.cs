using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService
{
    public async Task<int> DispatchPendingEmailsAsync(
        int batchSize,
        CancellationToken ct,
        string? workerId = null)
        => await new DispatchNotificationEmailsHandler(
            notifications,
            emailSender,
            emailOptions,
            clock,
            retryJitter).HandleAsync(
                new DispatchNotificationEmailsCommand(batchSize, workerId),
                ct);

    public async Task<NotificationDeliveryMetrics> GetDeliveryMetricsAsync(
        string organizationId,
        CancellationToken ct)
        => await new GetNotificationDeliveryMetricsHandler(
            notifications,
            clock).HandleAsync(new GetNotificationDeliveryMetricsQuery(organizationId), ct);

    public async Task<IReadOnlyList<NotificationDeadLetterSummary>> ListDeadLettersAsync(
        string organizationId,
        int pageSize,
        CancellationToken ct)
        => await new ListNotificationDeadLettersHandler(notifications).HandleAsync(
            new ListNotificationDeadLettersQuery(organizationId, pageSize), ct);

    public async Task<bool> ReplayDeadLetterAsync(
        string organizationId,
        string notificationId,
        CancellationToken ct)
        => await new ReplayNotificationDeadLetterHandler(
            notifications,
            clock).HandleAsync(
                new ReplayNotificationDeadLetterCommand(organizationId, notificationId), ct);

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

    private TimeSpan RetryDelay(int attempt, int baseSeconds, int maximumSeconds, double jitterRatio)
    {
        var boundedBase = TimeSpan.FromSeconds(Math.Clamp(baseSeconds, 1, 3600));
        var boundedMaximum = TimeSpan.FromSeconds(Math.Clamp(maximumSeconds, 1, 86_400));
        if (retryJitter is not null)
        {
            return new DurableMessageRetryPolicy(
                boundedBase,
                boundedMaximum,
                Math.Clamp(jitterRatio, 0, 1),
                retryJitter).DelayForAttempt(attempt);
        }

        var exponent = Math.Min(attempt - 1, 20);
        return TimeSpan.FromSeconds(Math.Min(
            boundedMaximum.TotalSeconds,
            boundedBase.TotalSeconds * Math.Pow(2, exponent)));
    }

    private static void ClearLease(NotificationDocument notification)
    {
        notification.EmailLeaseToken = null;
        notification.EmailClaimedBy = null;
        notification.EmailLeaseUntil = null;
    }

    private static string RequireOrganizationId(string organizationId)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            throw new ValidationException("Notification organization id is required.");
        return organizationId.Trim();
    }
}
