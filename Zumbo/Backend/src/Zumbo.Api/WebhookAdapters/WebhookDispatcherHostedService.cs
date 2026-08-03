using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Organizations;
using Zumbo.SharedKernel;

public sealed class WebhookDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDispatcherHostedService> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Clamp(options.Value.DispatcherIntervalSeconds, 1, 3600)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<WorkItemWebhookService>();
                await service.DispatchAsync(
                    Math.Clamp(options.Value.DispatchBatchSize, 1, 100),
                    workerId,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Webhook dispatcher iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
