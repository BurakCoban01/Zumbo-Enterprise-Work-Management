using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Storage;

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
