using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoTransactionContext : IAsyncDisposable
{
    public IClientSessionHandle? Session { get; private set; }
    public IMongoClient? Client { get; private set; }
    public bool HasActiveTransaction => Session?.IsInTransaction == true;

    internal void Attach(IMongoClient client, IClientSessionHandle session)
    {
        if (Session is not null)
        {
            throw new InvalidOperationException("A MongoDB transaction is already active in this scope.");
        }

        Client = client;
        Session = session;
    }

    internal void EnsureCompatible(IMongoClient client)
    {
        if (Session is not null && !ReferenceEquals(Client, client))
        {
            throw new InvalidOperationException(
                "The active MongoDB transaction cannot span differently configured MongoDB clients.");
        }
    }

    internal ValueTask DetachAndDisposeAsync()
    {
        var session = Session;
        Session = null;
        Client = null;
        session?.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => DetachAndDisposeAsync();
}

public sealed class MongoDurableTransactionRunner(
    IMongoDbService mongo,
    MongoTransactionContext context,
    ILogger<MongoDurableTransactionRunner>? logger = null) : IDurableTransactionRunner
{
    private const int MaximumAttempts = 3;
    private const string TransientTransactionError = "TransientTransactionError";
    private const string UnknownTransactionCommitResult = "UnknownTransactionCommitResult";

    public Task ExecuteAsync(
        string ownerModule,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<object?>(
            ownerModule,
            async token =>
            {
                await operation(token);
                return null;
            },
            cancellationToken);

    public async Task<TResult> ExecuteAsync<TResult>(
        string ownerModule,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (context.HasActiveTransaction)
        {
            context.EnsureCompatible(mongo.GetClient(ownerModule));
            return await operation(cancellationToken);
        }

        var client = mongo.GetClient(ownerModule);
        MongoException? lastTransientError = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var session = await client.StartSessionAsync(cancellationToken: cancellationToken);
            context.Attach(client, session);
            var retryTransaction = false;
            try
            {
                session.StartTransaction(new TransactionOptions(
                    readConcern: ReadConcern.Snapshot,
                    writeConcern: WriteConcern.WMajority,
                    readPreference: ReadPreference.Primary));
                var result = await operation(cancellationToken);
                await CommitWithRetryAsync(session, cancellationToken);
                return result;
            }
            catch (MongoException exception)
                when (attempt < MaximumAttempts && exception.HasErrorLabel(TransientTransactionError))
            {
                lastTransientError = exception;
                retryTransaction = true;
                await AbortIfActiveAsync(session);
            }
            catch
            {
                await AbortIfActiveAsync(session);
                throw;
            }
            finally
            {
                await context.DetachAndDisposeAsync();
            }

            if (retryTransaction)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }

        throw lastTransientError is not null
            ? lastTransientError
            : new InvalidOperationException("MongoDB transaction retry budget was exhausted.");
    }

    private static async Task CommitWithRetryAsync(
        IClientSessionHandle session,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await session.CommitTransactionAsync(cancellationToken);
                return;
            }
            catch (MongoException exception)
                when (attempt < MaximumAttempts
                    && exception.HasErrorLabel(UnknownTransactionCommitResult))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private async Task AbortIfActiveAsync(IClientSessionHandle session)
    {
        if (!session.IsInTransaction)
        {
            return;
        }

        var result = await CompensationExecution.RunAsync(
            "mongo.transaction.abort",
            token => session.AbortTransactionAsync(token));
        if (!result.Succeeded)
        {
            logger?.LogWarning(
                "Compensation operation {Operation} ended with {Outcome}; failure type {FailureType}.",
                result.Operation,
                result.Outcome,
                result.Exception?.GetType().Name ?? "none");
        }
    }
}
