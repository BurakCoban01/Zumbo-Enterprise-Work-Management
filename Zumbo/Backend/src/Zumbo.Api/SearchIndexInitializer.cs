using Zumbo.BuildingBlocks.Application.Search;

public sealed class SearchIndexInitializer(
    IWorkItemSearchIndex searchIndex,
    ILogger<SearchIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await searchIndex.InitializeAsync(cancellationToken);
        logger.LogInformation("Work item search index initialized.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
