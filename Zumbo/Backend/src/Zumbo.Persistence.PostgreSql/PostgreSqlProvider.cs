using System.Data.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlProvider : IAsyncDisposable
{
    private readonly PostgreSqlPersistenceOptions options;
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgreSqlSession session;
    private readonly PostgreSqlMigrationRunner migrations;
    private readonly ILogger<PostgreSqlProvider>? logger;

    public PostgreSqlProvider(
        string connectionString,
        ILogger<PostgreSqlProvider>? logger = null)
    {
        this.logger = logger;
        options = new PostgreSqlPersistenceOptions { ConnectionString = connectionString };
        options.Validate();
        dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        session = new PostgreSqlSession(dataSource);
        migrations = new PostgreSqlMigrationRunner(dataSource, options);
    }

    public IDocumentRepository<TDocument> CreateRepository<TDocument>(string schema, string table)
        where TDocument : class, IDocument
    {
        options.MapDocument<TDocument>(schema, table);
        return new PostgreSqlDocumentRepository<TDocument>(session, options);
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        await dataSource.OpenConnectionAsync(cancellationToken);

    public async Task MigrateAsync(CancellationToken cancellationToken) =>
        _ = await migrations.ApplyAsync(cancellationToken);

    public async Task RollbackAsync(string migrationId, CancellationToken cancellationToken)
    {
        var status = await migrations.StatusAsync(cancellationToken);
        var migration = status.Applied.SingleOrDefault(x => x.Name == migrationId)
            ?? throw new InvalidOperationException($"Applied migration '{migrationId}' was not found.");
        await migrations.RollbackAsync(migration.Version - 1, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken) =>
        (await migrations.StatusAsync(cancellationToken)).Applied.Select(x => x.Name).ToList();

    public async Task ExecuteInTransactionAsync(
        Func<DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            await operation(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await PostgreSqlCompensation.RunAsync(
                "postgres.provider.rollback",
                token => transaction.RollbackAsync(token),
                logger);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync();
        await dataSource.DisposeAsync();
    }
}
