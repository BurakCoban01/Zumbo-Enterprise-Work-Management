using System.Collections.Concurrent;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class InMemoryDurableTransactionRunner : IDurableTransactionRunner
{
    public async Task ExecuteAsync(
        string ownerModule,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        await operation(cancellationToken);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        string ownerModule,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        return await operation(cancellationToken);
    }
}
