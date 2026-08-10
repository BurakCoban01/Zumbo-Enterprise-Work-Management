using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class GetNotificationPreferencesHandler(NotificationService service)
{
    private GetNotificationPreferencesSlice? slice;

    public GetNotificationPreferencesHandler(
        IDocumentRepository<NotificationPreferenceDocument> preferences,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> lockOptions,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new GetNotificationPreferencesSlice(
            preferences,
            new NotificationPreferenceAccess(
                distributedLockProvider,
                lockOptions.Value,
                currentUser));

    public Task<NotificationPreferenceResponse> HandleAsync(
        GetNotificationPreferencesQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct) ?? service.GetPreferencesAsync(ct);
}
