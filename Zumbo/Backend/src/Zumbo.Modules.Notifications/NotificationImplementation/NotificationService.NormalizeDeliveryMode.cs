using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private static string NormalizeDeliveryMode(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? NotificationDeliveryModes.Immediate
            : value.Trim();
        if (normalized.Equals(NotificationDeliveryModes.Immediate, StringComparison.OrdinalIgnoreCase))
            return NotificationDeliveryModes.Immediate;
        if (normalized.Equals(NotificationDeliveryModes.DailyDigest, StringComparison.OrdinalIgnoreCase))
            return NotificationDeliveryModes.DailyDigest;
        throw new ValidationException("Notification delivery mode must be Immediate or DailyDigest.");
    }
}
