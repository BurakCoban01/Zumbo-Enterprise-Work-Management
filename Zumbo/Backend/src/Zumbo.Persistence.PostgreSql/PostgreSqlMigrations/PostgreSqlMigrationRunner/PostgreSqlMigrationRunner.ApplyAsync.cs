using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    public async Task<IReadOnlyList<PostgreSqlMigrationInfo>> ApplyAsync(
        CancellationToken cancellationToken = default)
    {
        var appliedNow = new List<PostgreSqlMigrationInfo>();
        var migrations = BuildMigrations();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await EnsureLedgerAsync(connection, cancellationToken);

        foreach (var migration in migrations)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireLockAsync(connection, transaction, cancellationToken);
                var applied = await ReadAppliedAsync(connection, transaction, ledgerMayBeMissing: false, cancellationToken);
                ValidateLedger(migrations, applied);
                if (applied.Any(row => row.Version == migration.Version))
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                await ExecuteAsync(connection, transaction, migration.UpSql, cancellationToken);
                await InsertLedgerAsync(connection, transaction, migration, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                appliedNow.Add(migration.Info);
            }
            catch
            {
                await PostgreSqlCompensation.RunAsync(
                    "postgres.migration_apply.rollback",
                    token => transaction.RollbackAsync(token),
                    logger);
                throw;
            }
        }

        return appliedNow;
    }
}
