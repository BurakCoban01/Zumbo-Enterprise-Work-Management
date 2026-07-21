public sealed class MongoIndexInitializer(
    MongoMigrationRunner migrations,
    ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var report = await migrations.RunAsync(cancellationToken);
        logger.LogInformation(
            "MongoDB migrations checked: {Applied} applied, {Skipped} skipped, {Paused} paused, dry-run={DryRun}",
            report.Applied,
            report.Skipped,
            report.Paused,
            report.DryRun);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
