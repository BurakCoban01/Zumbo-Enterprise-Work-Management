using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal sealed class NotificationPreferenceAccess(
    IDistributedLockProvider distributedLockProvider,
    DistributedLockOptions lockOptions,
    ICurrentUser currentUser)
{
    internal string RequireCurrentUser() =>
        currentUser.UserId
        ?? throw new UnauthorizedException("Authenticated user is required.");

    internal async Task<IAsyncDisposable> AcquireLockAsync(
        string resource,
        CancellationToken ct) =>
        await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(lockOptions.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(lockOptions.WaitSeconds, 0, 30)),
            ct)
        ?? throw new ConflictException(
            "NOTIFICATION_RESOURCE_BUSY",
            "Notification resource is busy; retry the operation.");
}
