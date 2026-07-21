using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlSchemaMigrator : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgreSqlMigrationRunner runner;

    public PostgreSqlSchemaMigrator(string connectionString)
    {
        var options = new PostgreSqlPersistenceOptions
        {
            ConnectionString = connectionString
        };
        options.Validate();
        var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            Pooling = true,
            Timeout = options.ConnectionTimeoutSeconds,
            CommandTimeout = options.CommandTimeoutSeconds,
            MinPoolSize = options.MinimumPoolSize,
            MaxPoolSize = options.MaximumPoolSize
        };
        dataSource = new NpgsqlDataSourceBuilder(connection.ConnectionString).Build();
        runner = new PostgreSqlMigrationRunner(dataSource, options);
    }

    public Task<PostgreSqlMigrationStatus> StatusAsync(CancellationToken cancellationToken = default) =>
        runner.StatusAsync(cancellationToken);

    public Task<IReadOnlyList<PostgreSqlMigrationInfo>> ApplyAsync(CancellationToken cancellationToken = default) =>
        runner.ApplyAsync(cancellationToken);

    public Task<IReadOnlyList<PostgreSqlMigrationInfo>> RollbackAsync(
        long targetVersion,
        CancellationToken cancellationToken = default) =>
        runner.RollbackAsync(targetVersion, cancellationToken);

    public Task<string> GenerateScriptAsync(
        long? fromVersion = null,
        long? toVersion = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default) =>
        runner.GenerateScriptAsync(fromVersion, toVersion, idempotent, cancellationToken);

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
