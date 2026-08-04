using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public static class NotificationEmailStatuses
{
    public const string Disabled = "Disabled";
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Sent = "Sent";
    public const string DeadLetter = "DeadLetter";
}
