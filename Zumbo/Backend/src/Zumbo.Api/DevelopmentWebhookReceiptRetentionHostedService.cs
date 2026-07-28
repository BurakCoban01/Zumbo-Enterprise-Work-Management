public sealed class DevelopmentWebhookReceiptRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DevelopmentWebhookReceiptRetentionHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var retention = scope.ServiceProvider.GetRequiredService<
                    Zumbo.Modules.WorkItems.DevelopmentWebhookReceiptRetentionService>();
                var deleted = await retention.PurgeExpiredAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Purged {Count} expired development webhook receipts.",
                        deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Development webhook receipt retention failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
