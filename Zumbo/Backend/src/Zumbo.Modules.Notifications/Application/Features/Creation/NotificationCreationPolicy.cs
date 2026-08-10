using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal static class NotificationCreationPolicy
{
    internal static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ValidationException("Notification type is required.");
        var normalized = type.Trim();
        if (normalized.Length > 50)
            throw new ValidationException("Notification type cannot exceed 50 characters.");
        return normalized;
    }

    internal static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ValidationException("Notification message is required.");
        var normalized = message.Trim();
        if (normalized.Length > 2000)
            throw new ValidationException("Notification message cannot exceed 2000 characters.");
        return normalized;
    }

    internal static string? NormalizeDeduplicationKey(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > 200)
            throw new ValidationException(
                "Notification deduplication key cannot exceed 200 characters.");
        return normalized;
    }
}
