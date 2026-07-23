using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record UpdateNotificationPreferencesRequest(
    bool InAppEnabled,
    bool EmailEnabled,
    IReadOnlyCollection<string>? MutedTypes,
    IReadOnlyCollection<NotificationTypePreferenceRequest>? TypeSettings = null,
    string DeliveryMode = NotificationDeliveryModes.Immediate,
    string TimeZoneId = "UTC",
    int DigestHourLocal = 8);

public sealed record NotificationTypePreferenceRequest(
    string Type,
    bool InAppEnabled,
    bool EmailEnabled);

public sealed record NotificationTypePreferenceResponse(
    string Type,
    bool InAppEnabled,
    bool EmailEnabled);

public sealed record NotificationPreferenceResponse(
    bool InAppEnabled,
    bool EmailEnabled,
    IReadOnlyCollection<string> MutedTypes,
    IReadOnlyCollection<NotificationTypePreferenceResponse>? TypeSettings = null,
    long Version = 0,
    string DeliveryMode = NotificationDeliveryModes.Immediate,
    string TimeZoneId = "UTC",
    int DigestHourLocal = 8);

public sealed record NotificationUser(string Id, string OrganizationId, string Email, bool IsActive);

public static class NotificationDeliveryModes
{
    public const string Immediate = "Immediate";
    public const string DailyDigest = "DailyDigest";
}

public static class NotificationEmailStatuses
{
    public const string Disabled = "Disabled";
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Sent = "Sent";
    public const string DeadLetter = "DeadLetter";
}

public sealed class EmailNotificationOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 25;
    public bool UseSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string FromAddress { get; init; } = "noreply@zumbo.local";
    public string FromName { get; init; } = "Zumbo";
    public int MaxAttempts { get; init; } = 5;
    public int BaseRetrySeconds { get; init; } = 60;
    public int MaximumRetrySeconds { get; init; } = 3600;
    public double RetryJitterRatio { get; init; } = 0.2;
    public int LeaseSeconds { get; init; } = 60;
    public int DispatchBatchSize { get; init; } = 50;
    public int DispatcherIntervalSeconds { get; init; } = 30;
}

public sealed record NotificationDeliveryMetrics(
    string OrganizationId,
    long Pending,
    long Processing,
    long Sent,
    long DeadLetter,
    long Disabled,
    DateTimeOffset? OldestPendingAt,
    DateTimeOffset CapturedAt);

public sealed record NotificationDeadLetterSummary(
    string Id,
    string Type,
    int Attempts,
    DateTimeOffset DeadLetteredAt);

public interface INotificationUserDirectory
{
    Task<NotificationUser?> FindAsync(string userId, CancellationToken ct);
}

public interface IEmailNotificationSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct);
}

public interface INotificationAuditWriter
{
    Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public sealed class NotificationDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Read { get; set; }
    public string? EmailAddress { get; set; }
    public string EmailStatus { get; set; } = NotificationEmailStatuses.Disabled;
    public string EmailDeliveryMode { get; set; } = NotificationDeliveryModes.Immediate;
    public int EmailAttempts { get; set; }
    public DateTimeOffset? EmailNextAttemptAt { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public string? EmailLastError { get; set; }
    public string? EmailLeaseToken { get; set; }
    public string? EmailClaimedBy { get; set; }
    public DateTimeOffset? EmailLeaseUntil { get; set; }
    public DateTimeOffset? EmailDeadLetteredAt { get; set; }
    public string? DeduplicationKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotificationPreferenceDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public List<string> MutedTypes { get; set; } = [];
    public List<NotificationTypePreferenceDocument> TypeSettings { get; set; } = [];
    public string DeliveryMode { get; set; } = NotificationDeliveryModes.Immediate;
    public string TimeZoneId { get; set; } = "UTC";
    public int DigestHourLocal { get; set; } = 8;
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class NotificationTypePreferenceDocument
{
    public string Type { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
}
