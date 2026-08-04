using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Storage;

public sealed class ExternalDependencyPolicyHealthCheck(
    IExternalDependencyPolicyProvider policies) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = ExternalDependencyNames.All
            .Select(name => policies.GetSnapshots().SingleOrDefault(x => x.Dependency == name)
                ?? new ExternalDependencySnapshot(name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0))
            .ToList();
        var open = snapshots.Where(x => x.CircuitOpen).Select(x => x.Dependency).ToArray();
        var data = snapshots.ToDictionary(
            x => x.Dependency,
            x => (object)new
            {
                x.CircuitOpen,
                x.InFlight,
                x.Queued,
                x.Succeeded,
                x.Failed,
                x.TimedOut,
                x.Rejected,
                x.AverageLatencyMilliseconds
            },
            StringComparer.Ordinal);
        return Task.FromResult(open.Length == 0
            ? HealthCheckResult.Healthy("External dependency policies are accepting traffic.", data)
            : HealthCheckResult.Degraded(
                $"External dependencies are degraded: {string.Join(", ", open)}.",
                data: data));
    }
}
