using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private TimeSpan RetryDelay(int attempt, int baseSeconds, int maximumSeconds, double jitterRatio)
    {
        var boundedBase = TimeSpan.FromSeconds(Math.Clamp(baseSeconds, 1, 3600));
        var boundedMaximum = TimeSpan.FromSeconds(Math.Clamp(maximumSeconds, 1, 86_400));
        if (retryJitter is not null)
        {
            return new DurableMessageRetryPolicy(
                boundedBase,
                boundedMaximum,
                Math.Clamp(jitterRatio, 0, 1),
                retryJitter).DelayForAttempt(attempt);
        }

        var exponent = Math.Min(attempt - 1, 20);
        return TimeSpan.FromSeconds(Math.Min(
            boundedMaximum.TotalSeconds,
            boundedBase.TotalSeconds * Math.Pow(2, exponent)));
    }
}
