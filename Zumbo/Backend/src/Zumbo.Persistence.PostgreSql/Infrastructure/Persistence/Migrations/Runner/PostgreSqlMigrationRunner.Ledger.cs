using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner
{
    private async Task EnsureLedgerAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS public.zumbo_schema_migrations (
                version bigint PRIMARY KEY,
                name text NOT NULL,
                checksum text NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT transaction_timestamp()
            );
            """;
        await using var command = CreateCommand(connection, transaction: null, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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

    private sealed record LedgerRow(long Version, string Name, string Checksum);
}
