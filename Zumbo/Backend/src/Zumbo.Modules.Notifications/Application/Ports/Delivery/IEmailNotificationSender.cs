using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public interface IEmailNotificationSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct);
}
