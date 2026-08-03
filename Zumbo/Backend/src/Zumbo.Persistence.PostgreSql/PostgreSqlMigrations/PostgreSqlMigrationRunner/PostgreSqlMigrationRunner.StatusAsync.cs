using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    public async Task<PostgreSqlMigrationStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var migrations = BuildMigrations();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var applied = await ReadAppliedAsync(connection, transaction: null, ledgerMayBeMissing: true, cancellationToken);
        ValidateLedger(migrations, applied);
        return new PostgreSqlMigrationStatus(
            applied.Select(row => new PostgreSqlMigrationInfo(row.Version, row.Name, row.Checksum)).ToList(),
            migrations.Where(migration => !applied.Any(row => row.Version == migration.Version))
                .Select(migration => migration.Info)
                .ToList());
    }
}
