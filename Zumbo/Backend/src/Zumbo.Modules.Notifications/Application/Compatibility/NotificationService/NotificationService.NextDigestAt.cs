using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private static DateTimeOffset NextDigestAt(DateTimeOffset now, string timeZoneId, int hour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localTarget = new DateTime(
            localNow.Year, localNow.Month, localNow.Day, hour, 0, 0,
            DateTimeKind.Unspecified);
        if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(1);
        if (zone.IsInvalidTime(localTarget)) localTarget = localTarget.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTarget, zone), TimeSpan.Zero);
    }
}
