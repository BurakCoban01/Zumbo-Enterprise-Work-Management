using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

public sealed class ExternalDependencyPolicyProvider(
    IConfiguration configuration,
    ExternalDependencyTelemetry telemetry,
    IExternalDependencyJitter jitter,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider) : IExternalDependencyPolicyProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, ExternalDependencyPolicy> policies = new(StringComparer.Ordinal);

    public IExternalDependencyPolicy Get(string dependency)
    {
        if (!ExternalDependencyNames.All.Contains(dependency, StringComparer.Ordinal))
            throw new InvalidOperationException($"Unknown external dependency policy '{dependency}'.");
        return policies.GetOrAdd(dependency, Create);
    }

    public IReadOnlyList<ExternalDependencySnapshot> GetSnapshots() => telemetry.GetSnapshots();

    private ExternalDependencyPolicy Create(string dependency)
    {
        var options = configuration.GetSection($"ExternalDependencies:{dependency}")
            .Get<ExternalDependencyPolicyOptions>() ?? new ExternalDependencyPolicyOptions();
        return new ExternalDependencyPolicy(
            dependency,
            options,
            telemetry,
            jitter,
            loggerFactory.CreateLogger($"Zumbo.ExternalDependencies.{dependency}"),
            timeProvider);
    }

    public void Dispose()
    {
        foreach (var policy in policies.Values) policy.Dispose();
    }
}
