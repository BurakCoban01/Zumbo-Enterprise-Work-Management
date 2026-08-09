using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal static class NotificationDeliveryPolicy
{
    internal static string RequireOrganizationId(string organizationId)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            throw new ValidationException("Notification organization id is required.");
        return organizationId.Trim();
    }

    internal static void ClearLease(NotificationDocument notification)
    {
        notification.EmailLeaseToken = null;
        notification.EmailClaimedBy = null;
        notification.EmailLeaseUntil = null;
    }
}
