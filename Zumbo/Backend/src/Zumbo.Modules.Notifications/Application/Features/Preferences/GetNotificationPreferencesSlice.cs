using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

internal sealed class GetNotificationPreferencesSlice(
    IDocumentRepository<NotificationPreferenceDocument> preferences,
    NotificationPreferenceAccess access)
{
    internal async Task<NotificationPreferenceResponse> HandleAsync(
        GetNotificationPreferencesQuery query,
        CancellationToken ct)
    {
        var userId = access.RequireCurrentUser();
        var preference = await preferences.SelectAsync(x => x.UserId == userId, ct);
        return preference is null
            ? new NotificationPreferenceResponse(true, true, [], [], 0)
            : NotificationPreferenceMapper.ToResponse(preference);
    }
}
