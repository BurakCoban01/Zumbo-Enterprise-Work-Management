using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private static NotificationResponse ToResponse(NotificationDocument notification) =>
        new(
            notification.Id,
            notification.UserId,
            notification.Type,
            notification.Message,
            notification.Read,
            notification.EmailStatus,
            notification.CreatedAt);

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
