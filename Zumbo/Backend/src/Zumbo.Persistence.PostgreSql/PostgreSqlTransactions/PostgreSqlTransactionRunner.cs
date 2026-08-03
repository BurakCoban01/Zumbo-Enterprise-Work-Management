using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlTransactionRunner(
    PostgreSqlSession session,
    ILogger<PostgreSqlTransactionRunner>? logger = null) :
    IPostgreSqlTransactionRunner,
    IDurableTransactionRunner
{
    public Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            async ct =>
            {
                await operation(ct);
                return true;
            },
            isolationLevel,
            cancellationToken);

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        await session.BeginAsync(isolationLevel, cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await PostgreSqlCompensation.RunAsync(
                "postgres.transaction.rollback",
                token => session.RollbackAsync(token),
                logger);
            throw;
        }
    }

    Task IDurableTransactionRunner.ExecuteAsync(
        string ownerModule,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, cancellationToken: cancellationToken);

    Task<TResult> IDurableTransactionRunner.ExecuteAsync<TResult>(
        string ownerModule,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, cancellationToken: cancellationToken);
}
