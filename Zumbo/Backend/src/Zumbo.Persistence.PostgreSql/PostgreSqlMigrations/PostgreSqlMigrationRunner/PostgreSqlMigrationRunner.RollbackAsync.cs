using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    public async Task<IReadOnlyList<PostgreSqlMigrationInfo>> RollbackAsync(
        long targetVersion,
        CancellationToken cancellationToken = default)
    {
        if (targetVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetVersion));
        }

        var rolledBack = new List<PostgreSqlMigrationInfo>();
        var migrations = BuildMigrations();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await EnsureLedgerAsync(connection, cancellationToken);

        foreach (var migration in migrations.Where(item => item.Version > targetVersion).OrderByDescending(item => item.Version))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireLockAsync(connection, transaction, cancellationToken);
                var applied = await ReadAppliedAsync(connection, transaction, ledgerMayBeMissing: false, cancellationToken);
                ValidateLedger(migrations, applied);
                if (!applied.Any(row => row.Version == migration.Version))
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                var laterApplied = applied.Any(row => row.Version > migration.Version);
                if (laterApplied)
                {
                    throw new InvalidOperationException(
                        $"Migration {migration.Version} cannot be rolled back before later migrations.");
                }

                await ExecuteAsync(connection, transaction, migration.DownSql, cancellationToken);
                await DeleteLedgerAsync(connection, transaction, migration.Version, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                rolledBack.Add(migration.Info);
            }
            catch
            {
                await PostgreSqlCompensation.RunAsync(
                    "postgres.migration_rollback.rollback",
                    token => transaction.RollbackAsync(token),
                    logger);
                throw;
            }
        }

        return rolledBack;
    }
}
