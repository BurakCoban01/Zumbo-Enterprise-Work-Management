using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Storage;

public sealed class DurableMessagingHealthCheck(
    IDurableEventOutbox outbox,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await outbox.GetMetricsAsync(timeProvider.GetUtcNow(), cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["pending"] = metrics.Pending,
                ["processing"] = metrics.Processing,
                ["deadLetter"] = metrics.DeadLetter,
                ["retried"] = metrics.Retried,
                ["oldestPendingAgeSeconds"] = metrics.OldestPendingAtUtc is null
                    ? 0
                    : Math.Max(0, (metrics.CapturedAtUtc - metrics.OldestPendingAtUtc.Value).TotalSeconds)
            };
            return HealthCheckResult.Healthy("Durable messaging store is reachable.", data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Durable messaging store is not reachable.",
                exception);
        }
    }
}
