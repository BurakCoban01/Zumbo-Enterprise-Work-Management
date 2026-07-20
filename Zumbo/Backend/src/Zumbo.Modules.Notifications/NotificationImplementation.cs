using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService(
    IDocumentRepository<NotificationDocument> notifications,
    IDocumentRepository<NotificationPreferenceDocument> preferences,
    INotificationUserDirectory userDirectory,
    IEmailNotificationSender emailSender,
    IOptions<EmailNotificationOptions> emailOptions,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IDurableMessageJitter? retryJitter = null)
{
    public async Task NotifyAsync(
        string userId,
        string type,
        string message,
        CancellationToken ct,
        string? deduplicationKey = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var normalizedType = NormalizeType(type);
        var normalizedMessage = NormalizeMessage(message);
        var user = await userDirectory.FindAsync(userId, ct);
        if (user is null || !user.IsActive)
        {
            return;
        }
        var preference = await preferences.SelectAsync(x => x.UserId == userId, ct);
        if (preference?.MutedTypes.Contains(normalizedType, StringComparer.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var normalizedDeduplicationKey = NormalizeDeduplicationKey(deduplicationKey);
        if (normalizedDeduplicationKey is not null)
        {
            await using var dedupeLock = await AcquireLockAsync(
                $"notification-dedupe:{user.OrganizationId}:{normalizedDeduplicationKey}", ct);
            if (await notifications.SelectAsync(
                    x => x.OrganizationId == user.OrganizationId
                        && x.DeduplicationKey == normalizedDeduplicationKey,
                    ct) is not null)
            {
                return;
            }

            await CreateNotificationAsync(
                user, normalizedType, normalizedMessage, preference, normalizedDeduplicationKey, ct);
            return;
        }

        await CreateNotificationAsync(user, normalizedType, normalizedMessage, preference, null, ct);
    }

    public async Task<IReadOnlyList<NotificationResponse>> ListAsync(
        string userId,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50,
        bool unreadOnly = false)
    {
        EnsureCurrentUser(userId);
        ListNotificationsValidator.Validate(new ListNotificationsQuery(userId, page, pageSize, unreadOnly));

        var result = await notifications.ListByFilterAsync(
            x => x.UserId == userId && (!unreadOnly || !x.Read),
            x => x.CreatedAt,
            orderDescending: true,
            page: page,
            pageSize: pageSize,
            cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public async Task MarkAsReadAsync(string notificationId, CancellationToken ct)
    {
        var userId = RequireCurrentUser();
        var notification = await notifications.SelectAsync(x => x.Id == notificationId && x.UserId == userId, ct)
            ?? throw new NotFoundException("NOTIFICATION_NOT_FOUND", "Notification was not found.");
        if (!notification.Read)
        {
            await notifications.UpdateOneFieldByFilterAsync(
                x => x.Id == notificationId && x.UserId == userId,
                x => x.Read,
                true,
                ct);
        }
    }

    public async Task<NotificationPreferenceResponse> GetPreferencesAsync(CancellationToken ct)
    {
        var userId = RequireCurrentUser();
        var preference = await preferences.SelectAsync(x => x.UserId == userId, ct);
        return preference is null
            ? new NotificationPreferenceResponse(true, true, [], [], 0)
            : ToResponse(preference);
    }

    public async Task<NotificationPreferenceResponse> UpdatePreferencesAsync(
        UpdateNotificationPreferencesRequest request,
        CancellationToken ct)
    {
        var userId = RequireCurrentUser();
        var mutedTypes = (request.MutedTypes ?? [])
            .Select(NormalizeType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mutedTypes.Count > 20)
        {
            throw new ValidationException("At most 20 notification types can be muted.");
        }

        var typeSettings = (request.TypeSettings ?? [])
            .Select(setting => new NotificationTypePreferenceDocument
            {
                Type = NormalizeType(setting.Type),
                InAppEnabled = setting.InAppEnabled,
                EmailEnabled = setting.EmailEnabled
            })
            .ToList();
        if (typeSettings.Count > 20
            || typeSettings.Select(setting => setting.Type).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != typeSettings.Count)
        {
            throw new ValidationException(
                "Notification type settings must contain at most 20 unique types.");
        }
        var deliveryMode = NormalizeDeliveryMode(request.DeliveryMode);
        var timeZoneId = NormalizeTimeZone(request.TimeZoneId);
        if (request.DigestHourLocal is < 0 or > 23)
            throw new ValidationException("Notification digest hour must be between 0 and 23.");

        await using var preferenceLock = await AcquireLockAsync("notification-preference:" + userId, ct);
        var existing = await preferences.SelectAsync(x => x.UserId == userId, ct);
        var preference = existing ?? new NotificationPreferenceDocument
        {
            Id = userId,
            UserId = userId
        };
        preference.InAppEnabled = request.InAppEnabled;
        preference.EmailEnabled = request.EmailEnabled;
        preference.MutedTypes = mutedTypes;
        preference.TypeSettings = typeSettings;
        preference.DeliveryMode = deliveryMode;
        preference.TimeZoneId = timeZoneId;
        preference.DigestHourLocal = request.DigestHourLocal;
        preference.UpdatedAt = clock.UtcNow;
        if (existing is null)
        {
            preference = await preferences.CreateAsync(preference, ct);
        }
        else
        {
            var result = await preferences.ReplaceByVersionAsync(
                x => x.Id == preference.Id,
                preference,
                preference.Version,
                ct);
            if (!result.Found)
            {
                throw new ConflictException(
                    "NOTIFICATION_PREFERENCE_CONFLICT",
                    "Notification preferences changed concurrently; retry the operation.");
            }
            preference.Version = result.Version!.Value;
        }

        return ToResponse(preference);
    }

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

    public async Task<NotificationDeliveryMetrics> GetDeliveryMetricsAsync(
        string organizationId,
        CancellationToken ct)
    {
        organizationId = RequireOrganizationId(organizationId);
        var pending = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.EmailStatus == NotificationEmailStatuses.Pending, ct);
        var processing = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.EmailStatus == NotificationEmailStatuses.Processing, ct);
        var sent = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.EmailStatus == NotificationEmailStatuses.Sent, ct);
        var deadLetter = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.EmailStatus == NotificationEmailStatuses.DeadLetter, ct);
        var disabled = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.EmailStatus == NotificationEmailStatuses.Disabled, ct);
        var oldest = (await notifications.ListByFilterAsync(
            x => x.OrganizationId == organizationId && x.EmailStatus == NotificationEmailStatuses.Pending,
            x => x.EmailNextAttemptAt!,
            pageSize: 1,
            cancellationToken: ct)).SingleOrDefault();
        return new NotificationDeliveryMetrics(
            organizationId, pending, processing, sent, deadLetter, disabled,
            oldest?.EmailNextAttemptAt, clock.UtcNow);
    }

    public async Task<bool> ReplayDeadLetterAsync(
        string organizationId,
        string notificationId,
        CancellationToken ct)
    {
        organizationId = RequireOrganizationId(organizationId);
        var notification = await notifications.SelectAsync(
            x => x.Id == notificationId
                && x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            ct);
        if (notification is null) return false;
        notification.EmailStatus = NotificationEmailStatuses.Pending;
        notification.EmailAttempts = 0;
        notification.EmailNextAttemptAt = clock.UtcNow;
        notification.EmailDeadLetteredAt = null;
        notification.EmailLastError = null;
        ClearLease(notification);
        var result = await notifications.ReplaceByFilterAsync(
            x => x.Id == notificationId
                && x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            notification,
            ct);
        return result.MatchedCount == 1;
    }

    private async Task CreateNotificationAsync(
        NotificationUser user,
        string type,
        string message,
        NotificationPreferenceDocument? preference,
        string? deduplicationKey,
        CancellationToken ct)
    {
        var typeSetting = preference?.TypeSettings.SingleOrDefault(
            setting => setting.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        var inAppEnabled = (preference?.InAppEnabled ?? true)
            && (typeSetting?.InAppEnabled ?? true);
        var emailEnabled = (preference?.EmailEnabled ?? true)
            && (typeSetting?.EmailEnabled ?? true)
            && emailOptions.Value.Enabled;
        if (!inAppEnabled && !emailEnabled)
        {
            return;
        }

        var deliveryMode = preference?.DeliveryMode ?? NotificationDeliveryModes.Immediate;
        var nextAttempt = emailEnabled
            ? deliveryMode == NotificationDeliveryModes.DailyDigest
                ? NextDigestAt(clock.UtcNow, preference?.TimeZoneId ?? "UTC", preference?.DigestHourLocal ?? 8)
                : clock.UtcNow
            : (DateTimeOffset?)null;
        await notifications.CreateAsync(new NotificationDocument
        {
            OrganizationId = user.OrganizationId,
            UserId = user.Id,
            Type = type,
            Message = message,
            Read = !inAppEnabled,
            EmailAddress = emailEnabled ? user.Email : null,
            EmailStatus = emailEnabled ? NotificationEmailStatuses.Pending : NotificationEmailStatuses.Disabled,
            EmailDeliveryMode = deliveryMode,
            EmailNextAttemptAt = nextAttempt,
            DeduplicationKey = deduplicationKey,
            CreatedAt = clock.UtcNow
        }, ct);
    }

    private void EnsureCurrentUser(string userId)
    {
        if (!string.Equals(RequireCurrentUser(), userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Users can only access their own notifications.");
        }
    }

    private string RequireCurrentUser() =>
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("NOTIFICATION_RESOURCE_BUSY", "Notification resource is busy; retry the operation.");
    }

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new ValidationException("Notification type is required.");
        var normalized = type.Trim();
        if (normalized.Length > 50) throw new ValidationException("Notification type cannot exceed 50 characters.");
        return normalized;
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ValidationException("Notification message is required.");
        var normalized = message.Trim();
        if (normalized.Length > 2000) throw new ValidationException("Notification message cannot exceed 2000 characters.");
        return normalized;
    }

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

    private static string? NormalizeDeduplicationKey(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > 200)
            throw new ValidationException("Notification deduplication key cannot exceed 200 characters.");
        return normalized;
    }

    private static string NormalizeDeliveryMode(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? NotificationDeliveryModes.Immediate
            : value.Trim();
        if (normalized.Equals(NotificationDeliveryModes.Immediate, StringComparison.OrdinalIgnoreCase))
            return NotificationDeliveryModes.Immediate;
        if (normalized.Equals(NotificationDeliveryModes.DailyDigest, StringComparison.OrdinalIgnoreCase))
            return NotificationDeliveryModes.DailyDigest;
        throw new ValidationException("Notification delivery mode must be Immediate or DailyDigest.");
    }

    private static string NormalizeTimeZone(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "UTC" : value.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return normalized;
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ValidationException("Notification time zone is invalid.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ValidationException("Notification time zone is invalid.");
        }
    }

    private static DateTimeOffset NextDigestAt(DateTimeOffset now, string timeZoneId, int hour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localTarget = new DateTime(
            localNow.Year, localNow.Month, localNow.Day, hour, 0, 0,
            DateTimeKind.Unspecified);
        if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(1);
        if (zone.IsInvalidTime(localTarget)) localTarget = localTarget.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTarget, zone), TimeSpan.Zero);
    }

    private static string RequireOrganizationId(string organizationId)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            throw new ValidationException("Notification organization id is required.");
        return organizationId.Trim();
    }

    private static NotificationResponse ToResponse(NotificationDocument notification) =>
        new(
            notification.Id,
            notification.UserId,
            notification.Type,
            notification.Message,
            notification.Read,
            notification.EmailStatus,
            notification.CreatedAt);

    private static NotificationPreferenceResponse ToResponse(NotificationPreferenceDocument preference) =>
        new(
            preference.InAppEnabled,
            preference.EmailEnabled,
            preference.MutedTypes,
            preference.TypeSettings.Select(setting => new NotificationTypePreferenceResponse(
                setting.Type,
                setting.InAppEnabled,
                setting.EmailEnabled)).ToList(),
            preference.Version,
            preference.DeliveryMode,
            preference.TimeZoneId,
            preference.DigestHourLocal);
}
