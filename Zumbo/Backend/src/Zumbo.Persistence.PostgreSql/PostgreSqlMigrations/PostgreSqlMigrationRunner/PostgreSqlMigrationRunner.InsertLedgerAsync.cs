using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private async Task InsertLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Migration migration,
        CancellationToken cancellationToken)
    {
        const string sql = $"INSERT INTO {Ledger} (version, name, checksum) VALUES (@version, @name, @checksum);";
        await using var command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("version", migration.Version);
        command.Parameters.AddWithValue("name", migration.Name);
        command.Parameters.AddWithValue("checksum", migration.Checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
