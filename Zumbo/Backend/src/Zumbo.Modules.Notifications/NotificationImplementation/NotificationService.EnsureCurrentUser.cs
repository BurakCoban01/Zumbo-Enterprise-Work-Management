using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private void EnsureCurrentUser(string userId)
    {
        if (!string.Equals(RequireCurrentUser(), userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Users can only access their own notifications.");
        }
    }
}
