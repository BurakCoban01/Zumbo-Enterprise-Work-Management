using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;

public sealed class NotificationUserDirectoryAdapter(IUserRepository users) : INotificationUserDirectory
{
    public async Task<NotificationUser?> FindAsync(string userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        return user is null ? null : new NotificationUser(user.Id, user.OrganizationId, user.Email, user.IsActive);
    }
}
