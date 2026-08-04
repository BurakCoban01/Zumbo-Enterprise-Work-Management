using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Storage;

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
