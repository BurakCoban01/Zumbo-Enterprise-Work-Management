using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class MarkNotificationAsReadHandler(NotificationService service)
{
    public async Task<MarkNotificationAsReadResponse> HandleAsync(
        MarkNotificationAsReadCommand command,
        CancellationToken ct)
    {
        MarkNotificationAsReadValidator.Validate(command);
        await service.MarkAsReadAsync(command.NotificationId, ct);
        return new MarkNotificationAsReadResponse(true);
    }
}
