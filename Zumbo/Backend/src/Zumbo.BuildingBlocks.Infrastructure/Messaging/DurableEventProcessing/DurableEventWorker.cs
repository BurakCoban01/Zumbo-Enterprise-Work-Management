using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class DurableEventWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<DurableEventProcessorOptions> configuredOptions,
    ILogger<DurableEventWorker> logger) : BackgroundService
{
    private readonly DurableEventProcessorOptions _options = Validate(configuredOptions.Value);
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Durable event worker {WorkerId} started", _workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<DurableEventProcessor>();
                var claimed = await processor.ProcessBatchAsync(_workerId, stoppingToken);
                if (claimed == 0 && _options.IdleDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Durable event worker cycle failed");
                await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
            }
        }
    }

    private static DurableEventProcessorOptions Validate(DurableEventProcessorOptions options)
    {
        options.Validate();
        return options;
    }
}
