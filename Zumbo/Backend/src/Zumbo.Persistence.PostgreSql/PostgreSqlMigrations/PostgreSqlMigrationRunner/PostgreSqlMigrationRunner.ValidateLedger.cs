using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private static void ValidateLedger(IReadOnlyList<Migration> migrations, IReadOnlyList<LedgerRow> applied)
    {
        foreach (var row in applied)
        {
            var migration = migrations.SingleOrDefault(item => item.Version == row.Version)
                ?? throw new InvalidOperationException($"Database contains unknown migration {row.Version}.");
            if (!string.Equals(migration.Name, row.Name, StringComparison.Ordinal)
                || !string.Equals(migration.Checksum, row.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Migration {row.Version} does not match its recorded checksum.");
            }
        }
    }
}
