using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class DurableEventProcessorOptions
{
    public int BatchSize { get; init; } = 50;
    public int MaximumAttempts { get; init; } = 8;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
    public double RetryJitterRatio { get; init; } = 0.2;
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public void Validate()
    {
        if (BatchSize is < 1 or > 500) throw new InvalidOperationException("Durable event batch size must be between 1 and 500.");
        if (MaximumAttempts is < 1 or > 100) throw new InvalidOperationException("Durable event maximum attempts must be between 1 and 100.");
        if (LeaseDuration <= TimeSpan.Zero || LeaseDuration > TimeSpan.FromMinutes(15)) throw new InvalidOperationException("Durable event lease duration is invalid.");
        if (BaseRetryDelay <= TimeSpan.Zero || MaximumRetryDelay < BaseRetryDelay) throw new InvalidOperationException("Durable event retry delay is invalid.");
        if (RetryJitterRatio is < 0 or > 1) throw new InvalidOperationException("Durable event retry jitter ratio is invalid.");
        if (IdleDelay < TimeSpan.Zero || IdleDelay > TimeSpan.FromMinutes(1)) throw new InvalidOperationException("Durable event idle delay is invalid.");
    }
}
