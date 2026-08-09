using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class WorkItemRecurrenceSchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkItemRecurrenceOptions> options,
    ILogger<WorkItemRecurrenceSchedulerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.IntervalSeconds, 5, 3600));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ScheduleDueRecurrencesHandler>();
                var transactions = scope.ServiceProvider.GetRequiredService<IDurableTransactionRunner>();
                await transactions.ExecuteAsync(
                    "WorkItems",
                    ct => handler.HandleAsync(new ScheduleDueRecurrencesCommand(), ct),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Work-item recurrence scheduler iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
