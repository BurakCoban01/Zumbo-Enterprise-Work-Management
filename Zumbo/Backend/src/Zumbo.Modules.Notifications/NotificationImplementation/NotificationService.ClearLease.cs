using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private static void ClearLease(NotificationDocument notification)
    {
        notification.EmailLeaseToken = null;
        notification.EmailClaimedBy = null;
        notification.EmailLeaseUntil = null;
    }
}
