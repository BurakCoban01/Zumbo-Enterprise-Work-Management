using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class UpdateNotificationPreferencesHandler(NotificationService service)
{
    private UpdateNotificationPreferencesSlice? slice;

    public UpdateNotificationPreferencesHandler(
        IDocumentRepository<NotificationPreferenceDocument> preferences,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new UpdateNotificationPreferencesSlice(
            preferences,
            new NotificationPreferenceAccess(
                distributedLockProvider,
                lockOptions.Value,
                currentUser),
            clock);

    public Task<NotificationPreferenceResponse> HandleAsync(
        UpdateNotificationPreferencesCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.UpdatePreferencesAsync(command.Request, ct);
}
