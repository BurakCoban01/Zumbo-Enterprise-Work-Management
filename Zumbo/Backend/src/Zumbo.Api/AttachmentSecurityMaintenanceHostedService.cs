using Microsoft.Extensions.Options;
using Zumbo.Modules.WorkItems;

public sealed class AttachmentSecurityMaintenanceHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AttachmentSecurityOptions> options,
    ILogger<AttachmentSecurityMaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AttachmentSecurityMaintenanceService>();
                var result = await service.RunBatchAsync(stoppingToken);
                if (result.Retried + result.PurgedMetadata + result.DeletedOrphans > 0)
                {
                    logger.LogInformation(
                        "Attachment security maintenance completed: retried={Retried}, clean={Cleaned}, rejected={Rejected}, purged={Purged}, orphans={Orphans}.",
                        result.Retried,
                        result.Cleaned,
                        result.Rejected,
                        result.PurgedMetadata,
                        result.DeletedOrphans);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Attachment security maintenance failed; the next bounded run will retry.");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(Math.Clamp(options.Value.MaintenanceIntervalMinutes, 1, 1_440)),
                stoppingToken);
        }
    }
}
