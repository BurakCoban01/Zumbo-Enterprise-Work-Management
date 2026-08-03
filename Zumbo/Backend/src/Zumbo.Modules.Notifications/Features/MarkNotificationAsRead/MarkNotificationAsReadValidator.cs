using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class MarkNotificationAsReadValidator
{
    public static void Validate(MarkNotificationAsReadCommand command) => ArgumentNullException.ThrowIfNull(command);
}
