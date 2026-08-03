using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task<NotificationPreferenceResponse> GetPreferencesAsync(CancellationToken ct)
    {
        var userId = RequireCurrentUser();
        var preference = await preferences.SelectAsync(x => x.UserId == userId, ct);
        return preference is null
            ? new NotificationPreferenceResponse(true, true, [], [], 0)
            : ToResponse(preference);
    }
}
