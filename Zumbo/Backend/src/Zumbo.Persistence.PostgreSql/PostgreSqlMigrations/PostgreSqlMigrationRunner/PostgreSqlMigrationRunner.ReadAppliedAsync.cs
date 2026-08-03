using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private async Task<IReadOnlyList<LedgerRow>> ReadAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool ledgerMayBeMissing,
        CancellationToken cancellationToken)
    {
        const string sql = $"SELECT version, name, checksum FROM {Ledger} ORDER BY version;";
        await using var command = CreateCommand(connection, transaction, sql);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<LedgerRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LedgerRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }

            return rows;
        }
        catch (PostgresException exception) when (ledgerMayBeMissing && exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return [];
        }
    }
}
