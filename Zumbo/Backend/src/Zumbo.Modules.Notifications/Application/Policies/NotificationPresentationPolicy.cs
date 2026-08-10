using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications.Application.Policies;

internal static class NotificationPresentationPolicy
{
    internal static NotificationPresentation For(
        string type,
        string? sourceKindValue,
        string? sourceIdValue,
        string? deduplicationKey)
    {
        var normalizedType = type.Trim();
        var sourceKind = NormalizeSourceKind(sourceKindValue);
        var sourceId = NormalizeSourceId(sourceIdValue)
            ?? SourceIdFromDeduplicationKey(deduplicationKey);

        return normalizedType switch
        {
            "Mention" or "Assignment" or "ApprovalRequest" =>
                new(NotificationCategories.Action, NotificationActionKinds.OpenWorkItem,
                    sourceKind ?? "WorkItem", sourceId),
            "TeamInvitation" =>
                new(NotificationCategories.Action, NotificationActionKinds.OpenTeam,
                    sourceKind ?? "Team", sourceId),
            "Approval" or "DueDateReminder" =>
                new(NotificationCategories.Awareness, NotificationActionKinds.OpenWorkItem,
                    sourceKind ?? "WorkItem", sourceId),
            _ => new(NotificationCategories.Awareness, NotificationActionKinds.None, sourceKind, sourceId)
        };
    }

    internal static string? NormalizeSourceKind(string? value) => Normalize(value, 40);

    internal static string? NormalizeSourceId(string? value) => Normalize(value, 200);

    private static string? Normalize(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maximumLength)
        {
            throw new ValidationException($"Notification source value cannot exceed {maximumLength} characters.");
        }
        return normalized;
    }

    private static string? SourceIdFromDeduplicationKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && (parts[0] == "mention" || parts[0] == "due" || parts[0] == "watcher")
            ? parts[1]
            : null;
    }
}
