using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService
{
    public async Task<IReadOnlyList<NotificationResponse>> ListAsync(
        string userId,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50,
        bool unreadOnly = false)
        => await listNotificationsHandler.HandleAsync(
            new ListNotificationsQuery(userId, page, pageSize, unreadOnly),
            ct);

    public async Task MarkAsReadAsync(string notificationId, CancellationToken ct)
    {
        await markNotificationAsReadHandler.HandleAsync(
            new MarkNotificationAsReadCommand(notificationId),
            ct);
    }

    private static NotificationResponse ToResponse(NotificationDocument notification) =>
        NotificationResponseMapper.ToResponse(notification);

    private static NotificationPreferenceResponse ToResponse(NotificationPreferenceDocument preference) =>
        new(
            preference.InAppEnabled,
            preference.EmailEnabled,
            preference.MutedTypes,
            preference.TypeSettings.Select(setting => new NotificationTypePreferenceResponse(
                setting.Type,
                setting.InAppEnabled,
                setting.EmailEnabled)).ToList(),
            preference.Version,
            preference.DeliveryMode,
            preference.TimeZoneId,
            preference.DigestHourLocal);
}
