using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

public sealed class RandomExternalDependencyJitter : IExternalDependencyJitter
{
    public TimeSpan Apply(TimeSpan delay, double ratio)
    {
        var boundedRatio = Math.Clamp(ratio, 0, 1);
        var factor = 1 - boundedRatio + Random.Shared.NextDouble() * boundedRatio * 2;
        return TimeSpan.FromMilliseconds(Math.Max(1, delay.TotalMilliseconds * factor));
    }
}
