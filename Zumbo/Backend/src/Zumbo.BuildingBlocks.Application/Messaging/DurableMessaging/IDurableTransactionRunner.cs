namespace Zumbo.BuildingBlocks.Application.Messaging;

public interface IDurableTransactionRunner
{
    Task ExecuteAsync(
        string ownerModule,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteAsync<TResult>(
        string ownerModule,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
