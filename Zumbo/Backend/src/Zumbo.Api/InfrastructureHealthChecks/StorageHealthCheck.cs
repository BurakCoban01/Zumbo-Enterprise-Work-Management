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
