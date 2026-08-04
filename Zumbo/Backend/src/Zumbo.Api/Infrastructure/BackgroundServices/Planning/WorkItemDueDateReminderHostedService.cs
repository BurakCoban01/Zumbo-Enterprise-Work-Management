using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.WorkItems;

public sealed class DueDateReminderHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DueDateReminderOptions> options,
    ILogger<DueDateReminderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(options.Value.IntervalMinutes, 1, 1440));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<WorkItemService>();
                var transactions = scope.ServiceProvider.GetRequiredService<IDurableTransactionRunner>();
                await transactions.ExecuteAsync(
                    "WorkItems",
                    token => service.SendDueDateRemindersAsync(options.Value.HorizonHours, token),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Due-date reminder dispatcher iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
