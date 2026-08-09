using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal static class NotificationPreferenceValidation
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

    internal static string NormalizeDeliveryMode(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? NotificationDeliveryModes.Immediate
            : value.Trim();
        if (normalized.Equals(
                NotificationDeliveryModes.Immediate,
                StringComparison.OrdinalIgnoreCase))
        {
            return NotificationDeliveryModes.Immediate;
        }
        if (normalized.Equals(
                NotificationDeliveryModes.DailyDigest,
                StringComparison.OrdinalIgnoreCase))
        {
            return NotificationDeliveryModes.DailyDigest;
        }
        throw new ValidationException(
            "Notification delivery mode must be Immediate or DailyDigest.");
    }

    internal static string NormalizeTimeZone(string value)
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
}
