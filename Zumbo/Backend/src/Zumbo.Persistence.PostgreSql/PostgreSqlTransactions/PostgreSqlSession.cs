using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlSession : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly IExternalDependencyPolicy? resiliencePolicy;
    private readonly ILogger<PostgreSqlSession>? logger;
    private NpgsqlConnection? transactionConnection;
    private NpgsqlTransaction? transaction;

    public PostgreSqlSession(NpgsqlDataSource dataSource)
        : this(dataSource, null, null)
    {
    }

    public PostgreSqlSession(
        NpgsqlDataSource dataSource,
        IExternalDependencyPolicyProvider? policyProvider,
        ILogger<PostgreSqlSession>? logger = null)
    {
        this.dataSource = dataSource;
        this.logger = logger;
        resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.PostgreSql);
    }

    internal bool HasActiveTransaction => transaction is not null;

    internal async ValueTask<PostgreSqlConnectionLease> LeaseAsync(CancellationToken cancellationToken)
    {
        if (transactionConnection is not null)
        {
            return new PostgreSqlConnectionLease(transactionConnection, transaction, ownsConnection: false);
        }

        var connection = await OpenConnectionAsync(cancellationToken);
        return new PostgreSqlConnectionLease(connection, transaction: null, ownsConnection: true);
    }

    internal async Task BeginAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        if (HasActiveTransaction)
        {
            throw new InvalidOperationException("Nested PostgreSQL transactions are not supported.");
        }

        transactionConnection = await OpenConnectionAsync(cancellationToken);
        try
        {
            transaction = await transactionConnection.BeginTransactionAsync(isolationLevel, cancellationToken);
        }
        catch
        {
            await transactionConnection.DisposeAsync();
            transactionConnection = null;
            throw;
        }
    }

    internal async Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureActiveTransaction();
        await transaction!.CommitAsync(cancellationToken);
        await ClearTransactionAsync();
    }

    internal async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await ClearTransactionAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await PostgreSqlCompensation.RunAsync(
                "postgres.session_dispose.rollback",
                token => transaction.RollbackAsync(token),
                logger);
        }

        await ClearTransactionAsync();
    }

    private void EnsureActiveTransaction()
    {
        if (transaction is null || transactionConnection is null)
        {
            throw new InvalidOperationException("No PostgreSQL transaction is active.");
        }
    }

    private Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        resiliencePolicy is null
            ? dataSource.OpenConnectionAsync(cancellationToken).AsTask()
            : resiliencePolicy.ExecuteAsync(
                "connection-open",
                ExternalDependencyOperationKind.Read,
                token => dataSource.OpenConnectionAsync(token).AsTask(),
                exception => exception is NpgsqlException npgsql && npgsql.IsTransient,
                cancellationToken);

    private async ValueTask ClearTransactionAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
        }

        if (transactionConnection is not null)
        {
            await transactionConnection.DisposeAsync();
        }

        transaction = null;
        transactionConnection = null;
    }
}
