using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Storage;

public sealed class StorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.CheckHealthAsync(cancellationToken);
            return HealthCheckResult.Healthy("Attachment storage is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Attachment storage is not reachable.", ex);
        }
    }
}

public sealed class MongoHealthCheck(IMongoDbService mongo) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await mongo.CheckHealthAsync(cancellationToken);
            return HealthCheckResult.Healthy("MongoDB is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB is not reachable.", ex);
        }
    }
}

public sealed class RedisHealthCheck(
    IConnectionMultiplexer redis,
    IExternalDependencyPolicyProvider policies) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await policies.Get(ExternalDependencyNames.Redis).ExecuteAsync(
                "health",
                ExternalDependencyOperationKind.Health,
                _ => redis.GetDatabase().PingAsync(),
                exception => exception is RedisException,
                cancellationToken);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", ex);
        }
    }
}

public sealed class PostgreSqlHealthCheck(
    NpgsqlDataSource dataSource,
    IExternalDependencyPolicyProvider policies) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await policies.Get(ExternalDependencyNames.PostgreSql).ExecuteAsync(
                "health",
                ExternalDependencyOperationKind.Health,
                async token =>
                {
                    await using var command = dataSource.CreateCommand("SELECT 1");
                    return await command.ExecuteScalarAsync(token);
                },
                exception => exception is NpgsqlException npgsql && npgsql.IsTransient,
                cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }
}

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
