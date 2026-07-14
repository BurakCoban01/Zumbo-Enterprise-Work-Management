using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationResponse(
    string Id,
    string UserId,
    string Type,
    string Message,
    bool Read,
    string EmailStatus,
    DateTimeOffset CreatedAt);
public sealed record UpdateNotificationPreferencesRequest(
    bool InAppEnabled,
    bool EmailEnabled,
    IReadOnlyCollection<string>? MutedTypes);
public sealed record NotificationPreferenceResponse(
    bool InAppEnabled,
    bool EmailEnabled,
    IReadOnlyCollection<string> MutedTypes);
public sealed record NotificationUser(string Id, string Email, bool IsActive);

public sealed class EmailNotificationOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 25;
    public bool UseSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string FromAddress { get; init; } = "noreply@zumbo.local";
    public string FromName { get; init; } = "Zumbo";
}

public interface INotificationUserDirectory
{
    Task<NotificationUser?> FindAsync(string userId, CancellationToken ct);
}

public interface IEmailNotificationSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct);
}

public sealed class NotificationDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Read { get; set; }
    public string? EmailAddress { get; set; }
    public string EmailStatus { get; set; } = "Disabled";
    public int EmailAttempts { get; set; }
    public DateTimeOffset? EmailNextAttemptAt { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public string? EmailLastError { get; set; }
    public string? DeduplicationKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotificationPreferenceDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public List<string> MutedTypes { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class NotificationService(
    IDocumentRepository<NotificationDocument> notifications,
    IDocumentRepository<NotificationPreferenceDocument> preferences,
    INotificationUserDirectory userDirectory,
    IEmailNotificationSender emailSender,
    IOptions<EmailNotificationOptions> emailOptions,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser)
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
        var preference = await preferences.SelectAsync(x => x.UserId == userId, ct);
        if (preference?.MutedTypes.Contains(normalizedType, StringComparer.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var user = await userDirectory.FindAsync(userId, ct);
        if (user is null || !user.IsActive)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(deduplicationKey))
        {
            await using var dedupeLock = await AcquireLockAsync("notification-dedupe:" + deduplicationKey, ct);
            if (await notifications.SelectAsync(x => x.DeduplicationKey == deduplicationKey, ct) is not null)
            {
                return;
            }

            await CreateNotificationAsync(user, normalizedType, normalizedMessage, preference, deduplicationKey, ct);
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
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new ValidationException("Notification page must be positive and page size must be between 1 and 100.");
        }

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
            ? new NotificationPreferenceResponse(true, true, [])
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

        var preference = await preferences.SelectAsync(x => x.UserId == userId, ct)
            ?? new NotificationPreferenceDocument { UserId = userId };
        preference.InAppEnabled = request.InAppEnabled;
        preference.EmailEnabled = request.EmailEnabled;
        preference.MutedTypes = mutedTypes;
        preference.UpdatedAt = clock.UtcNow;
        if (await preferences.SelectAsync(x => x.Id == preference.Id, ct) is null)
        {
            await preferences.CreateAsync(preference, ct);
        }
        else
        {
            await preferences.ReplaceByFilterAsync(x => x.Id == preference.Id, preference, ct);
        }

        return ToResponse(preference);
    }

    public async Task<int> DispatchPendingEmailsAsync(int batchSize, CancellationToken ct)
    {
        await using var dispatcherLock = await AcquireLockAsync("notification-email-dispatcher", ct);
        var now = clock.UtcNow;
        var pending = await notifications.ListByFilterAsync(
            x => x.EmailStatus == "Pending" && x.EmailNextAttemptAt <= now,
            x => x.CreatedAt,
            pageSize: Math.Clamp(batchSize, 1, 100),
            cancellationToken: ct);
        var sent = 0;
        foreach (var notification in pending)
        {
            try
            {
                await emailSender.SendAsync(
                    notification.EmailAddress!,
                    $"Zumbo: {notification.Type}",
                    notification.Message,
                    ct);
                notification.EmailStatus = "Sent";
                notification.EmailSentAt = clock.UtcNow;
                notification.EmailLastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notification.EmailAttempts++;
                notification.EmailLastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                notification.EmailStatus = notification.EmailAttempts >= 5 ? "Failed" : "Pending";
                notification.EmailNextAttemptAt = clock.UtcNow.AddMinutes(Math.Pow(2, notification.EmailAttempts));
            }

            await notifications.ReplaceByFilterAsync(x => x.Id == notification.Id, notification, ct);
        }

        return sent;
    }

    private async Task CreateNotificationAsync(
        NotificationUser user,
        string type,
        string message,
        NotificationPreferenceDocument? preference,
        string? deduplicationKey,
        CancellationToken ct)
    {
        var inAppEnabled = preference?.InAppEnabled ?? true;
        var emailEnabled = (preference?.EmailEnabled ?? true) && emailOptions.Value.Enabled;
        if (!inAppEnabled && !emailEnabled)
        {
            return;
        }

        await notifications.CreateAsync(new NotificationDocument
        {
            UserId = user.Id,
            Type = type,
            Message = message,
            Read = !inAppEnabled,
            EmailAddress = emailEnabled ? user.Email : null,
            EmailStatus = emailEnabled ? "Pending" : "Disabled",
            EmailNextAttemptAt = emailEnabled ? clock.UtcNow : null,
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
        new(preference.InAppEnabled, preference.EmailEnabled, preference.MutedTypes);
}
