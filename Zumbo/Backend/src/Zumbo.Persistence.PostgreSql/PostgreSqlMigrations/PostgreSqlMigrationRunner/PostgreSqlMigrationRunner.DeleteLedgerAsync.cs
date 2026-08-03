using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private async Task DeleteLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long version,
        CancellationToken cancellationToken)
    {
        const string sql = $"DELETE FROM {Ledger} WHERE version = @version;";
        await using var command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
