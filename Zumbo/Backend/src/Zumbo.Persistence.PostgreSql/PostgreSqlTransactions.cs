using System.Data;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.Persistence.PostgreSql;

public interface IPostgreSqlTransactionRunner
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}

public sealed class PostgreSqlSession : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly IExternalDependencyPolicy? resiliencePolicy;
    private NpgsqlConnection? transactionConnection;
    private NpgsqlTransaction? transaction;

    public PostgreSqlSession(NpgsqlDataSource dataSource)
        : this(dataSource, null)
    {
    }

    public PostgreSqlSession(
        NpgsqlDataSource dataSource,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        this.dataSource = dataSource;
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
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (NpgsqlException)
            {
                // Disposal must still release a broken connection.
            }
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

public sealed class PostgreSqlTransactionRunner(PostgreSqlSession session) :
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
            await session.RollbackAsync(CancellationToken.None);
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

internal sealed class PostgreSqlConnectionLease(
    NpgsqlConnection connection,
    NpgsqlTransaction? transaction,
    bool ownsConnection) : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; } = connection;
    public NpgsqlTransaction? Transaction { get; } = transaction;

    public NpgsqlCommand CreateCommand(string sql, int commandTimeoutSeconds)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Transaction = Transaction;
        return command;
    }

    public async ValueTask DisposeAsync()
    {
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
        }
    }
}
